using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Clio;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Command.ProcessModel;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Unit tests for the <c>validate-process-graph</c> MCP tool: arg→graph mapping, finding shape,
/// safety flags, the validator's R-rule findings surfacing in the response (Story 5), and that the
/// required-package check is resolved per-call against the request environment.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class ValidateProcessGraphToolTests {
	private const string EnvName = "dev";

	private IToolCommandResolver _commandResolver;
	private IRequiredPackageChecker _checker;
	private ValidateProcessGraphTool _tool;

	[SetUp]
	public void SetUp() {
		_checker = Substitute.For<IRequiredPackageChecker>();
		_commandResolver = Substitute.For<IToolCommandResolver>();
		_commandResolver.Resolve<IRequiredPackageChecker>(Arg.Any<EnvironmentOptions>()).Returns(_checker);
		_tool = new ValidateProcessGraphTool(new ProcessGraphValidator(), _commandResolver);
	}

	private static ProcessGraphNodeArg N(string name, string type) => new(name, type);

	private static ProcessGraphEdgeArg E(string source, string target, string flowKind = "sequence") => new(source, target, flowKind);

	private ValidateProcessGraphResponse Validate(List<ProcessGraphNodeArg> nodes, List<ProcessGraphEdgeArg> edges)
		=> _tool.Validate(new ValidateProcessGraphArgs(EnvName, nodes, edges));

	[Test]
	[Category("Unit")]
	[Description("A valid Start -> Read data -> End graph returns success with zero error findings.")]
	public void Validate_ShouldReturnNoErrors_WhenGraphIsValid() {
		// Arrange
		List<ProcessGraphNodeArg> nodes = [N("s", "startEvent"), N("r", "readDataUserTask"), N("e", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [E("s", "r"), E("r", "e")];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.Success.Should().BeTrue(because: "validation of a well-formed graph must succeed");
		response.HasErrors.Should().BeFalse(because: "the canonical Start -> Read data -> End graph violates no rule");
		response.Findings.Should().NotContain(f => f.Severity == "error",
			because: "a valid graph must produce no error findings");
	}

	[Test]
	[Category("Unit")]
	[Description("R1: a start event with an incoming flow surfaces an error finding in the response.")]
	public void Validate_ShouldSurfaceR1Error_WhenStartHasIncomingFlow() {
		// Arrange
		List<ProcessGraphNodeArg> nodes = [N("s", "startEvent"), N("a", "activityUserTask"), N("e", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [E("s", "a"), E("a", "e"), E("a", "s")];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.HasErrors.Should().BeTrue(because: "a start event with an incoming flow violates R1");
		response.Findings.Should().Contain(f => f.RuleId == "R1" && f.Severity == "error" && f.NodeName == "s",
			because: "the R1 violation must be reported against the offending start node");
	}

	[Test]
	[Category("Unit")]
	[Description("An UNKNOWN flow-kind is refused rather than treated as a plain sequence flow. The parser used to fall through to sequence for anything it did not recognise, so a typo ('conditionnal') validated a graph nobody described - and in the reassuring direction, because a plain flow violates fewer rules than a conditional one. A tool whose whole job is catching a mistake before the designer is driven must not silently correct the input.")]
	public void Validate_ShouldRefuseTheCall_WhenFlowKindIsUnknown() {
		// Arrange
		List<ProcessGraphNodeArg> nodes = [N("s", "startEvent"), N("a", "activityUserTask"), N("e", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [E("s", "a"), E("a", "e", "conditionnal")];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.Success.Should().BeFalse(
			because: "an unrecognised flow-kind is a caller mistake, and answering about a different graph hides it");
		response.Error.Should().Contain("conditionnal",
			because: "the caller cannot fix a rejected value it is not shown");
		response.Error.Should().Contain("conditional",
			because: "the refusal has to name the legal values, or the caller guesses again");
		response.HasErrors.Should().BeNull(
			because: "the graph was never validated, so there is no answer about its rules - and while this "
				+ "was a non-nullable bool the payload said \"has-errors\": false, which this tool's own "
				+ "description advertises and the shipped prompt tells the agent to branch on, so a graph "
				+ "nobody looked at read as a graph with nothing wrong");
	}

	[Test]
	[Category("Unit")]
	[Description("An OMITTED flow-kind is still a plain sequence flow. That is the documented default and the common case, so refusing an unknown kind must not turn the optional field into a required one.")]
	public void Validate_ShouldTreatAnOmittedFlowKindAsSequence() {
		// Arrange
		List<ProcessGraphNodeArg> nodes = [N("s", "startEvent"), N("r", "readDataUserTask"), N("e", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [new("s", "r", null), new("r", "e", null)];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.Success.Should().BeTrue(because: "flow-kind is optional and defaults to a plain sequence flow");
		response.HasErrors.Should().BeFalse(
			because: "Start -> Read data -> End over two plain flows violates no rule");
	}

	[Test]
	[Category("Unit")]
	[Description("R14: a default flow with no sibling conditional surfaces an error where the source actually BRANCHES. The source here is an activity with two outgoing flows, not a gateway, so the finding is R14's own and not the or-gateway flow-kind rule's.")]
	public void Validate_ShouldSurfaceR14Error_WhenDivergingSourceHasADefaultWithNoConditional() {
		// Arrange
		List<ProcessGraphNodeArg> nodes = [N("s", "startEvent"), N("a", "activityUserTask"), N("b", "activityUserTask"),
			N("c", "activityUserTask"), N("e", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [E("s", "a"), E("a", "b", "default"), E("a", "c"), E("b", "e"), E("c", "e")];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.Findings.Should().Contain(f => f.RuleId == "R14" && f.Severity == "error",
			because: "a default branch says 'taken when nothing else matched', so with no conditional sibling "
				+ "there is nothing for it to be the fallback of - and this also proves the 'default' "
				+ "flow-kind was parsed");
	}

	[Test]
	[Category("Unit")]
	[Description("R14 is scoped by ARITY, and that scope is the fix rather than a refinement. A CONVERGING or-gateway's single outgoing flow is a default flow by construction: the designer's allowed-outgoing list for an or-gateway is conditional + default with no plain sequence flow at all, so there is no other kind it could have. Unscoped, this rule called 45 shipped gateways invalid - 40 exclusive and 5 inclusive, among them BulkFileManagement/DeleteFilesInTable and CaseService/RunSendEmailToCaseGroup.")]
	public void Validate_ShouldNotSurfaceR14_ForAConvergingGatewayWithOneDefaultFlow() {
		// Arrange: two branches merge into one exclusive gateway, whose single outgoing flow is the default.
		List<ProcessGraphNodeArg> nodes = [N("s", "startEvent"), N("split", "exclusiveGateway"),
			N("a", "activityUserTask"), N("b", "activityUserTask"), N("merge", "exclusiveGateway"), N("e", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [E("s", "split"),
			new ProcessGraphEdgeArg("split", "a", "conditional", "1 > 0"),
			E("split", "b", "default"), E("a", "merge", "default"), E("b", "merge", "default"),
			E("merge", "e", "default")];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.Findings.Should().NotContain(f => f.RuleId == "R14" && f.NodeName == "merge",
			because: "a converging gateway has exactly one way out and the designer cannot draw a plain flow "
				+ "there, so its single default flow is the only shape available - calling it invalid rejects "
				+ "content the designer itself produces");
	}

	[Test]
	[Category("Unit")]
	[Description("R14: at most one default flow per source. The default is the branch taken when nothing matched, so two make that undecidable; the platform does not refuse it and picks by collection order, which leaves the second one dead metadata that reads like a live branch. Zero sources in the shipped corpus carry two.")]
	public void Validate_ShouldSurfaceR14Error_WhenASourceHasTwoDefaultFlows() {
		// Arrange
		List<ProcessGraphNodeArg> nodes = [N("s", "startEvent"), N("g", "exclusiveGateway"),
			N("a", "activityUserTask"), N("b", "activityUserTask"), N("e", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [E("s", "g"), E("g", "a", "default"), E("g", "b", "default"),
			E("a", "e"), E("b", "e")];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.Findings.Should().Contain(
			f => f.RuleId == "R14" && f.Severity == "error" && f.Message.Contains("2 default flows"),
			because: "two fallbacks out of one element make 'the branch taken when nothing matched' undecidable");
	}

	[Test]
	[Category("Unit")]
	[Description("R7: a DIVERGING exclusive gateway may not carry a plain sequence flow - the mirror of R11. From an or-gateway the designer offers conditional and default only and removes the plain connection from the menu, so a plain flow there says nothing about how the branch is chosen.")]
	public void Validate_ShouldSurfaceR7Error_WhenADivergingGatewayHasAPlainFlow() {
		// Arrange
		List<ProcessGraphNodeArg> nodes = [N("s", "startEvent"), N("g", "exclusiveGateway"),
			N("a", "activityUserTask"), N("b", "activityUserTask"), N("e", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [E("s", "g"),
			new ProcessGraphEdgeArg("g", "a", "conditional", "1 > 0"), E("g", "b"), E("a", "e"), E("b", "e")];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.Findings.Should().Contain(
			f => f.RuleId == "R7" && f.Severity == "error" && f.Message.Contains("plain sequence flow"),
			because: "every outgoing flow from a gateway that chooses has to say how it is chosen");
	}

	[Test]
	[Category("Unit")]
	[Description("The or-gateway flow-kind rule is arity-scoped like R14: 14 shipped exclusive gateways carry a single plain sequence flow, all of them with exactly ONE outgoing - legacy converging gateways from an older designer, tolerated on read.")]
	public void Validate_ShouldNotSurfaceR7Error_ForALegacyConvergingGatewayWithOnePlainFlow() {
		// Arrange
		List<ProcessGraphNodeArg> nodes = [N("s", "startEvent"), N("split", "exclusiveGateway"),
			N("a", "activityUserTask"), N("b", "activityUserTask"), N("merge", "exclusiveGateway"), N("e", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [E("s", "split"),
			new ProcessGraphEdgeArg("split", "a", "conditional", "1 > 0"),
			E("split", "b", "default"), E("a", "merge"), E("b", "merge"), E("merge", "e")];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.Findings.Should().NotContain(f => f.RuleId == "R7" && f.NodeName == "merge",
			because: "a gateway with one way out is not choosing anything, so the flow-kind rule has nothing "
				+ "to say about it");
	}

	[Test]
	[Category("Unit")]
	[Description("R7 names the actual run-time failure rather than saying the process 'dead-ends'. FlowConditionalGateway.OnVisited throws MismatchItemsCountException when no condition matched and no default branch exists, and nothing earlier objects - the platform's own interpretation validator has no branch-coverage rule. Stays a WARNING, because 65 shipped exclusive gateways deliberately have two conditional flows and no default.")]
	public void Validate_ShouldWarnR7_NamingTheRuntimeException_WhenADivergingGatewayHasNoDefault() {
		// Arrange
		List<ProcessGraphNodeArg> nodes = [N("s", "startEvent"), N("g", "exclusiveGateway"),
			N("a", "activityUserTask"), N("b", "activityUserTask"), N("e", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [E("s", "g"),
			new ProcessGraphEdgeArg("g", "a", "conditional", "1 > 0"),
			new ProcessGraphEdgeArg("g", "b", "conditional", "2 > 1"), E("a", "e"), E("b", "e")];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.Findings.Should().Contain(
			f => f.RuleId == "R7" && f.Severity == "warning" && f.Message.Contains("MismatchItemsCountException"),
			because: "the consequence is specific and findable, and naming it is what lets a reader search for "
				+ "it - 'dead-ends' names nothing");
	}

	[Test]
	[Category("Unit")]
	[Description("R15: a flow from an element to itself is refused. The designer refuses to DRAW one while tolerating the three that exist in the shipped corpus on re-save; this tool only ever sees a PLANNED graph, so only the authoring half applies. At run time a self-looping task re-executes on every completion, and nothing on the diagram shows it, because the layout engine skips self-loops.")]
	public void Validate_ShouldSurfaceR15Error_ForASelfLoop() {
		// Arrange
		List<ProcessGraphNodeArg> nodes = [N("s", "startEvent"), N("a", "activityUserTask"), N("e", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [E("s", "a"), E("a", "a"), E("a", "e")];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.Findings.Should().Contain(
			f => f.RuleId == "R15" && f.Severity == "error" && f.Message.Contains("to itself"),
			because: "a self-loop either never runs or runs forever, and it is invisible on the diagram");
	}

	[Test]
	[Category("Unit")]
	[Description("R13: a conditional flow whose condition is supplied but EMPTY is an error. The platform does not report this - it substitutes the literal 'true', producing a branch that looks conditional and always fires, and 7 shipped flows are in that state. The rule needs the optional 'condition' field, which is why it could not exist before.")]
	public void Validate_ShouldSurfaceR13Error_ForAConditionalFlowWithAnEmptyCondition() {
		// Arrange
		List<ProcessGraphNodeArg> nodes = [N("s", "startEvent"), N("g", "exclusiveGateway"),
			N("a", "activityUserTask"), N("b", "activityUserTask"), N("e", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [E("s", "g"),
			new ProcessGraphEdgeArg("g", "a", "conditional", "   "),
			E("g", "b", "default"), E("a", "e"), E("b", "e")];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.Findings.Should().Contain(
			f => f.RuleId == "R13" && f.Severity == "error" && f.Message.Contains("literal 'true'"),
			because: "a branch that always fires is the opposite of the branch the author described");
	}

	[Test]
	[Category("Unit")]
	[Description("An OMITTED condition raises nothing. The field is optional and purely additive, so a caller describing a graph's SHAPE rather than its predicates must not be flooded with findings about conditions they never claimed to supply.")]
	public void Validate_ShouldNotSurfaceR13Error_WhenTheConditionIsSimplyOmitted() {
		// Arrange
		List<ProcessGraphNodeArg> nodes = [N("s", "startEvent"), N("g", "exclusiveGateway"),
			N("a", "activityUserTask"), N("b", "activityUserTask"), N("e", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [E("s", "g"), E("g", "a", "conditional"), E("g", "b", "default"),
			E("a", "e"), E("b", "e")];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.Findings.Should().NotContain(f => f.RuleId == "R13" && f.Message.Contains("literal 'true'"),
			because: "omitting an optional field is not the same as supplying an empty one");
	}

	[Test]
	[Category("Unit")]
	[Description("R8 (warning): a parallel join whose incoming branches come from a common exclusive split can deadlock. The join proceeds only when EVERY incoming branch has delivered a token, and an exclusive split takes one - so the instance hangs in Running with no exception and no log line, which is the failure mode with no diagnostic at all.")]
	public void Validate_ShouldWarnR8_WhenAParallelJoinMergesBranchesOfAnExclusiveSplit() {
		// Arrange: xor splits to a and b, both of which run into an AND join.
		List<ProcessGraphNodeArg> nodes = [N("s", "startEvent"), N("xor", "exclusiveGateway"),
			N("a", "activityUserTask"), N("b", "activityUserTask"), N("and", "parallelGateway"), N("e", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [E("s", "xor"),
			new ProcessGraphEdgeArg("xor", "a", "conditional", "1 > 0"),
			E("xor", "b", "default"), E("a", "and"), E("b", "and"), E("and", "e")];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.Findings.Should().Contain(
			f => f.RuleId == "R8" && f.Severity == "warning" && f.Message.Contains("hang in Running"),
			because: "an AND join behind an XOR split waits for a branch that will never run, and nothing "
				+ "anywhere reports it");
	}

	[Test]
	[Category("Unit")]
	[Description("A genuine AND split joined by an AND gateway raises no deadlock warning - the shape the rule must not punish, since both branches really do run.")]
	public void Validate_ShouldNotWarnR8_ForAGenuineParallelSplitAndJoin() {
		// Arrange
		List<ProcessGraphNodeArg> nodes = [N("s", "startEvent"), N("fork", "parallelGateway"),
			N("a", "activityUserTask"), N("b", "activityUserTask"), N("join", "parallelGateway"), N("e", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [E("s", "fork"), E("fork", "a"), E("fork", "b"),
			E("a", "join"), E("b", "join"), E("join", "e")];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.Findings.Should().NotContain(f => f.RuleId == "R8",
			because: "both branches of an AND split always run, so the join always completes");
	}

	[Test]
	[Category("Unit")]
	[Description("R13: a conditional flow from a start event surfaces an error (proves the 'conditional' flow-kind was parsed).")]
	public void Validate_ShouldSurfaceR13Error_WhenConditionalFlowFromStart() {
		// Arrange
		List<ProcessGraphNodeArg> nodes = [N("s", "startEvent"), N("a", "activityUserTask"), N("e", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [E("s", "a", "conditional"), E("a", "e")];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.Findings.Should().Contain(f => f.RuleId == "R13" && f.Severity == "error",
			because: "a conditional flow may originate only from a gateway or activity (R13)");
	}

	[Test]
	[Category("Unit")]
	[Description("R15: an orphan node that cannot reach an end surfaces an error finding.")]
	public void Validate_ShouldSurfaceR15Error_WhenNodeIsOrphan() {
		// Arrange
		List<ProcessGraphNodeArg> nodes =
			[N("s", "startEvent"), N("r", "readDataUserTask"), N("e", "endEvent"), N("orphan", "activityUserTask")];
		List<ProcessGraphEdgeArg> edges = [E("s", "r"), E("r", "e")];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.Findings.Should().Contain(f => f.RuleId == "R15" && f.Severity == "error" && f.NodeName == "orphan",
			because: "an unreachable node violates R15");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-ERR: an edge referencing a missing node returns a finding (R15), not an unhandled exception.")]
	public void Validate_ShouldReturnFinding_WhenEdgeReferencesMissingNode() {
		// Arrange
		List<ProcessGraphNodeArg> nodes = [N("s", "startEvent"), N("r", "readDataUserTask"), N("e", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [E("s", "r"), E("r", "e"), E("r", "ghost")];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.Success.Should().BeTrue(because: "malformed graphs are reported as findings, not exceptions");
		response.Findings.Should().Contain(f => f.RuleId == "R15" && f.Severity == "error",
			because: "a flow referencing a missing node must surface as an R15 finding (every flow needs a valid source/target)");
	}

	[Test]
	[Category("Unit")]
	[Description("DUP: two nodes sharing a name surface a duplicate-name error finding (and the call still succeeds, not throws).")]
	public void Validate_ShouldSurfaceDupError_WhenTwoNodesShareAName() {
		// Arrange
		List<ProcessGraphNodeArg> nodes =
			[N("s", "startEvent"), N("a", "activityUserTask"), N("a", "readDataUserTask"), N("e", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [E("s", "a"), E("a", "e")];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.Success.Should().BeTrue(because: "a duplicate name is reported as a finding, not an unhandled exception");
		response.Findings.Should().Contain(f => f.RuleId == "DUP" && f.Severity == "error" && f.NodeName == "a",
			because: "the duplicate element name must surface as a DUP error against the offending node");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-08: a node with an unrecognized type surfaces an UNKNOWN error finding rather than being silently accepted.")]
	public void Validate_ShouldSurfaceUnknownError_WhenNodeTypeIsUnrecognized() {
		// Arrange
		List<ProcessGraphNodeArg> nodes = [N("s", "startEvent"), N("x", "totallyBogusType"), N("e", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [E("s", "x"), E("x", "e")];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.Success.Should().BeTrue(because: "an unknown type is reported as a finding, not an exception");
		response.Findings.Should().Contain(f => f.RuleId == "UNKNOWN" && f.Severity == "error" && f.NodeName == "x",
			because: "an unrecognized element type must surface as an UNKNOWN error against the offending node");
	}

	[Test]
	[Category("Unit")]
	[Description("The tool carries the read-only, non-destructive, idempotent, closed-world safety flags.")]
	public void ValidateTool_ShouldCarryReadOnlySafetyFlags_WhenInspected() {
		// Arrange
		MethodInfo method = typeof(ValidateProcessGraphTool).GetMethod(nameof(ValidateProcessGraphTool.Validate));
		McpServerToolAttribute attribute = method!.GetCustomAttribute<McpServerToolAttribute>();

		// Assert
		attribute.Should().NotBeNull(because: "the validate method must be exposed as an MCP tool");
		attribute!.ReadOnly.Should().BeTrue(because: "validation performs no mutation");
		attribute.Destructive.Should().BeFalse(because: "validation never changes state");
		attribute.Idempotent.Should().BeTrue(because: "validating the same graph always yields the same result");
		attribute.OpenWorld.Should().BeFalse(because: "validation is a closed, in-memory operation");
	}

	[Test]
	[Category("Unit")]
	[Description("The required-package checker is resolved per-call against the environment named in the request args, not from the startup container.")]
	public void Validate_ShouldResolveCheckerForRequestEnvironment_WhenInvoked() {
		// Arrange
		List<ProcessGraphNodeArg> nodes = [N("s", "startEvent"), N("e", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [E("s", "e")];

		// Act
		Validate(nodes, edges);

		// Assert
		_commandResolver.Received(1).Resolve<IRequiredPackageChecker>(
			Arg.Is<EnvironmentOptions>(o => o.Environment == EnvName));
		_checker.Received(1).EnsureRequirements(Arg.Any<object>());
	}

	[Test]
	[Category("Unit")]
	[Description("When the required package is absent the checker throws PackageRequirementException; the tool returns success=false with that message and does not validate the graph.")]
	public void Validate_ShouldReturnFailureAndSkipValidation_WhenRequiredPackageIsMissing() {
		// Arrange
		IProcessGraphValidator validator = Substitute.For<IProcessGraphValidator>();
		ValidateProcessGraphTool tool = new(validator, _commandResolver);
		const string message = "Package 'CrtProcessBuilder' is required. Run 'clio install-process-builder -e dev'";
		_checker.When(c => c.EnsureRequirements(Arg.Any<object>()))
			.Do(_ => throw new PackageRequirementException(message));

		// Act
		ValidateProcessGraphResponse response = tool.Validate(new ValidateProcessGraphArgs(EnvName, [N("s", "startEvent")], []));

		// Assert
		response.Success.Should().BeFalse(because: "a missing required package must fail the call cleanly");
		response.Error.Should().Be(message, because: "the install hint from the package check must surface verbatim");
		validator.DidNotReceive().Validate(Arg.Any<ProcessGraph>());
	}

	[Test]
	[Category("Unit")]
	[Description("When the requested environment is unknown the resolver throws InvalidOperationException; the tool surfaces that message as success=false and does not validate the graph.")]
	public void Validate_ShouldReturnFailureAndSkipValidation_WhenEnvironmentIsUnknown() {
		// Arrange
		IProcessGraphValidator validator = Substitute.For<IProcessGraphValidator>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		const string message = "Environment 'ghost' was not found.";
		resolver.Resolve<IRequiredPackageChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new InvalidOperationException(message));
		ValidateProcessGraphTool tool = new(validator, resolver);

		// Act
		ValidateProcessGraphResponse response = tool.Validate(new ValidateProcessGraphArgs("ghost", [N("s", "startEvent")], []));

		// Assert
		response.Success.Should().BeFalse(because: "an unknown environment must fail the call cleanly");
		response.Error.Should().Be(message, because: "the resolver's friendly environment-not-found message must surface verbatim");
		validator.DidNotReceive().Validate(Arg.Any<ProcessGraph>());
	}
}
