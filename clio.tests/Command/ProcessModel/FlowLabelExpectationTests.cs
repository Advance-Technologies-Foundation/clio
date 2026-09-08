using System.Collections.Generic;
using System.Linq;
using Clio.Command.ProcessModel;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// Covers the silent-drop detection for a flow's diagram label: a CrtProcessBuilder below 1.6.0.8 declares no
/// <c>label</c> member on a flow, so its serializer discards the field and the operation still answers success.
/// The caller then gets exactly the two identical unlabelled arrows the label exists to prevent, with nothing
/// saying so. These tests pin the pure halves — what the payload asked for, what the read-back is missing, and
/// what the caller is told.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "ProcessModel")]
public class FlowLabelExpectationTests {

	#region Methods: Private

	private static DescribeProcessResult Described(params (string Source, string Target, string Label)[] flows) =>
		new() {
			Flows = flows
				.Select(flow => new DescribedFlow { Source = flow.Source, Target = flow.Target, Label = flow.Label })
				.ToList()
		};

	#endregion

	#region Methods: FromDescriptor

	[Test]
	[Description("A build descriptor's labelled flows are the only ones whose label can be dropped; an unlabelled flow is irrelevant to the check, so the ordinary build pays nothing for it.")]
	public void FromDescriptor_ShouldReturnOnlyFlowsCarryingALabel() {
		// Arrange
		const string descriptor = """
			{"name":"UsrProc","flows":[
				{"source":"Start","target":"Decide"},
				{"source":"Decide","target":"Yes","kind":"conditional","condition":"1 > 0","label":"Approved"},
				{"source":"Decide","target":"No","kind":"default","label":"Rejected"}]}
			""";

		// Act
		IReadOnlyList<FlowLabelExpectation.FlowLabel> expected = FlowLabelExpectation.FromDescriptor(descriptor);

		// Assert
		expected.Select(flow => flow.Label).Should().BeEquivalentTo(["Approved", "Rejected"],
			because: "only a flow that asked for a label can have one silently discarded");
	}

	[Test]
	[Description("An EMPTY label is not collected. It means 'clear this label', and its success condition — no label on the flow — is indistinguishable from what an old server that dropped the field leaves behind, so a warning there would be a guess rather than a finding.")]
	[TestCase("\"\"")]
	[TestCase("\"   \"")]
	public void FromDescriptor_ShouldIgnoreAnEmptyLabel(string labelJson) {
		// Arrange
		string descriptor = $$"""
			{"name":"UsrProc","flows":[{"source":"Decide","target":"No","label":{{labelJson}}}]}
			""";

		// Act
		IReadOnlyList<FlowLabelExpectation.FlowLabel> expected = FlowLabelExpectation.FromDescriptor(descriptor);

		// Assert
		expected.Should().BeEmpty(because: "a clear has no expected label to verify");
	}

	[Test]
	[Description("A flow with no endpoints is skipped rather than collected: the endpoint pair is the only handle the read-back has on a flow, so an entry without one could never be matched and would warn on every call.")]
	public void FromDescriptor_ShouldIgnoreAFlowWithoutEndpoints() {
		// Arrange
		const string descriptor = """
			{"name":"UsrProc","flows":[{"label":"Approved"},{"source":"a","label":"Rejected"}]}
			""";

		// Act
		IReadOnlyList<FlowLabelExpectation.FlowLabel> expected = FlowLabelExpectation.FromDescriptor(descriptor);

		// Assert
		expected.Should().BeEmpty(because: "a label that cannot be addressed in the read-back cannot be verified");
	}

	[Test]
	[Description("A descriptor with no flows at all, and malformed JSON, both produce no expectation rather than throwing — the operation itself would have failed on the malformed case, and guessing would only add noise.")]
	[TestCase("""{"name":"UsrProc","elements":[]}""")]
	[TestCase("{ not json")]
	[TestCase("")]
	public void FromDescriptor_ShouldReturnEmpty_WhenThereIsNothingToRead(string descriptor) {
		// Arrange
		// (the descriptor is the arrangement)

		// Act
		IReadOnlyList<FlowLabelExpectation.FlowLabel> expected = FlowLabelExpectation.FromDescriptor(descriptor);

		// Assert
		expected.Should().BeEmpty(because: "no flows asked for a label, so there is nothing to verify");
	}

	#endregion

	#region Methods: FromOperations

	[Test]
	[Description("Both write routes carry the label on the operation itself, so addFlow and setFlow are collected the same way — unlike the email block there is no nested element descriptor to reach into.")]
	public void FromOperations_ShouldCollectLabelsFromAddFlowAndSetFlow() {
		// Arrange
		const string operations = """
			[{"op":"addFlow","source":"Decide","target":"Yes","kind":"conditional","condition":"1 > 0","label":"Approved"},
			 {"op":"setFlow","source":"Decide","target":"No","kind":"default","label":"Rejected"},
			 {"op":"removeFlow","source":"Decide","target":"Maybe"}]
			""";

		// Act
		IReadOnlyList<FlowLabelExpectation.FlowLabel> expected = FlowLabelExpectation.FromOperations(operations);

		// Assert
		expected.Select(flow => flow.Label).Should().BeEquivalentTo(["Approved", "Rejected"],
			because: "addFlow and setFlow both write a label and both can have it discarded");
	}

	[Test]
	[Description("An operations array that is not an array, or carries no label, yields no expectation rather than throwing.")]
	[TestCase("""[{"op":"removeFlow","source":"a","target":"b"}]""")]
	[TestCase("""{"op":"addFlow"}""")]
	[TestCase("nonsense")]
	public void FromOperations_ShouldReturnEmpty_WhenNoLabelIsWritten(string operations) {
		// Arrange
		// (the operations payload is the arrangement)

		// Act
		IReadOnlyList<FlowLabelExpectation.FlowLabel> expected = FlowLabelExpectation.FromOperations(operations);

		// Assert
		expected.Should().BeEmpty(because: "nothing asked for a label, so the extra read-back is skipped");
	}

	#endregion

	#region Methods: MissingLabels

	[Test]
	[Description("A label the read-back does not show is the finding this guard exists for: the operation answered success and the connector is unlabelled.")]
	public void MissingLabels_ShouldReportALabelTheReadBackDoesNotShow() {
		// Arrange
		IReadOnlyList<FlowLabelExpectation.FlowLabel> expected = FlowLabelExpectation.FromDescriptor("""
			{"flows":[{"source":"Decide","target":"Yes","label":"Approved"},
			          {"source":"Decide","target":"No","label":"Rejected"}]}
			""");
		DescribeProcessResult described = Described(("Decide", "Yes", "Approved"), ("Decide", "No", null));

		// Act
		IReadOnlyList<FlowLabelExpectation.FlowLabel> missing =
			FlowLabelExpectation.MissingLabels(described, expected);

		// Assert
		missing.Select(flow => flow.Target).Should().BeEquivalentTo(["No"],
			because: "the label that came back is verified and the one that did not is the drop");
	}

	[Test]
	[Description("A label that came back DIFFERENT is reported too, not only an absent one — a server that stored something else is as much a disagreement between what was asked for and what is drawn.")]
	public void MissingLabels_ShouldReportALabelThatCameBackDifferent() {
		// Arrange
		IReadOnlyList<FlowLabelExpectation.FlowLabel> expected = FlowLabelExpectation.FromDescriptor("""
			{"flows":[{"source":"Decide","target":"Yes","label":"Approved"}]}
			""");
		DescribeProcessResult described = Described(("Decide", "Yes", "Everything else"));

		// Act
		IReadOnlyList<FlowLabelExpectation.FlowLabel> missing =
			FlowLabelExpectation.MissingLabels(described, expected);

		// Assert
		missing.Should().HaveCount(1,
			because: "a label that is not the one requested is not a verified label");
	}

	[Test]
	[Description("Surrounding whitespace is ignored in the comparison, because the server TRIMS what it stores — reporting that as a drop would warn about a label that is drawn exactly as asked.")]
	public void MissingLabels_ShouldIgnoreSurroundingWhitespace() {
		// Arrange
		IReadOnlyList<FlowLabelExpectation.FlowLabel> expected = FlowLabelExpectation.FromDescriptor("""
			{"flows":[{"source":"Decide","target":"Yes","label":"  Approved  "}]}
			""");
		DescribeProcessResult described = Described(("Decide", "Yes", "Approved"));

		// Act
		IReadOnlyList<FlowLabelExpectation.FlowLabel> missing =
			FlowLabelExpectation.MissingLabels(described, expected);

		// Assert
		missing.Should().BeEmpty(because: "the trim is the server storing the label correctly, not dropping it");
	}

	[Test]
	[Description("Endpoint names are matched case-insensitively, matching how the server resolves an element by name — otherwise a caller who wrote 'decide' would be warned about a label that landed.")]
	public void MissingLabels_ShouldMatchEndpointsCaseInsensitively() {
		// Arrange
		IReadOnlyList<FlowLabelExpectation.FlowLabel> expected = FlowLabelExpectation.FromDescriptor("""
			{"flows":[{"source":"decide","target":"YES","label":"Approved"}]}
			""");
		DescribeProcessResult described = Described(("Decide", "Yes", "Approved"));

		// Act
		IReadOnlyList<FlowLabelExpectation.FlowLabel> missing =
			FlowLabelExpectation.MissingLabels(described, expected);

		// Assert
		missing.Should().BeEmpty(because: "the flow was found and its label is right");
	}

	[Test]
	[Description("A flow the description does not contain at all is NOT reported. The endpoints may have been written as UIds, or the flow removed later in the same batch, and neither is evidence a label was dropped — a guard that cries wolf on a working build gets ignored along with its true findings.")]
	public void MissingLabels_ShouldNotReportAFlowTheDescriptionDoesNotContain() {
		// Arrange
		IReadOnlyList<FlowLabelExpectation.FlowLabel> expected = FlowLabelExpectation.FromDescriptor("""
			{"flows":[{"source":"Decide","target":"Gone","label":"Approved"}]}
			""");
		DescribeProcessResult described = Described(("Decide", "Yes", "Approved"));

		// Act
		IReadOnlyList<FlowLabelExpectation.FlowLabel> missing =
			FlowLabelExpectation.MissingLabels(described, expected);

		// Assert
		missing.Should().BeEmpty(because: "an unmatched flow is an unverifiable one, not a failed one");
	}

	[Test]
	[Description("A description with no flows, and an empty expectation, both yield no findings rather than throwing.")]
	public void MissingLabels_ShouldReturnEmpty_WhenThereIsNothingToCompare() {
		// Arrange
		IReadOnlyList<FlowLabelExpectation.FlowLabel> expected = FlowLabelExpectation.FromDescriptor("""
			{"flows":[{"source":"Decide","target":"Yes","label":"Approved"}]}
			""");

		// Act
		IReadOnlyList<FlowLabelExpectation.FlowLabel> noFlows =
			FlowLabelExpectation.MissingLabels(new DescribeProcessResult(), expected);
		IReadOnlyList<FlowLabelExpectation.FlowLabel> nothingExpected =
			FlowLabelExpectation.MissingLabels(Described(("Decide", "Yes", null)), []);

		// Assert
		noFlows.Should().BeEmpty(because: "a description that reports no flows is not evidence of a drop");
		nothingExpected.Should().BeEmpty(because: "no label was requested, so none can be missing");
	}

	#endregion

	#region Methods: BuildWarning

	[Test]
	[Description("The warning names each flow by its endpoint pair and quotes the label that did not land, because the endpoint pair is the only handle the caller has to fix it — and it names the cause and the remedy, since 'the label is missing' alone leaves them auditing their own payload.")]
	public void BuildWarning_ShouldNameTheFlowsTheLabelsAndTheRemedy() {
		// Arrange
		IReadOnlyList<FlowLabelExpectation.FlowLabel> missing = [
			new FlowLabelExpectation.FlowLabel("Decide", "Yes", "Approved"),
			new FlowLabelExpectation.FlowLabel("Decide", "No", "Rejected")
		];

		// Act
		string warning = FlowLabelExpectation.BuildWarning(missing);

		// Assert
		warning.Should().Contain("Decide -> Yes ('Approved')",
			because: "the endpoint pair plus the text is what the caller needs to re-apply it");
		warning.Should().Contain("Decide -> No ('Rejected')",
			because: "every dropped label is reported, not just the first");
		warning.Should().Contain("1.6.0.8",
			because: "naming the version turns 'it did not work' into a diagnosis");
		warning.Should().Contain("install-process-builder",
			because: "the remedy has to be actionable in the same breath as the finding");
		warning.Should().Contain("flows",
			because: "two dropped labels are plural — a warning that says 'flow' about two reads as a partial finding");
	}

	[Test]
	[Description("Nothing missing yields null rather than an empty string, so a caller can treat null as 'no warning to emit' without inspecting the text.")]
	public void BuildWarning_ShouldReturnNull_WhenNothingIsMissing() {
		// Arrange
		IReadOnlyList<FlowLabelExpectation.FlowLabel> missing = [];

		// Act
		string warning = FlowLabelExpectation.BuildWarning(missing);

		// Assert
		warning.Should().BeNull(because: "a verified operation must not emit a warning at all");
	}

	#endregion
}
