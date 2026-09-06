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
		response.Findings.Should().Contain(
			f => f.RuleId == "R14" && f.Severity == "error" && f.Message.Contains("sibling conditional"),
			because: "a default branch says 'taken when nothing else matched', so with no conditional sibling "
				+ "there is nothing for it to be the fallback of - and this also proves the 'default' "
				+ "flow-kind was parsed. R14 now reports two different defects, so the message is what says "
				+ "which one fired");
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
		response.HasErrors.Should().BeFalse(
			because: "the whole graph is a canonical conditional+default split feeding a converging gateway - "
				+ "the shape the designer produces - so asserting the WHOLE error surface rather than one "
				+ "rule id is what stops a future rule from rejecting it by another name");
	}

	[Test]
	[Category("Unit")]
	[Description("R14 does not fire when a plain sibling leads into a GATEWAY. That is not an unexpressed decision, it is the decision living one element further on, and the platform says so: ProcessSchemaFlowNode.GetOutgoingsDefFlows, with no conditional flow present, recurses into a sequence flow whose target is a gateway and collects THAT gateway's default flows. This is the shape of CrtLeadOppMgmtApp/LeadDistribution's ReadDataUserTask1 - the ONE shipped process the arity fix still rejected, which is how 45 became 1 rather than 0.")]
	public void Validate_ShouldNotSurfaceR14_WhenAPlainSiblingLeadsIntoAGateway() {
		// Arrange: a read-data task with a default branch and a plain flow into a gateway.
		List<ProcessGraphNodeArg> nodes = [N("s", "startEvent"), N("read", "readDataUserTask"),
			N("a", "activityUserTask"), N("gw", "exclusiveGateway"), N("b", "activityUserTask"),
			N("c", "activityUserTask"), N("e", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [E("s", "read"), E("read", "a", "default"), E("read", "gw"),
			new ProcessGraphEdgeArg("gw", "b", "conditional", "1 > 0"), E("gw", "c", "default"),
			E("a", "e"), E("b", "e"), E("c", "e")];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.Findings.Should().NotContain(f => f.RuleId == "R14" && f.NodeName == "read",
			because: "the default branch has something to be the fallback OF - the conditions are inside the "
				+ "gateway the plain sibling leads to, and the platform walks into it to find them");
		response.HasErrors.Should().BeFalse(
			because: "this is a shipped, running process, and the rule that rejected it is the one this "
				+ "change exists to scope correctly");
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
		response.Findings.Should().NotContain(f => f.Message.Contains("sibling conditional"),
			because: "the sibling-conditional half of R14 is scoped to exactly ONE default, and this is what "
				+ "that clause decides: with two of them the source's problem is the second default, not a "
				+ "missing condition, and reporting both would send the caller to add a conditional flow "
				+ "beside a fallback that is already ambiguous. Without this assertion the clause can be "
				+ "deleted with the suite green");
	}

	[Test]
	[Category("Unit")]
	[Description("R7: a DIVERGING exclusive gateway carrying a plain sequence flow is WARNED about, not refused - the mirror of R11, softened by measurement. The designer offers conditional and default only out of an or-gateway and removes the plain connection from the menu, so the flow says nothing about how its branch is chosen; but seven shipped or-gateways are in exactly that shape and they run, because the runtime takes any non-conditional outgoing as the default branch.")]
	public void Validate_ShouldWarnR7_WhenADivergingGatewayHasAPlainFlow() {
		// Arrange
		List<ProcessGraphNodeArg> nodes = [N("s", "startEvent"), N("g", "exclusiveGateway"),
			N("a", "activityUserTask"), N("b", "activityUserTask"), N("e", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [E("s", "g"),
			new ProcessGraphEdgeArg("g", "a", "conditional", "1 > 0"), E("g", "b"), E("a", "e"), E("b", "e")];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.Findings.Should().Contain(
			f => f.RuleId == "R7" && f.Severity == "warning" && f.Message.Contains("plain sequence flow"),
			because: "the diagram should say which branch is the fallback - but a WARNING, because seven "
				+ "shipped or-gateways are diverging and carry a plain flow, and they run: the runtime takes "
				+ "any non-conditional outgoing as the default. An error here would reject real content, "
				+ "which is the defect the R14 arity scope in this same change exists to undo");
		response.HasErrors.Should().BeFalse(
			because: "a shape the shipped corpus contains seven times over must not fail validation");
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
		response.HasErrors.Should().BeFalse(
			because: "14 shipped exclusive gateways carry exactly this single plain flow, so the whole graph "
				+ "must come back clean and not merely free of one rule id");
	}

	[Test]
	[Category("Unit")]
	[Description("R7 names the run-time outcome the OPERATOR can search for, not the internal exception type. The rule used to promise MismatchItemsCountException, read out of FlowConditionalGateway.OnVisited; a manual run on a stand saw validate-process-graph say that and the resulting SysProcessLog entry say 'None of the conditions were met after the element ...' instead - and record the instance as SUSPENDED, not failed. Stays a WARNING, because 65 shipped exclusive gateways deliberately have two conditional flows and no default.")]
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
			f => f.RuleId == "R7" && f.Severity == "warning"
				&& f.Message.Contains("None of the conditions were met after the element"),
			because: "the consequence is specific and findable, and naming it is what lets a reader search for "
				+ "it - 'dead-ends' names nothing");
		Validate([N("s", "startEvent"), N("split", "exclusiveGateway"), N("x", "activityUserTask"),
				N("y", "activityUserTask"), N("e", "endEvent")],
			[E("s", "split"), new ProcessGraphEdgeArg("split", "x", "conditional", "1 > 0"),
				E("split", "y", "default"), E("x", "e"), E("y", "e")])
			.Findings.Should().NotContain(f => f.RuleId == "R7" && f.NodeName == "split",
				because: "the guard that asks whether a default EXISTS was unfalsifiable until this line: "
					+ "replacing it with `if (true)` left the whole suite green while the warning fired on "
					+ "every diverging gateway, the canonical conditional+default split included");
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
			f => f.RuleId == "R13" && f.Severity == "error" && f.Message.Contains("literal 'true'")
				&& f.Source == "g" && f.Target == "a",
			because: "a branch that always fires is the opposite of the branch the author described - and the "
				+ "source/target are how an agent finds the offending flow, so they are asserted rather than "
				+ "assumed");
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
			f => f.RuleId == "R8" && f.Severity == "warning" && f.NodeName == "and"
				&& f.Message.Contains("hang in Running"),
			because: "an AND join behind an XOR split waits for a branch that will never run, and nothing "
				+ "anywhere reports it - the finding names the JOIN, which is the element to change");
	}

	[Test]
	[Category("Unit")]
	[Description("R8 fires when the or-gateway feeds the parallel join DIRECTLY. One arm goes through an activity and the other jumps straight to the join - the commonest hand-authored deadlock of the family, and the one shape the rule missed, because the backward walk started at the inbound edge's SOURCE and so never contained the inbound edge itself. The direct branch projected to nothing at the gateway and was discarded before any pair could form.")]
	public void Validate_ShouldWarnR8_WhenAnOrGatewayFeedsTheJoinDirectly() {
		// Arrange: xor picks ONE arm; the join waits for both.
		List<ProcessGraphNodeArg> nodes = [N("s", "startEvent"), N("xor", "exclusiveGateway"),
			N("a", "activityUserTask"), N("join", "parallelGateway"), N("e", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [E("s", "xor"),
			new ProcessGraphEdgeArg("xor", "a", "conditional", "1 > 0"), E("xor", "join", "default"),
			E("a", "join"), E("join", "e")];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.Findings.Should().Contain(
			f => f.RuleId == "R8" && f.Severity == "warning" && f.NodeName == "join"
				&& f.Message.Contains("xor"),
			because: "the two branches leave 'xor' by different flows and the gateway takes only one, so "
				+ "whichever way it goes the join is still waiting for the other - the instance hangs in "
				+ "Running with no error, which is why the warning has to name the gateway");
	}

	[Test]
	[Category("Unit")]
	[Description("R8 must NOT fire when a parallel section sits downstream of a choice. Both branches of the fork run whenever the choice reaches it, and when the choice goes the other way no token reaches the join at all - there is no deadlock in either case. This is the graph that proves the rule compares DIVERGENCE and not ancestry: for a genuine AND fork the two backward walks are identical from the fork upward, so they contain every or-gateway in the process behind it, and a node-level intersection warns on almost any real graph.")]
	public void Validate_ShouldNotWarnR8_WhenAParallelForkSitsBelowAChoice() {
		// Arrange: xor picks between a parallel section and a plain branch.
		List<ProcessGraphNodeArg> nodes = [N("s", "startEvent"), N("xor", "exclusiveGateway"),
			N("fork", "parallelGateway"), N("a", "activityUserTask"), N("b", "activityUserTask"),
			N("join", "parallelGateway"), N("other", "activityUserTask"), N("e", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [E("s", "xor"),
			new ProcessGraphEdgeArg("xor", "fork", "conditional", "1 > 0"), E("xor", "other", "default"),
			E("fork", "a"), E("fork", "b"), E("a", "join"), E("b", "join"), E("join", "e"), E("other", "e")];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.Findings.Should().NotContain(f => f.RuleId == "R8",
			because: "both arms of the fork reach the join through the SAME flow out of the xor, so the xor "
				+ "never chooses between them - warning here would tell an agent to replace a correct AND "
				+ "join with an XOR one, which fires everything downstream twice");
	}

	[Test]
	[Category("Unit")]
	[Description("R8 must NOT fire when a CONVERGING or-gateway sits upstream of a parallel section. The gateway has one way out, so it chooses nothing; this is the 45-shipped-gateway shape, and it is what the rule's own or-gateway arity guard exists to exempt.")]
	public void Validate_ShouldNotWarnR8_WhenAConvergingGatewayFeedsAParallelFork() {
		// Arrange
		List<ProcessGraphNodeArg> nodes = [N("s", "startEvent"), N("merge", "exclusiveGateway"),
			N("fork", "parallelGateway"), N("a", "activityUserTask"), N("b", "activityUserTask"),
			N("join", "parallelGateway"), N("e", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [E("s", "merge"), E("merge", "fork", "default"),
			E("fork", "a"), E("fork", "b"), E("a", "join"), E("b", "join"), E("join", "e")];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.Findings.Should().NotContain(f => f.RuleId == "R8",
			because: "a gateway with one way out picks nothing, so it cannot starve a join");
		response.HasErrors.Should().BeFalse(
			because: "this is a shape the designer itself produces, so no rule may call it invalid");
	}

	[Test]
	[Category("Unit")]
	[Description("R8 must NOT fire on a retry loop. The backward walk follows the back-edge, so the loop's own exclusive gateway ends up behind BOTH branches of any parallel section inside the loop - ancestry again, not divergence. Back-edges are in 15% of real gateway processes.")]
	public void Validate_ShouldNotWarnR8_ForAParallelSectionInsideARetryLoop() {
		// Arrange: fork/join inside a loop whose exit is decided by an exclusive gateway.
		List<ProcessGraphNodeArg> nodes = [N("s", "startEvent"), N("fork", "parallelGateway"),
			N("a", "activityUserTask"), N("b", "activityUserTask"), N("join", "parallelGateway"),
			N("retry", "exclusiveGateway"), N("e", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [E("s", "fork"), E("fork", "a"), E("fork", "b"),
			E("a", "join"), E("b", "join"), E("join", "retry"),
			new ProcessGraphEdgeArg("retry", "fork", "conditional", "1 > 0"), E("retry", "e", "default")];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.Findings.Should().NotContain(f => f.RuleId == "R8",
			because: "both arms re-enter the loop through the same flow out of the retry gateway");
	}

	[Test]
	[Category("Unit")]
	[Description("The INCLUSIVE gateway's half of every rule this change touches, in one graph: R9 rather than R7 is the rule id, the no-default warning names the runtime exception, and the arity scope holds for a converging inclusive gateway too. Without this the 'R7 : R9' selector, the inclusive arm of the or-gateway guard and the inclusive arm of the R14 arity scope are all unexecuted - and 5 of the 45 shipped counter-examples are inclusive gateways.")]
	public void Validate_ShouldSurfaceR9_ForAnInclusiveGateway_AndScopeItByArity() {
		// Arrange
		List<ProcessGraphNodeArg> nodes = [N("s", "startEvent"), N("split", "inclusiveGateway"),
			N("a", "activityUserTask"), N("b", "activityUserTask"), N("merge", "inclusiveGateway"),
			N("e", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [E("s", "split"),
			new ProcessGraphEdgeArg("split", "a", "conditional", "1 > 0"),
			new ProcessGraphEdgeArg("split", "b", "conditional", "2 > 1"),
			E("a", "merge"), E("b", "merge"), E("merge", "e", "default")];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.Findings.Should().Contain(
			f => f.RuleId == "R9" && f.Severity == "warning" && f.NodeName == "split"
				&& f.Message.Contains("None of the conditions were met after the element"),
			because: "an inclusive gateway reports R9, not R7, and the warning quotes the process-log line the "
				+ "operator will actually read rather than an exception type they cannot search for");
		response.Findings.Should().NotContain(f => f.NodeName == "merge",
			because: "the converging inclusive gateway has one way out, so every arity-scoped rule leaves "
				+ "it alone - the same exemption the exclusive one gets");
		response.HasErrors.Should().BeFalse(
			because: "two conditional branches with no default is legal - 65 shipped exclusive gateways are "
				+ "in exactly that shape, which is why R7/R9 is a warning");
	}

	[Test]
	[Category("Unit")]
	[Description("An omitted flow-kind is a plain SEQUENCE flow, and the graph is diverging so the answer is discriminating. The previous arrangement was a straight chain, where one outgoing flow per node makes sequence, conditional and default indistinguishable - the R14 arity scope this change introduced is what took that test's discriminating power away.")]
	public void Validate_ShouldTreatAnOmittedFlowKindAsSequence_OnADivergingSource() {
		// Arrange
		List<ProcessGraphNodeArg> nodes = [N("s", "startEvent"), N("a", "activityUserTask"),
			N("e1", "endEvent"), N("e2", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [E("s", "a"),
			new ProcessGraphEdgeArg("a", "e1"), new ProcessGraphEdgeArg("a", "e2")];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.HasErrors.Should().BeFalse(
			because: "two plain flows out of an activity are an implicit parallel split, which is legal - "
				+ "read as DEFAULT they would be two default flows and two R14 errors");
		response.Findings.Should().Contain(f => f.RuleId == "R12" && f.Severity == "warning",
			because: "R12 warns about the implicit parallel split, and it fires only for SEQUENCE flows - "
				+ "read as conditional there would be no finding at all");
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
