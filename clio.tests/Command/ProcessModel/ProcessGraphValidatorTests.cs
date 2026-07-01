using System.Collections.Generic;
using Clio.Command.ProcessModel;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// Unit tests for <see cref="ProcessGraphValidator"/> — one case per error/warning rule (R1–R17),
/// the clean Start->Read data->End graph, and the no-false-positive guarantee.
/// </summary>
[TestFixture]
[Property("Module", "ProcessModel")]
[Category("Unit")]
public sealed class ProcessGraphValidatorTests {
	private readonly IProcessGraphValidator _validator = new ProcessGraphValidator();

	private static ProcessGraphNode Node(string name, string type) => new(name, type);

	private static ProcessGraphEdge Seq(string from, string to) => new(from, to, ProcessFlowKind.Sequence);

	private static ProcessGraphEdge Cond(string from, string to) => new(from, to, ProcessFlowKind.Conditional);

	private static ProcessGraphEdge Def(string from, string to) => new(from, to, ProcessFlowKind.Default);

	private ProcessGraphValidationResult Validate(IReadOnlyList<ProcessGraphNode> nodes, IReadOnlyList<ProcessGraphEdge> edges)
		=> _validator.Validate(new ProcessGraph(nodes, edges));

	[Test]
	[Category("Unit")]
	[Description("A valid Start -> Read data -> End graph produces zero error findings.")]
	public void Validate_ShouldReturnNoErrors_WhenStartReadDataEndGraphIsValid() {
		// Arrange
		List<ProcessGraphNode> nodes = [Node("s", "startEvent"), Node("r", "readDataUserTask"), Node("e", "endEvent")];
		List<ProcessGraphEdge> edges = [Seq("s", "r"), Seq("r", "e")];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.HasErrors.Should().BeFalse(
			because: "a Start -> Read data -> End graph is the canonical valid minimal process");
		result.Findings.Should().NotContain(f => f.Severity == ProcessGraphSeverity.Error,
			because: "no rule is violated by the canonical valid graph");
	}

	[Test]
	[Category("Unit")]
	[Description("R1: a start event with an incoming flow is an error.")]
	public void Validate_ShouldReturnR1Error_WhenStartHasIncomingFlow() {
		// Arrange
		List<ProcessGraphNode> nodes = [Node("s", "startEvent"), Node("a", "activityUserTask"), Node("e", "endEvent")];
		List<ProcessGraphEdge> edges = [Seq("s", "a"), Seq("a", "e"), Seq("a", "s")];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().Contain(f => f.RuleId == "R1" && f.Severity == ProcessGraphSeverity.Error && f.NodeName == "s",
			because: "a start event must not have an incoming flow (R1)");
	}

	[Test]
	[Category("Unit")]
	[Description("R2: an end event with an outgoing flow is an error.")]
	public void Validate_ShouldReturnR2Error_WhenEndHasOutgoingFlow() {
		// Arrange
		List<ProcessGraphNode> nodes = [Node("s", "startEvent"), Node("r", "readDataUserTask"), Node("e", "endEvent")];
		List<ProcessGraphEdge> edges = [Seq("s", "r"), Seq("r", "e"), Seq("e", "r")];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().Contain(f => f.RuleId == "R2" && f.Severity == ProcessGraphSeverity.Error && f.NodeName == "e",
			because: "an end event must not have an outgoing flow (R2)");
	}

	[Test]
	[Category("Unit")]
	[Description("R15: a flow referencing a missing node is an error rather than an exception (every flow needs a valid source/target).")]
	public void Validate_ShouldReturnR15Error_WhenEdgeReferencesMissingNode() {
		// Arrange
		List<ProcessGraphNode> nodes = [Node("s", "startEvent"), Node("r", "readDataUserTask"), Node("e", "endEvent")];
		List<ProcessGraphEdge> edges = [Seq("s", "r"), Seq("r", "e"), Seq("r", "ghost")];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().Contain(f => f.RuleId == "R15" && f.Severity == ProcessGraphSeverity.Error,
			because: "a flow whose endpoint is not a node must be flagged (R15: every flow needs a valid source/target), not crash the validator");
	}

	[Test]
	[Category("Unit")]
	[Description("R3: a graph with no start event is an error.")]
	public void Validate_ShouldReturnR3Error_WhenNoStartEvent() {
		// Arrange
		List<ProcessGraphNode> nodes = [Node("a", "activityUserTask"), Node("e", "endEvent")];
		List<ProcessGraphEdge> edges = [Seq("a", "e")];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().Contain(f => f.RuleId == "R3" && f.Severity == ProcessGraphSeverity.Error,
			because: "a process must have exactly one start event (R3)");
	}

	[Test]
	[Category("Unit")]
	[Description("R3: a graph with more than one start event is an error.")]
	public void Validate_ShouldReturnR3Error_WhenMoreThanOneStartEvent() {
		// Arrange
		List<ProcessGraphNode> nodes =
			[Node("s1", "startEvent"), Node("s2", "startEvent"), Node("r", "readDataUserTask"), Node("e", "endEvent")];
		List<ProcessGraphEdge> edges = [Seq("s1", "r"), Seq("s2", "r"), Seq("r", "e")];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().Contain(f => f.RuleId == "R3" && f.Severity == ProcessGraphSeverity.Error,
			because: "a process must have exactly one top-level start event (R3)");
	}

	[Test]
	[Category("Unit")]
	[Description("R10: an event-based gateway whose outgoing does not lead to an intermediate catch event is an error.")]
	public void Validate_ShouldReturnR10Error_WhenEventBasedGatewayOutgoingIsNotCatchEvent() {
		// Arrange
		List<ProcessGraphNode> nodes =
			[Node("s", "startEvent"), Node("g", "eventBasedGateway"), Node("a", "activityUserTask"), Node("e", "endEvent")];
		List<ProcessGraphEdge> edges = [Seq("s", "g"), Seq("g", "a"), Seq("a", "e")];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().Contain(f => f.RuleId == "R10" && f.Severity == ProcessGraphSeverity.Error,
			because: "an event-based gateway outgoing must lead to an intermediate catch event (R10)");
	}

	[Test]
	[Category("Unit")]
	[Description("R11: a parallel/event-based gateway carrying a conditional flow is an error.")]
	public void Validate_ShouldReturnR11Error_WhenParallelGatewayHasConditionalFlow() {
		// Arrange
		List<ProcessGraphNode> nodes =
			[Node("s", "startEvent"), Node("g", "parallelGateway"), Node("a", "activityUserTask"), Node("e", "endEvent")];
		List<ProcessGraphEdge> edges = [Seq("s", "g"), Cond("g", "a"), Seq("a", "e")];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().Contain(f => f.RuleId == "R11" && f.Severity == ProcessGraphSeverity.Error,
			because: "parallel and event-based gateways must use plain sequence flows only (R11)");
	}

	[Test]
	[Category("Unit")]
	[Description("R13: a conditional flow originating from a start event (not a gateway/activity) is an error.")]
	public void Validate_ShouldReturnR13Error_WhenConditionalFlowOriginatesFromStart() {
		// Arrange
		List<ProcessGraphNode> nodes = [Node("s", "startEvent"), Node("a", "activityUserTask"), Node("e", "endEvent")];
		List<ProcessGraphEdge> edges = [Cond("s", "a"), Seq("a", "e")];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().Contain(f => f.RuleId == "R13" && f.Severity == ProcessGraphSeverity.Error,
			because: "a conditional flow may originate only from a gateway or an activity (R13)");
	}

	[Test]
	[Category("Unit")]
	[Description("R14: a default flow with no sibling conditional flow is an error.")]
	public void Validate_ShouldReturnR14Error_WhenDefaultFlowHasNoSiblingConditional() {
		// Arrange
		List<ProcessGraphNode> nodes =
			[Node("s", "startEvent"), Node("g", "exclusiveGateway"), Node("a", "activityUserTask"), Node("e", "endEvent")];
		List<ProcessGraphEdge> edges = [Seq("s", "g"), Def("g", "a"), Seq("a", "e")];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().Contain(f => f.RuleId == "R14" && f.Severity == ProcessGraphSeverity.Error,
			because: "a default flow is legal only when at least one sibling conditional flow exists (R14)");
	}

	[Test]
	[Category("Unit")]
	[Description("R15: an orphan node that cannot reach an end event is an error.")]
	public void Validate_ShouldReturnR15Error_WhenNodeIsOrphan() {
		// Arrange
		List<ProcessGraphNode> nodes =
			[Node("s", "startEvent"), Node("r", "readDataUserTask"), Node("e", "endEvent"), Node("orphan", "activityUserTask")];
		List<ProcessGraphEdge> edges = [Seq("s", "r"), Seq("r", "e")];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().Contain(f => f.RuleId == "R15" && f.Severity == ProcessGraphSeverity.Error && f.NodeName == "orphan",
			because: "a node unreachable from the start (and unable to reach an end) violates R15");
	}

	[Test]
	[Category("Unit")]
	[Description("R7 (warning, never error): a diverging exclusive gateway with no default flow yields a warning.")]
	public void Validate_ShouldReturnR7Warning_WhenDivergingExclusiveGatewayHasNoDefault() {
		// Arrange
		List<ProcessGraphNode> nodes = [
			Node("s", "startEvent"), Node("g", "exclusiveGateway"),
			Node("a1", "activityUserTask"), Node("a2", "activityUserTask"), Node("e", "endEvent")
		];
		List<ProcessGraphEdge> edges = [Seq("s", "g"), Cond("g", "a1"), Cond("g", "a2"), Seq("a1", "e"), Seq("a2", "e")];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().Contain(f => f.RuleId == "R7" && f.Severity == ProcessGraphSeverity.Warning,
			because: "a diverging exclusive gateway without a default flow should be warned about (R7)");
		result.Findings.Should().NotContain(f => f.RuleId == "R7" && f.Severity == ProcessGraphSeverity.Error,
			because: "R7 is advisory and must never be an error");
	}

	[Test]
	[Category("Unit")]
	[Description("R12 (warning, never error): multiple outgoing sequence flows from a non-gateway yields a warning.")]
	public void Validate_ShouldReturnR12Warning_WhenNonGatewayHasMultipleOutgoingSequenceFlows() {
		// Arrange
		List<ProcessGraphNode> nodes =
			[Node("s", "startEvent"), Node("a", "activityUserTask"), Node("e1", "endEvent"), Node("e2", "endEvent")];
		List<ProcessGraphEdge> edges = [Seq("s", "a"), Seq("a", "e1"), Seq("a", "e2")];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().Contain(f => f.RuleId == "R12" && f.Severity == ProcessGraphSeverity.Warning,
			because: "multiple outgoing sequence flows form an implicit parallel split worth confirming (R12)");
		result.Findings.Should().NotContain(f => f.RuleId == "R12" && f.Severity == ProcessGraphSeverity.Error,
			because: "R12 is advisory and must never be an error");
	}

	[Test]
	[Category("Unit")]
	[Description("R17 (warning, never error): Add data feeding a non-Read-data activity yields a warning.")]
	public void Validate_ShouldReturnR17Warning_WhenAddDataFeedsNonReadDataActivity() {
		// Arrange
		List<ProcessGraphNode> nodes =
			[Node("s", "startEvent"), Node("add", "addDataUserTask"), Node("a", "activityUserTask"), Node("e", "endEvent")];
		List<ProcessGraphEdge> edges = [Seq("s", "add"), Seq("add", "a"), Seq("a", "e")];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().Contain(f => f.RuleId == "R17" && f.Severity == ProcessGraphSeverity.Warning,
			because: "Add data outputs only an Id, so consuming other fields without a Read data warrants a warning (R17)");
		result.Findings.Should().NotContain(f => f.RuleId == "R17" && f.Severity == ProcessGraphSeverity.Error,
			because: "R17 is advisory and must never be an error");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-08: a node with an unrecognized data-id is surfaced as a finding (never crashes).")]
	public void Validate_ShouldSurfaceUnknownFinding_WhenNodeTypeIsUnrecognized() {
		// Arrange
		List<ProcessGraphNode> nodes = [Node("s", "startEvent"), Node("x", "totallyBogusType"), Node("e", "endEvent")];
		List<ProcessGraphEdge> edges = [Seq("s", "x"), Seq("x", "e")];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().Contain(f => f.RuleId == "UNKNOWN" && f.Severity == ProcessGraphSeverity.Error && f.NodeName == "x",
			because: "an unrecognized element type must be surfaced as a finding rather than silently accepted");
	}

	[Test]
	[Category("Unit")]
	[Description("DUP: two elements sharing a name are surfaced as an error (and the validator does not throw).")]
	public void Validate_ShouldReturnDupError_WhenTwoElementsShareAName() {
		// Arrange — the activity name "a" is reused; the server does not guard this on build/modify.
		List<ProcessGraphNode> nodes =
			[Node("s", "startEvent"), Node("a", "activityUserTask"), Node("a", "readDataUserTask"), Node("e", "endEvent")];
		List<ProcessGraphEdge> edges = [Seq("s", "a"), Seq("a", "e")];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().Contain(f => f.RuleId == "DUP" && f.Severity == ProcessGraphSeverity.Error && f.NodeName == "a",
			because: "element names must be unique within a process, and a duplicate must be reported rather than crash the validator");
		result.HasErrors.Should().BeTrue(because: "a duplicate element name is a structural error");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-06: a designer-accepted exclusive split (conditional + default) produces no error findings.")]
	public void Validate_ShouldReturnNoErrors_WhenExclusiveSplitHasConditionalAndDefault() {
		// Arrange
		List<ProcessGraphNode> nodes = [
			Node("s", "startEvent"), Node("g", "exclusiveGateway"),
			Node("a1", "activityUserTask"), Node("a2", "activityUserTask"), Node("e", "endEvent")
		];
		List<ProcessGraphEdge> edges = [Seq("s", "g"), Cond("g", "a1"), Def("g", "a2"), Seq("a1", "e"), Seq("a2", "e")];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.HasErrors.Should().BeFalse(
			because: "a well-formed exclusive split (one conditional + one default) is accepted by the designer — no false positives");
		result.Findings.Should().NotContain(f => f.Severity == ProcessGraphSeverity.Error,
			because: "a designer-accepted graph must produce zero error findings");
	}
}
