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
	[Description("R14: a default flow with no sibling conditional (conditional/default flow-kinds are parsed) surfaces an error.")]
	public void Validate_ShouldSurfaceR14Error_WhenDefaultFlowHasNoSiblingConditional() {
		// Arrange
		List<ProcessGraphNodeArg> nodes = [N("s", "startEvent"), N("g", "exclusiveGateway"), N("a", "activityUserTask"), N("e", "endEvent")];
		List<ProcessGraphEdgeArg> edges = [E("s", "g"), E("g", "a", "default"), E("a", "e")];

		// Act
		ValidateProcessGraphResponse response = Validate(nodes, edges);

		// Assert
		response.Findings.Should().Contain(f => f.RuleId == "R14" && f.Severity == "error",
			because: "a lone default flow violates R14 — and proves the 'default' flow-kind was parsed");
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
