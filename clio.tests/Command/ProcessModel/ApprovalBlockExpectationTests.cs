using System.Collections.Generic;
using System.Linq;
using Clio.Command.ProcessModel;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// Covers the silent-drop detection for the Approval element: a server that predates it discards an
/// <c>approval</c> block and still answers success, so the only way to know the configuration landed is to read
/// it back. These tests pin the pure halves (what the payload asked for, what the description is missing, what
/// the caller is told).
/// </summary>
[TestFixture]
[Category("Unit")]
public class ApprovalBlockExpectationTests {

	#region Methods: FromDescriptor

	[Test]
	[Description("A build descriptor's elements carrying an approval block are the ones whose configuration must be verified; elements without one are irrelevant to the check.")]
	public void FromDescriptor_ShouldReturnOnlyElementsCarryingAnApprovalBlock() {
		// Arrange
		const string descriptor = """
			{"name":"UsrProc","elements":[
				{"name":"StartEvent1","type":"startEvent"},
				{"name":"Approve1","type":"approval","approval":{"object":"Order"}},
				{"name":"Task1","type":"userTask"},
				{"name":"Approve2","type":"approval","approval":{"purpose":"x"}}]}
			""";

		// Act
		IReadOnlyList<ApprovalBlockExpectation.ApprovalExpectation> expected = ApprovalBlockExpectation.FromDescriptor(descriptor);

		// Assert
		expected.Select(e => e.ElementName).Should().BeEquivalentTo(["Approve1", "Approve2"],
			because: "only the elements that actually asked for an approval configuration can have one dropped");
	}

	[Test]
	[Description("A descriptor with no approval block anywhere produces no expectation, so the ordinary build path never pays for the extra read-back.")]
	public void FromDescriptor_ShouldReturnEmpty_WhenNoApprovalBlockPresent() {
		// Arrange
		const string descriptor = """
			{"name":"UsrProc","elements":[{"name":"StartEvent1","type":"startEvent"}]}
			""";

		// Act
		IReadOnlyList<ApprovalBlockExpectation.ApprovalExpectation> expected = ApprovalBlockExpectation.FromDescriptor(descriptor);

		// Assert
		expected.Should().BeEmpty(because: "there is nothing to verify when nothing asked for an approval block");
	}

	[Test]
	[Description("Malformed descriptor JSON yields no expectation rather than throwing — the operation itself would have failed on it, and guessing would only add noise.")]
	public void FromDescriptor_ShouldReturnEmpty_WhenJsonIsMalformed() {
		// Arrange
		const string descriptor = "{not json";

		// Act
		IReadOnlyList<ApprovalBlockExpectation.ApprovalExpectation> expected = ApprovalBlockExpectation.FromDescriptor(descriptor);

		// Assert
		expected.Should().BeEmpty(because: "an unparseable payload is not evidence that a block was dropped");
	}

	#endregion

	#region Methods: FromOperations

	[Test]
	[Description("Both modify routes that carry an approval block are detected: addElement nests the name under 'element', setElement puts it on the operation.")]
	public void FromOperations_ShouldCoverAddElementAndSetElement() {
		// Arrange
		const string operations = """
			[{"op":"addElement","element":{"name":"Approve1","type":"approval","approval":{"object":"Order"}}},
			 {"op":"setElement","elementName":"Approve2","elementUpdate":{"approval":{"allowDelegation":true}}},
			 {"op":"setElement","elementName":"Task1","elementUpdate":{"useBackgroundMode":true}}]
			""";

		// Act
		IReadOnlyList<ApprovalBlockExpectation.ApprovalExpectation> expected = ApprovalBlockExpectation.FromOperations(operations);

		// Assert
		expected.Select(e => e.ElementName).Should().BeEquivalentTo(["Approve1", "Approve2"],
			because: "an approval block reaches the server through either route and is discarded the same way in both");
	}

	#endregion

	#region Methods: Private

	/// <summary>An expectation for an element whose sent block carried no approver — the pre-approver shape.</summary>
	private static ApprovalBlockExpectation.ApprovalExpectation Expect(string elementName) =>
		new(elementName, ExpectsApprover: false);

	#endregion

	#region Methods: Missing

	[Test]
	[Description("An element the read-back reports WITHOUT an approval block is the dropped one; an element that does carry the block is fine.")]
	public void Missing_ShouldReportOnlyElementsWithoutAnApprovalBlock() {
		// Arrange
		var described = new DescribeProcessResult {
			Elements = [
				new DescribedElement { Name = "Approve1", Approval = new DescribedApproval { Object = "Order" } },
				new DescribedElement { Name = "Approve2", Approval = null }
			]
		};

		// Act
		IReadOnlyList<ApprovalBlockExpectation.DroppedApproval> missing = ApprovalBlockExpectation.Missing(described, [Expect("Approve1"), Expect("Approve2")]);

		// Assert
		missing.Select(m => m.ElementName).Should().BeEquivalentTo(["Approve2"],
			because: "the read-back is the evidence — a reported block means the configuration landed");
	}

	[Test]
	[Description("An element absent from the read-back entirely is NOT reported as dropped: an identifier this comparison cannot resolve is a reason to stay quiet, not to accuse the server.")]
	public void Missing_ShouldStaySilent_WhenElementIsNotInTheReadBack() {
		// Arrange
		var described = new DescribeProcessResult {
			Elements = [new DescribedElement { Name = "SomethingElse" }]
		};

		// Act
		IReadOnlyList<ApprovalBlockExpectation.DroppedApproval> missing = ApprovalBlockExpectation.Missing(described, [Expect("Approve1")]);

		// Assert
		missing.Should().BeEmpty(
			because: "the check must not accuse the server on evidence it does not have");
	}

	[Test]
	[Description("An element addressed by UId matches the read-back too, so a caller who passed a UId is not falsely told its configuration was discarded.")]
	public void Missing_ShouldMatchOnUid() {
		// Arrange
		const string uid = "d1f532ea-bdd5-4c62-aec0-7a698a5e5334";
		var described = new DescribeProcessResult {
			Elements = [
				new DescribedElement { Name = "Approve1", Uid = uid, Approval = new DescribedApproval() }
			]
		};

		// Act
		IReadOnlyList<ApprovalBlockExpectation.DroppedApproval> missing = ApprovalBlockExpectation.Missing(described, [Expect(uid)]);

		// Assert
		missing.Should().BeEmpty(
			because: "setElement identifies an element by name OR UId, so both must match the read-back");
	}

	[Test]
	[Description("Nothing expected means nothing to check, so no read-back comparison happens at all.")]
	public void Missing_ShouldReturnEmpty_WhenNothingWasExpected() {
		// Arrange
		var described = new DescribeProcessResult { Elements = [] };

		// Act
		IReadOnlyList<ApprovalBlockExpectation.DroppedApproval> missing = ApprovalBlockExpectation.Missing(described, []);

		// Assert
		missing.Should().BeEmpty(because: "an empty expectation short-circuits the whole check");
	}


	[Test]
	[Description("A block that comes back WITHOUT the approver that was sent is reported as dropped: a server carrying the Approval element but not its approver returns the block and discards that one member, which a presence-only check would read as healthy.")]
	public void Missing_ShouldReportAnApprovalBlockThatCameBackWithoutItsApprover() {
		// Arrange — the block landed, the approver did not
		var described = new DescribeProcessResult {
			Elements = [
				new DescribedElement {
					Name = "Approve1",
					Approval = new DescribedApproval { Object = "Order", ApproverType = null }
				}
			]
		};

		// Act
		IReadOnlyList<ApprovalBlockExpectation.DroppedApproval> missing = ApprovalBlockExpectation.Missing(
			described, [new ApprovalBlockExpectation.ApprovalExpectation("Approve1", ExpectsApprover: true)]);

		// Assert
		missing.Should().ContainSingle(because: "the element has nobody assigned to approve it")
			.Which.BlockPresent.Should().BeTrue(
				because: "the block itself survived, so the warning must describe the approver drop rather than "
					+ "claim the whole configuration is missing — different cause, different message");
	}

	[Test]
	[Description("A caller who sent no approver is not warned about one: the check reports what the request asked for, so an element legitimately left with its existing approver stays quiet.")]
	public void Missing_ShouldStaySilentAboutTheApprover_WhenNoneWasSent() {
		// Arrange
		var described = new DescribeProcessResult {
			Elements = [
				new DescribedElement { Name = "Approve1", Approval = new DescribedApproval { ApproverType = null } }
			]
		};

		// Act
		IReadOnlyList<ApprovalBlockExpectation.DroppedApproval> missing =
			ApprovalBlockExpectation.Missing(described, [Expect("Approve1")]);

		// Assert
		missing.Should().BeEmpty(
			because: "an update that said nothing about the approver keeps whatever the element had, and a "
				+ "read-back reporting none is not evidence that anything was discarded");
	}

	#endregion

	#region Methods: BuildWarning

	[Test]
	[Description("The warning names the affected elements, states the element is unconfigured, and gives the action that fixes it.")]
	public void BuildWarning_ShouldNameElementsAndTheFix() {
		// Act
		string warning = ApprovalBlockExpectation.BuildWarning([new ApprovalBlockExpectation.DroppedApproval("Approve1", BlockPresent: false)]);

		// Assert
		warning.Should().Contain("Approve1",
			because: "the caller has to know which element is unconfigured");
		warning.Should().Contain("UNCONFIGURED",
			because: "the point of the warning is that the element is NOT configured despite the success answer");
		warning.Should().Contain("install-process-builder",
			because: "the warning must carry the one action that fixes the usual cause");
	}

	[Test]
	[Description("The approver-drop warning says the block DID land and the approver did not, so the reader is not sent to look for a missing approval configuration that is in fact there.")]
	public void BuildWarning_ShouldDescribeAnApproverDrop_SeparatelyFromAMissingBlock() {
		// Act
		string warning = ApprovalBlockExpectation.BuildWarning(
			[new ApprovalBlockExpectation.DroppedApproval("Approve1", BlockPresent: true)]);

		// Assert
		warning.Should().Contain("WITHOUT the approver",
			because: "the two drops have different causes and different fixes, so they must not read alike");
		warning.Should().Contain("NOBODY assigned",
			because: "the consequence is what makes the warning actionable — the approval cannot be acted on");
		warning.Should().NotContain("no approval block",
			because: "the block DID come back; saying otherwise sends the reader after the wrong problem");
		warning.Should().Contain("install-process-builder",
			because: "the fix for the usual cause belongs in every variant of this warning");
	}

	[Test]
	[Description("No dropped block produces no warning, so a caller can treat null as 'nothing to emit'.")]
	public void BuildWarning_ShouldReturnNull_WhenNothingWasDropped() {
		// Act
		string warning = ApprovalBlockExpectation.BuildWarning([]);

		// Assert
		warning.Should().BeNull(because: "a clean operation must stay quiet");
	}

	#endregion

}
