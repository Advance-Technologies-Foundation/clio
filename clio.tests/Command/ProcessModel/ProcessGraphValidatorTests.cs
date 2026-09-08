using System.Collections.Generic;
using Clio.Command.ProcessModel;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// Unit tests for <see cref="ProcessGraphValidator"/> — one case per error/warning rule (R1–R18; both
/// R13 condition halves live in the tool fixture, where the wire shape of an omitted field is real),
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

	// A flow whose KIND is not conditional but which carries a condition anyway - the shape ModelBuilder
	// and DescribeProcessPrompt both document as a trap, because the condition is dropped during flow-
	// schema generation and the branch then runs unconditionally.
	private static ProcessGraphEdge SeqWithCondition(string from, string to, string condition) =>
		new(from, to, ProcessFlowKind.Sequence, condition);

	private static ProcessGraphEdge DefWithCondition(string from, string to, string condition) =>
		new(from, to, ProcessFlowKind.Default, condition);

	private ProcessGraphValidationResult Validate(IReadOnlyList<ProcessGraphNode> nodes, IReadOnlyList<ProcessGraphEdge> edges)
		=> _validator.Validate(new ProcessGraph(nodes, edges));

	[Test]
	[Category("Unit")]
	[Description("A graph written with the BUILD tokens the MCP tools advertise (readData / changeData / changeAccessRights / sendEmail) validates with no error findings. These spellings do not end in \"usertask\", so before ENG-92717 they resolved to EventType.Unknown and CheckUnknownTypes raised a hard error on a graph that builds fine.")]
	[TestCase("readData")]
	[TestCase("changeData")]
	[TestCase("changeAccessRights")]
	[TestCase("sendEmail")]
	public void Validate_ShouldReturnNoErrors_WhenGraphUsesBuildTokens(string elementType) {
		// Arrange
		List<ProcessGraphNode> nodes = [Node("s", "startEvent"), Node("x", elementType), Node("e", "endEvent")];
		List<ProcessGraphEdge> edges = [Seq("s", "x"), Seq("x", "e")];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.HasErrors.Should().BeFalse(
			because: $"'{elementType}' is a build token create-business-process accepts, so validating a graph "
				+ "that uses it must not report the element as unrecognized");
	}

	[Test]
	[Category("Unit")]
	[Description("R17 fires whatever casing the Add data source node uses: the rule normalizes the type, so a data-id spelled AddDataUserTask is the same element kind as addDataUserTask and must not escape the warning.")]
	[TestCase("addDataUserTask")]
	[TestCase("AddDataUserTask")]
	[TestCase("adddatausertask")]
	public void Validate_ShouldRaiseR17_WhenAddDataChainsIntoANonReadDataInAnySourceCasing(string addDataType) {
		// Arrange
		List<ProcessGraphNode> nodes =
			[Node("s", "startEvent"), Node("a", addDataType), Node("t", "performTask"), Node("e", "endEvent")];
		List<ProcessGraphEdge> edges = [Seq("s", "a"), Seq("a", "t"), Seq("t", "e")];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().Contain(finding => finding.RuleId == "R17",
			because: $"'{addDataType}' names the Add data element, so the chaining advice must reach the caller "
				+ "regardless of how the surface spelled the type");
	}

	[Test]
	[Category("Unit")]
	[Description("R17 does not fire when Add data chains into a Read data written as the build token 'readData'. Widening the type vocabulary made readData resolve to an Activity, so a rule comparing the raw literal 'readDataUserTask' would warn 'chain a Read data' while pointing AT a Read data element.")]
	[TestCase("readDataUserTask")]
	[TestCase("readData")]
	[TestCase("ReadDataUserTask")]
	public void Validate_ShouldNotRaiseR17_WhenAddDataChainsIntoAReadDataInAnySpelling(string readDataType) {
		// Arrange
		List<ProcessGraphNode> nodes =
			[Node("s", "startEvent"), Node("a", "addDataUserTask"), Node("r", readDataType), Node("e", "endEvent")];
		List<ProcessGraphEdge> edges = [Seq("s", "a"), Seq("a", "r"), Seq("r", "e")];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().NotContain(finding => finding.RuleId == "R17",
			because: $"'{readDataType}' IS a Read data element, so advising the caller to chain one before it "
				+ "would send an agent to insert a redundant element");
	}

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
	[Description("R13: a conditional flow originating from a start event is reported as a WARNING. It was an "
		+ "error, and the corpus refutes that: four shipped conditional flows leave an event and run. The "
		+ "finding stays because the designer will not draw the connection.")]
	public void Validate_ShouldReturnR13Warning_WhenConditionalFlowOriginatesFromStart() {
		// Arrange
		List<ProcessGraphNode> nodes = [Node("s", "startEvent"), Node("a", "activityUserTask"), Node("e", "endEvent")];
		List<ProcessGraphEdge> edges = [Cond("s", "a"), Seq("a", "e")];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().Contain(f => f.RuleId == "R13" && f.Severity == ProcessGraphSeverity.Warning
				&& f.Message.Contains("neither a gateway nor an activity"),
			because: "the source role is still worth reporting - the designer offers no such connection. The "
				+ "message is asserted because the `Cond` helper omits the condition, so this graph raises TWO "
				+ "R13 warnings: without it the omitted-condition one satisfies the predicate on its own and "
				+ "the source-role clause could be deleted whole with this test still green");
		result.Findings.Should().NotContain(f => f.RuleId == "R13" && f.Severity == ProcessGraphSeverity.Error,
			because: "an error here told an agent that two shipped CrtBase processes are invalid");
	}

	[Test]
	[Category("Unit")]
	[Description("A condition supplied for a flow whose kind is NOT conditional is reported by "
		+ "nothing, and that is pinned here as a deliberate boundary rather than left as a gap. "
		+ "CheckConditionalFlows iterates only the edges whose FlowKind is Conditional, so the shape "
		+ "never reaches a predicate. It is a real trap - the condition is dropped during flow-schema "
		+ "generation and the branch then runs unconditionally, which DescribeProcessPrompt documents "
		+ "- so if a warning is ever added, this test is what has to change, and changing it is the "
		+ "moment to argue the severity against the shipped corpus the way R7/R9/R13/R14 were.")]
	public void Validate_ShouldReportNothing_WhenANonConditionalFlowCarriesACondition() {
		// Arrange
		List<ProcessGraphNode> nodes = [
			Node("s", "startEvent"), Node("a", "activityUserTask"), Node("g", "exclusiveGateway"),
			Node("b", "activityUserTask"), Node("c", "activityUserTask"), Node("e", "endEvent")
		];
		List<ProcessGraphEdge> edges = [
			Seq("s", "a"),
			// the two shapes under test: a condition on a PLAIN flow and on a DEFAULT one
			SeqWithCondition("a", "g", "[#Amount#] > 100"),
			new("g", "b", ProcessFlowKind.Conditional, "[#Amount#] > 100"),
			DefWithCondition("g", "c", "[#Amount#] <= 100"),
			Seq("b", "e"), Seq("c", "e")
		];

		// The arrangement itself is asserted first. Everything below is an assertion of SILENCE, and a
		// silent expectation passes just as well when the shape under test is not present - so if a
		// helper ever stopped carrying the condition through, this test would stay green while no
		// longer testing anything.
		edges.Should().Contain(e => e.FlowKind == ProcessFlowKind.Sequence
				&& e.Condition == "[#Amount#] > 100",
			because: "the graph has to actually carry a condition on a PLAIN flow, or the silence"
				+ " asserted below is silence about nothing");
		edges.Should().Contain(e => e.FlowKind == ProcessFlowKind.Default
				&& e.Condition == "[#Amount#] <= 100",
			because: "and on a DEFAULT flow, which is the second of the two shapes under test");

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().NotContain(f => f.RuleId == "R13",
			because: "R13 owns conditions and reads only conditional flows, so a condition on a plain or "
				+ "default flow is outside every one of its clauses - it is not an empty condition and not "
				+ "an omitted one, because the flow is not conditional in the first place");
		result.HasErrors.Should().BeFalse(
			because: "nothing in the rule set makes this an error, and an agent checking this graph's "
				+ "SHAPE is told it is buildable - which it is; what it is not told is that the two "
				+ "conditions will be discarded and both branches taken");
	}

	[Test]
	[Category("Unit")]
	[Description("R14: a default flow with no sibling conditional flow is an error where the source actually BRANCHES. The source is a DIVERGING activity, and that matters: this test used to arrange a single default flow out of an exclusive gateway, which is the CONVERGING shape 45 shipped gateways are in, and it asserted the rule that rejected them.")]
	public void Validate_ShouldReturnR14Error_WhenDivergingSourceHasADefaultWithNoConditional() {
		// Arrange
		List<ProcessGraphNode> nodes = [Node("s", "startEvent"), Node("a", "activityUserTask"),
			Node("b", "activityUserTask"), Node("c", "activityUserTask"), Node("e", "endEvent")];
		List<ProcessGraphEdge> edges =
			[Seq("s", "a"), Def("a", "b"), Seq("a", "c"), Seq("b", "e"), Seq("c", "e")];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().Contain(
			f => f.RuleId == "R14" && f.Severity == ProcessGraphSeverity.Error
				&& f.Message.Contains("sibling conditional"),
			because: "a default branch is the fallback of something, so with no conditional sibling there is "
				+ "nothing for it to fall back from - and the message discriminator matters because R14 now "
				+ "reports two different defects");
	}

	[Test]
	[Category("Unit")]
	[Description("R14 is scoped by ARITY. A converging or-gateway's single outgoing flow is a default flow by construction - the designer's allowed-outgoing list for an or-gateway is conditional + default with no plain sequence flow - and unscoped this rule called 45 shipped gateways invalid. HasErrors is asserted rather than one rule id, because the whole point is that the shape the designer itself produces is clean.")]
	public void Validate_ShouldReturnNoError_ForAConvergingGatewayWithOneDefaultFlow() {
		// Arrange
		List<ProcessGraphNode> nodes = [Node("s", "startEvent"), Node("split", "exclusiveGateway"),
			Node("a", "activityUserTask"), Node("b", "activityUserTask"),
			Node("merge", "exclusiveGateway"), Node("e", "endEvent")];
		List<ProcessGraphEdge> edges = [Seq("s", "split"), Cond("split", "a"), Def("split", "b"),
			Def("a", "merge"), Def("b", "merge"), Def("merge", "e")];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.HasErrors.Should().BeFalse(
			because: "a converging gateway has one way out and the designer cannot draw a plain flow there, "
				+ "so its single default flow is the only shape available - and asserting HasErrors rather "
				+ "than one rule id is what keeps a future rule from quietly rejecting it by another name");
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

	[Test]
	[Category("Unit")]
	[Description("R18: a conditional branch beside TWO flows that have none is an error. The platform "
		+ "synthesizes a gateway, removes exactly ONE unconditional flow and runs the rest, so the second one "
		+ "starts alongside the branch the condition chose.")]
	public void Validate_ShouldReturnR18Error_WhenAConditionalHasTwoUnconditionalSiblings() {
		// Arrange
		List<ProcessGraphNode> nodes = [
			Node("s", "startEvent"), Node("t", "userTask"),
			Node("a", "endEvent"), Node("b", "endEvent"), Node("c", "endEvent")
		];
		List<ProcessGraphEdge> edges = [
			Seq("s", "t"), Seq("t", "a"), Seq("t", "b"), Cond("t", "c")
		];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().Contain(f => f.RuleId == "R18" && f.Severity == ProcessGraphSeverity.Error,
			because: "the second unconditional flow always runs, so this is a decision plus a stray branch");
		result.HasErrors.Should().BeTrue(
			because: "CrtProcessBuilder refuses to build the shape, so a warning would promise a build that fails");
	}

	[Test]
	[Category("Unit")]
	[Description("R18 counts an explicitly declared `default` as UNCONDITIONAL, so "
		+ "[conditional, default, sequence] is an error. This is the shape raised in review as a false "
		+ "positive - the premise being that a `default` is the privileged fallback and therefore does not "
		+ "count. It is not privileged: FlowConditionalGateway.GetIsDefSequenceFlow is "
		+ "`BpmnElementName != ConditionalSequenceFlowName`, which matches default and plain alike, and "
		+ "RemoveDefSequenceFlow then removes exactly ONE of them by list order - so the survivor starts "
		+ "beside the branch the condition chose, and WHICH one survives depends on array order. Measured "
		+ "twice besides: CrtProcessBuilder refuses this exact graph (exit code 1), and zero of 1711 shipped "
		+ "schemas are in it.\n"
		+ "This test exists because the suite could not tell the two readings apart. The other R18 cases use "
		+ "[sequence, sequence, conditional], where both readings count two - so narrowing the predicate to "
		+ "`FlowKind == Sequence` left all 4819 tests green while R18 stopped firing on the one shape the "
		+ "rebuttal is about. Mixing the kinds is what makes the count discriminating.")]
	public void Validate_ShouldReturnR18Error_WhenAConditionalHasADefaultAndAPlainSibling() {
		// Arrange - one conditional branch, one declared default, one plain flow, all off the same element.
		List<ProcessGraphNode> nodes = [
			Node("s", "startEvent"), Node("t", "userTask"),
			Node("a", "endEvent"), Node("b", "endEvent"), Node("c", "endEvent")
		];
		List<ProcessGraphEdge> edges = [
			Seq("s", "t"), Cond("t", "a"), Def("t", "b"), Seq("t", "c")
		];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().Contain(f => f.RuleId == "R18" && f.Severity == ProcessGraphSeverity.Error,
			because: "the default and the plain flow are both non-conditional, the synthesized gateway drops "
				+ "one of the two and runs the other, and marking one of them `default` changes nothing about "
				+ "that - the marker is not read");
		result.Findings.Should().NotContain(f => f.RuleId == "R12",
			because: "R12 counts only SEQUENCE flows and needs more than one, so on this mixed shape it is "
				+ "silent - which is why R18 is the only finding standing between the author and a silent "
				+ "double start, and why deleting R18 would leave the shape unreported rather than "
				+ "double-reported");
	}

	[Test]
	[Category("Unit")]
	[Description("R18 does not fire on one conditional beside a single unconditional flow. 736 shipped "
		+ "sources carry exactly that shape, 310 of them not gateways, so a finding there is a false positive.")]
	public void Validate_ShouldNotReturnR18_WhenAConditionalHasOneUnconditionalSibling() {
		// Arrange
		List<ProcessGraphNode> nodes = [
			Node("s", "startEvent"), Node("t", "userTask"), Node("a", "endEvent"), Node("b", "endEvent")
		];
		List<ProcessGraphEdge> edges = [Seq("s", "t"), Cond("t", "a"), Seq("t", "b")];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().NotContain(f => f.RuleId == "R18",
			because: "one conditional branch and one fallback is the ordinary branch shape the corpus ships");
	}

	[Test]
	[Category("Unit")]
	[Description("R18 does not fire on two unconditional flows with no condition between them. That is the "
		+ "implicit parallel split R12 reports, both branches were observed running on a stand, and it is the "
		+ "MIXTURE that has no single meaning rather than the pair.")]
	public void Validate_ShouldNotReturnR18_WhenTwoUnconditionalFlowsCarryNoCondition() {
		// Arrange
		List<ProcessGraphNode> nodes = [
			Node("s", "startEvent"), Node("t", "userTask"), Node("a", "endEvent"), Node("b", "endEvent")
		];
		List<ProcessGraphEdge> edges = [Seq("s", "t"), Seq("t", "a"), Seq("t", "b")];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().NotContain(f => f.RuleId == "R18",
			because: "an implicit parallel split is a real shape and R12 already reports it as a warning");
		result.HasErrors.Should().BeFalse(
			because: "R12 is advisory, so a plain parallel split must not block a build");
	}

	[Test]
	[Category("Unit")]
	[Description("R13 on a conditional flow leaving an event is a WARNING, not an error. Four shipped flows "
		+ "have that source - two a start event, two an intermediate catch signal event - and they run.")]
	public void Validate_ShouldWarnNotError_WhenAConditionalFlowLeavesAStartEvent() {
		// Arrange
		List<ProcessGraphNode> nodes = [Node("s", "startEvent"), Node("e", "endEvent")];
		List<ProcessGraphEdge> edges = [new("s", "e", ProcessFlowKind.Conditional, "1 > 0")];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().Contain(f => f.RuleId == "R13" && f.Severity == ProcessGraphSeverity.Warning,
			because: "the designer will not draw the connection, so it is still worth reporting");
		result.Findings.Should().NotContain(f => f.RuleId == "R13" && f.Severity == ProcessGraphSeverity.Error,
			because: "an error told an agent that the platform's own shipped content is invalid");
	}

	[Test]
	[Category("Unit")]
	[Description("A node with no name yields findings instead of an exception. The name is a dictionary key, "
		+ "so a null one threw ArgumentNullException out of the first grouping and the caller got no findings "
		+ "for ANY node - against the interface contract of never throwing on malformed input.")]
	public void Validate_ShouldReportAndKeepGoing_WhenANodeHasNoName() {
		// Arrange
		List<ProcessGraphNode> nodes = [
			Node("s", "startEvent"), Node(null, "userTask"), Node("e", "endEvent")
		];
		List<ProcessGraphEdge> edges = [Seq("s", "e")];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().Contain(f => f.RuleId == "UNNAMED",
			because: "an element that cannot be referenced by name is reported rather than crashing the run");
		result.Findings.Should().Contain(f => f.RuleId == "R15",
			because: "the rest of the graph is still analysed - the unnamed node is unreachable and says so");
	}

	[Test]
	[Category("Unit")]
	[Description("A flow with a blank endpoint is reported by the ordinary missing-node rule rather than "
		+ "throwing. ContainsKey(null) throws the same ArgumentNullException a null node name did.")]
	public void Validate_ShouldReportAndKeepGoing_WhenAFlowHasABlankEndpoint() {
		// Arrange
		List<ProcessGraphNode> nodes = [Node("s", "startEvent"), Node("e", "endEvent")];
		List<ProcessGraphEdge> edges = [Seq("s", "e"), Seq("s", null)];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().Contain(f => f.RuleId == "R15",
			because: "a blank endpoint is a flow that references a node the graph does not contain");
		result.HasErrors.Should().BeTrue(
			because: "it is still an error - what changed is that the caller is told rather than thrown at");
	}

	[Test]
	[Category("Unit")]
	[Description("R8 sees the SYNTHESIZED gateway. An element with two conditional outgoing flows chooses one "
		+ "of them exactly as a declared exclusive gateway does, so a parallel join fed by both hangs in "
		+ "Running forever - and raised nothing while the rule looked only at declared gateway TYPES.")]
	public void Validate_ShouldReturnR8Warning_WhenAParallelJoinIsFedByAConditionalSplitOnAnActivity() {
		// Arrange
		List<ProcessGraphNode> nodes = [
			Node("s", "startEvent"), Node("a", "activityUserTask"),
			Node("b", "activityUserTask"), Node("c", "activityUserTask"),
			Node("join", "parallelGateway"), Node("e", "endEvent")
		];
		List<ProcessGraphEdge> edges = [
			Seq("s", "a"),
			new("a", "b", ProcessFlowKind.Conditional, "1 > 0"),
			new("a", "c", ProcessFlowKind.Conditional, "2 > 0"),
			Seq("b", "join"), Seq("c", "join"), Seq("join", "e")
		];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().Contain(f => f.RuleId == "R8",
			because: "the platform synthesizes an exclusive gateway on 'a', so only one branch ever reaches "
				+ "the join and it waits for both");
	}

	[Test]
	[Category("Unit")]
	[Description("The same join fed by two PLAIN flows off one element is a genuine AND fork and must stay "
		+ "silent. Both branches were observed running on a stand, so the join really does receive both.")]
	public void Validate_ShouldNotReturnR8_WhenAParallelJoinIsFedByAPlainSplit() {
		// Arrange
		List<ProcessGraphNode> nodes = [
			Node("s", "startEvent"), Node("a", "activityUserTask"),
			Node("b", "activityUserTask"), Node("c", "activityUserTask"),
			Node("join", "parallelGateway"), Node("e", "endEvent")
		];
		List<ProcessGraphEdge> edges = [
			Seq("s", "a"), Seq("a", "b"), Seq("a", "c"),
			Seq("b", "join"), Seq("c", "join"), Seq("join", "e")
		];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().NotContain(f => f.RuleId == "R8",
			because: "an element with no conditional outgoing flow gets no synthesized gateway - every branch "
				+ "is taken, so the join receives both tokens and nothing hangs");
	}

	[Test]
	[Category("Unit")]
	[Description("A null ENTRY in elements[] yields findings instead of an exception. Distinct from a node "
		+ "whose NAME is null, which the case above covers: `{\"elements\":[null]}` deserializes to a list "
		+ "CONTAINING null, so the guard is about the entry, not the field. No test covered this, which is "
		+ "why deleting that guard stayed green and a static analyser could call it unnecessary.")]
	public void Validate_ShouldReportAndKeepGoing_WhenAnElementEntryIsNull() {
		// Arrange
		List<ProcessGraphNode> nodes = [Node("s", "startEvent"), null, Node("e", "endEvent")];
		List<ProcessGraphEdge> edges = [Seq("s", "e")];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Should().NotBeNull(
			because: "IProcessGraphValidator documents never throwing on malformed input, and a null entry is "
				+ "malformed input the MCP schema does not forbid");
		result.Findings.Should().NotContain(f => f.RuleId == "UNNAMED",
			because: "a null ENTRY is not an unnamed element - there is no element - so it is dropped rather "
				+ "than reported as one, and the named nodes are still analysed");
	}

	[Test]
	[Category("Unit")]
	[Description("A null ENTRY in flows[] yields findings instead of an exception, for the same reason and "
		+ "through a different guard - the edge projection's own null filter.")]
	public void Validate_ShouldReportAndKeepGoing_WhenAFlowEntryIsNull() {
		// Arrange
		List<ProcessGraphNode> nodes = [Node("s", "startEvent"), Node("e", "endEvent")];
		List<ProcessGraphEdge> edges = [Seq("s", "e"), null];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Should().NotBeNull(
			because: "a null flow entry must not take the whole validation down either");
		result.HasErrors.Should().BeFalse(
			because: "the graph that remains is the canonical valid one, so dropping the null entry leaves "
				+ "nothing to report - if this ever fails, the null was turned into a finding rather than "
				+ "dropped, which is a different decision and needs its own test");
	}

	[Test]
	[Category("Unit")]
	[Description("A flow with BOTH endpoints blank is reported as two missing nodes and NOT as a self-loop. "
		+ "One placeholder for both ends made `{\"edges\":[{}]}` normalise to Source == Target, so the "
		+ "self-loop rule fabricated an R15 telling the caller to route the flow back through a gateway - "
		+ "remediation about repeating an element, for someone who forgot both endpoints. The existing test "
		+ "covers ONE blank endpoint, which is why the collapse stayed invisible.")]
	public void Validate_ShouldNotReportASelfLoop_WhenBothFlowEndpointsAreBlank() {
		// Arrange
		List<ProcessGraphNode> nodes = [Node("s", "startEvent"), Node("e", "endEvent")];
		List<ProcessGraphEdge> edges = [Seq("s", "e"), new(null, null, ProcessFlowKind.Sequence)];

		// Act
		ProcessGraphValidationResult result = Validate(nodes, edges);

		// Assert
		result.Findings.Should().HaveCount(1,
			because: "one forgotten flow is one mistake and deserves one finding. A count rather than only an "
				+ "absence assertion: keying this test on the self-loop message alone would let a re-wording "
				+ "of CheckSelfLoops - a method in this same file - bring the fabricated finding back green");
		result.Findings.Should().Contain(f => f.RuleId == "R15"
				&& f.Message.Contains("source '(missing source)'")
				&& f.Message.Contains("target '(missing target)'"),
			because: "it IS a flow referencing nodes the graph does not contain, which is what to report - and "
				+ "the two placeholders are asserted BY NAME because nothing else pins them: swap them and "
				+ "every other assertion in the suite still passes while the message misnames both ends");
		result.Findings.Should().NotContain(f => f.Message.Contains("to itself"),
			because: "an edge that connects nothing is not a self-loop, and the self-loop remediation - route "
				+ "it back through a gateway - has nothing to do with the caller's mistake");
	}
}
