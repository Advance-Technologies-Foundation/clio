using System.Collections.Generic;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// The <see cref="PageAppendProjection"/> that <see cref="PageBodyMerger"/> reports alongside an append
/// merge (GitHub #1150), so a caller can see the outcome before committing to the write.
/// </summary>
/// <remarks>
/// Traited <c>Module = Command</c> for the reason spelled out in <see cref="PageBodyMergerTests"/>: the
/// subject is a root-level <c>clio/Command/</c> file, and the pre-existing <c>PageBodyMerger_*</c> tests in
/// the <c>Module = McpServer</c> fixture are NOT selected by a merger-only change. Split from that fixture
/// rather than appended to it because the assertions here are about the REPORT, not the merged array.
/// <para>
/// The three loss channels are covered separately and their independence is asserted, because the first
/// version of this feature reported only <c>droppedOperations</c> while claiming it was the only way an
/// append loses an operation. It was not.
/// </para>
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class PageAppendProjectionTests {

	private static string WebBody(string viewConfigDiffInner) =>
		$$"""
		define("Test", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {
			return {
				viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/{{viewConfigDiffInner}}/**SCHEMA_VIEW_CONFIG_DIFF*/,
				viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
				modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
				handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
				converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
				validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
			};
		});
		""";

	/// <summary>A web body with every marker pair EXCEPT <c>SCHEMA_VIEW_CONFIG_DIFF</c>.</summary>
	private static string WebBodyWithoutViewConfigDiffSection() =>
		"""
		define("Test", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {
			return {
				viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
				modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
				handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/
			};
		});
		""";

	private static string MobileBody(string viewConfigDiffInner) =>
		$$"""
		{
			"viewConfigDiff": {{viewConfigDiffInner}},
			"viewModelConfigDiff": [],
			"modelConfigDiff": []
		}
		""";

	private const string SingleInsert =
		"""[{"operation":"insert","name":"UsrName","parentName":"Main","values":{"type":"crt.Input"}}]""";

	[Test]
	[Description("Counts a purely additive append: nothing replaced, nothing lost on any channel, totals add up.")]
	public void Merge_WhenFragmentOnlyAdds_ProjectsTheSumWithNoLoss() {
		// Arrange — the GH-1150 arithmetic in miniature: the reporter expected 106 + 16 = 122, got 121, and
		// had no way to see that before the write.
		string current = """
			[
				{"operation":"merge","name":"ContactRolesExpansionPanel","values":{"title":"Coverage"}},
				{"operation":"move","name":"ContactRolesExpansionPanel","parentName":"OverviewTab","propertyName":"items","index":3}
			]
			""";
		string incoming = """
			[
				{"operation":"insert","name":"UsrNewWorkspaceTab","parentName":"Tabs","propertyName":"items","index":1,"values":{"type":"crt.TabContainer"}},
				{"operation":"insert","name":"UsrNewWorkspaceRoot","parentName":"UsrNewWorkspaceTab","propertyName":"items","index":0,"values":{"type":"crt.FlexContainer"}}
			]
			""";

		// Act
		PageBodyMerger.Merge(WebBody(current), WebBody(incoming), out PageAppendProjection projection);

		// Assert
		projection.Should().NotBeNull(because: "a successful merge always reports what it did");
		projection.CurrentOperationCount.Should().Be(2,
			because: "the page's own body carries two operations for one component");
		projection.IncomingOperationCount.Should().Be(2, because: "the fragment carries two inserts");
		projection.ProjectedOperationCount.Should().Be(4,
			because: "no incoming identity collides with a current one, so every operation survives");
		projection.AddedOperationCount.Should().Be(2, because: "both incoming entries introduce a new identity");
		projection.ReplacedOperationCount.Should().Be(0,
			because: "the fragment names no identity the current body already carries");
		projection.DroppedOperationCount.Should().Be(0,
			because: "this is the case GH-1150 reported: an unrelated append must not lose an existing operation");
		projection.CollapsedIncomingOperationCount.Should().Be(0,
			because: "the fragment's two entries have distinct identities, so neither supersedes the other");
		projection.ViewConfigDiffApplied.Should().BeTrue(
			because: "the current body has the marker pair, so the merged array reaches the written body");
	}

	[Test]
	[Description("Names a replaced current operation, and counts it as neither a loss nor an addition.")]
	public void Merge_WhenFragmentCollidesWithCurrentIdentity_NamesTheReplacementWithoutInflatingTheTotal() {
		// Arrange
		string current = """
			[
				{"operation":"merge","name":"UsrPanel","values":{"title":"Old"}},
				{"operation":"move","name":"UsrPanel","parentName":"Tab","propertyName":"items","index":1}
			]
			""";
		string incoming = """[{"operation":"merge","name":"UsrPanel","values":{"title":"New"}}]""";

		// Act
		PageBodyMerger.Merge(WebBody(current), WebBody(incoming), out PageAppendProjection projection);

		// Assert
		projection.ProjectedOperationCount.Should().Be(2,
			because: "the incoming merge takes the current merge's place rather than adding to the array");
		projection.AddedOperationCount.Should().Be(0,
			because: "the only incoming entry replaced rather than added");
		projection.ReplacedOperationCount.Should().Be(1, because: "exactly one current identity was matched");
		projection.ReplacedOperations.Should().ContainSingle(
				because: "one replacement means one label")
			.Which.Should().Be("merge UsrPanel", because: "the label names the verb and the component");
		projection.DroppedOperationCount.Should().Be(0,
			because: "a replacement is not a loss: the operation survives carrying the caller's values");
	}

	[Test]
	[Description("Reports a further current entry of an already-superseded identity as dropped.")]
	public void Merge_WhenCurrentRepeatsAnIdentityTheFragmentSupersedes_ReportsTheFurtherEntryAsDropped() {
		// Arrange — the merger replaces the FIRST occurrence and drops any later one rather than re-applying
		// stale values after the replacement. Deliberate, but it IS a lost operation.
		string current = """
			[
				{"operation":"merge","name":"UsrPanel","values":{"title":"First"}},
				{"operation":"merge","name":"UsrPanel","values":{"visible":true}}
			]
			""";
		string incoming = """[{"operation":"merge","name":"UsrPanel","values":{"title":"Incoming"}}]""";

		// Act
		PageBodyMerger.Merge(WebBody(current), WebBody(incoming), out PageAppendProjection projection);

		// Assert
		projection.CurrentOperationCount.Should().Be(2,
			because: "the current body carries the same identity twice");
		projection.ProjectedOperationCount.Should().Be(1,
			because: "the replacement takes the first slot and the further duplicate is not carried over");
		projection.DroppedOperationCount.Should().Be(1, because: "exactly one current entry is lost");
		projection.DroppedOperations.Should().ContainSingle(because: "one drop means one label")
			.Which.Should().Be("merge UsrPanel", because: "the caller must be told which operation goes");
		projection.ReplacedOperationCount.Should().Be(1,
			because: "the first occurrence was replaced, which is reported separately from the drop");
		projection.CollapsedIncomingOperationCount.Should().Be(0,
			because: "the duplication is in the CURRENT body, not in the fragment");
	}

	[Test]
	[Description("GH-1150 review: a fragment that carries one identity twice loses its own earlier entry, and says so.")]
	public void Merge_WhenFragmentRepeatsOneIdentity_ReportsTheEarlierEntryAsCollapsed() {
		// Arrange — the likeliest loss in practice, and the one the first version of this projection missed
		// entirely: the caller's OWN operation vanishes, and the response used to say dropped=0.
		string incoming = """
			[
				{"operation":"merge","name":"UsrPanel","values":{"title":"A"}},
				{"operation":"merge","name":"UsrPanel","values":{"visible":true}}
			]
			""";

		// Act
		string merged = PageBodyMerger.Merge(WebBody("[]"), WebBody(incoming), out PageAppendProjection projection);

		// Assert
		projection.IncomingOperationCount.Should().Be(2, because: "the caller authored two operations");
		projection.ProjectedOperationCount.Should().Be(1,
			because: "merging by identity keeps only the last spelling of the repeated identity");
		projection.AddedOperationCount.Should().Be(1, because: "only one distinct identity is emitted");
		projection.CollapsedIncomingOperationCount.Should().Be(1,
			because: "the earlier entry is a real loss and must be counted, not absorbed silently");
		projection.CollapsedIncomingOperations.Should().ContainSingle(because: "one collapse means one label")
			.Which.Should().Be("merge UsrPanel", because: "the caller must be told which of their entries goes");
		projection.DroppedOperationCount.Should().Be(0,
			because: "dropped is scoped to the SERVER's body; conflating the two hides whose fragment to fix");
		merged.Should().NotContain("\"A\"",
			because: "the assertion above is only meaningful if the earlier entry's values really are gone");
	}

	[Test]
	[Description("GH-1150 review: a current web body with no viewConfigDiff marker pair discards the merged array, and the projection says so.")]
	public void Merge_WhenCurrentBodyHasNoViewConfigDiffSection_ReportsThatTheMergedArrayIsNotApplied() {
		// Arrange — ReplaceSection is a single-match regex over a marker PAIR; with no pair it returns the
		// body unchanged and the merged array is silently discarded. Nothing upstream rejects such a body:
		// marker-integrity validation is skipped in append mode and only ever inspected the fragment.
		string current = WebBodyWithoutViewConfigDiffSection();

		// Act
		string merged = PageBodyMerger.Merge(current, WebBody(SingleInsert), out PageAppendProjection projection);

		// Assert
		projection.ViewConfigDiffApplied.Should().BeFalse(
			because: "without the marker pair the merged array cannot be written back, and every count above it describes an array the write throws away");
		merged.Should().NotContain("SCHEMA_VIEW_CONFIG_DIFF",
			because: "the assertion above is only meaningful if the section really is absent from the result");
		merged.Should().NotContain("UsrName",
			because: "the caller's insert is discarded entirely, which is what makes a silent success dangerous");
	}

	[Test]
	[Description("A normal web append reports the merged array as applied.")]
	public void Merge_WhenCurrentBodyHasTheViewConfigDiffSection_ReportsThatTheMergedArrayIsApplied() {
		// Arrange
		string current = WebBody("[]");

		// Act
		string merged = PageBodyMerger.Merge(current, WebBody(SingleInsert), out PageAppendProjection projection);

		// Assert
		projection.ViewConfigDiffApplied.Should().BeTrue(
			because: "the marker pair is present, so the merged array reaches the written body");
		merged.Should().Contain("UsrName",
			because: "the flag is only trustworthy if it tracks whether the operation actually landed");
	}

	[Test]
	[Description("A mobile body reports the merged array as applied even when it carries no viewConfigDiff property.")]
	public void Merge_WhenMobileBodyOmitsViewConfigDiff_StillReportsItApplied() {
		// Arrange — the mobile path assigns the property unconditionally, creating it when absent, so it has
		// no marker-pair precondition to miss.
		string currentWithoutSection = """
			{
				"viewModelConfigDiff": [],
				"modelConfigDiff": []
			}
			""";

		// Act
		PageBodyMerger.Merge(
			currentWithoutSection, MobileBody(SingleInsert), out PageAppendProjection projection);

		// Assert
		projection.ViewConfigDiffApplied.Should().BeTrue(
			because: "MergeMobile creates the property rather than writing back into a marker pair");
		projection.AddedOperationCount.Should().Be(1, because: "the incoming insert is a new identity");
	}

	[Test]
	[Description("A property remove and an element remove for one name are labelled distinctly.")]
	public void Merge_WhenPropertyRemoveIsReplaced_LabelDistinguishesItFromAnElementRemove() {
		// Arrange — the two are different identities because JsonDiffApplier routes them into different
		// groups. If the label collapsed them, two rows would read as one repeated line.
		string current = """
			[
				{"operation":"remove","name":"UsrPanel"},
				{"operation":"remove","name":"UsrPanel","properties":["layoutConfig"]}
			]
			""";
		string incoming = """[{"operation":"remove","name":"UsrPanel","properties":["layoutConfig"]}]""";

		// Act
		PageBodyMerger.Merge(WebBody(current), WebBody(incoming), out PageAppendProjection projection);

		// Assert
		projection.ReplacedOperations.Should().ContainSingle(because: "only the property remove is matched")
			.Which.Should().Be("remove(properties) UsrPanel",
				because: "the property-targeting discriminator must be visible in the label");
		projection.ProjectedOperationCount.Should().Be(2,
			because: "the element remove is a different identity and survives untouched");
		projection.DroppedOperationCount.Should().Be(0,
			because: "neither current entry shares an identity with the other, so nothing is superseded twice");
	}

	[Test]
	[Description("An entry carrying no operation verb is named rather than blanked.")]
	public void Merge_WhenEntryHasNoOperationVerb_LabelsItExplicitly() {
		// Arrange
		string current = """[{"name":"UsrPanel","values":{"title":"Old"}}]""";
		string incoming = """[{"name":"UsrPanel","values":{"title":"New"}}]""";

		// Act
		PageBodyMerger.Merge(WebBody(current), WebBody(incoming), out PageAppendProjection projection);

		// Assert
		projection.ReplacedOperations.Should().ContainSingle(because: "the identities match on name alone")
			.Which.Should().Be("(no operation) UsrPanel",
				because: "a missing verb is a real, distinct identity in the merge and must not render as a blank");
	}

	[Test]
	[Description("Mobile bodies project identically — both dialects share the viewConfigDiff merge.")]
	public void Merge_WhenBodyIsMobile_ProjectsTheSameWayAsWeb() {
		// Arrange
		string current = """
			[
				{"operation":"merge","name":"UsrPanel","values":{"title":"Old"}},
				{"operation":"merge","name":"UsrPanel","values":{"visible":true}}
			]
			""";
		string incoming = """[{"operation":"merge","name":"UsrPanel","values":{"title":"New"}}]""";

		// Act
		PageBodyMerger.Merge(MobileBody(current), MobileBody(incoming), out PageAppendProjection projection);

		// Assert
		projection.ReplacedOperations.Should().ContainSingle(because: "one current identity is matched")
			.Which.Should().Be("merge UsrPanel", because: "the label format is dialect-independent");
		projection.DroppedOperationCount.Should().Be(1,
			because: "MergeMobile routes through the same MergeViewConfigDiffOperations as MergeWeb");
	}

	[Test]
	[Description("Unidentified entries are counted in the totals but never reported as replaced, dropped or collapsed.")]
	public void Merge_WhenEntriesLackAUsableName_CountsThemWithoutClaimingAnIdentity() {
		// Arrange — an entry whose name is absent or not a JSON string is never merged and never reordered.
		// It still occupies a slot, so the totals must include it, but it has no identity to name.
		string current = """[{"operation":"merge","name":123,"values":{"title":"Old"}}]""";
		string incoming = """[{"operation":"merge","values":{"title":"New"}}]""";

		// Act
		PageBodyMerger.Merge(WebBody(current), WebBody(incoming), out PageAppendProjection projection);

		// Assert
		projection.CurrentOperationCount.Should().Be(1, because: "the non-string name still occupies a slot");
		projection.IncomingOperationCount.Should().Be(1, because: "the nameless entry still occupies a slot");
		projection.ProjectedOperationCount.Should().Be(2,
			because: "neither entry can collide with anything, so both are preserved");
		projection.ReplacedOperationCount.Should().Be(0, because: "no identity could be computed to match on");
		projection.DroppedOperationCount.Should().Be(0, because: "an unidentified entry is never superseded");
		projection.CollapsedIncomingOperationCount.Should().Be(0,
			because: "two unidentified entries never collapse into one another");
		projection.AddedOperationCount.Should().Be(0,
			because: "an unidentified incoming entry is emitted without being counted as a new identity, which is why the counts do not reconcile arithmetically");
	}

	[Test]
	[Description("The named lists are capped while the counts stay exact, so a cap never understates the scale.")]
	public void Merge_WhenLossExceedsTheNamingCap_KeepsCountsExactAndTruncatesOnlyTheNames() {
		// Arrange — 30 distinct components, each with a duplicate current merge the fragment supersedes:
		// 30 drops, above the 25-entry naming cap.
		List<string> currentEntries = [];
		List<string> incomingEntries = [];
		for (int i = 0; i < 30; i++) {
			// Concatenated rather than interpolated: a raw string ending in }} would be read as an
			// interpolation close, not as JSON.
			currentEntries.Add("{\"operation\":\"merge\",\"name\":\"UsrPanel" + i + "\",\"values\":{\"title\":\"A\"}}");
			currentEntries.Add("{\"operation\":\"merge\",\"name\":\"UsrPanel" + i + "\",\"values\":{\"visible\":true}}");
			incomingEntries.Add("{\"operation\":\"merge\",\"name\":\"UsrPanel" + i + "\",\"values\":{\"title\":\"B\"}}");
		}

		// Act
		PageBodyMerger.Merge(
			WebBody($"[{string.Join(",", currentEntries)}]"),
			WebBody($"[{string.Join(",", incomingEntries)}]"),
			out PageAppendProjection projection);

		// Assert
		projection.DroppedOperationCount.Should().Be(30, because: "the count is exact and unbounded");
		projection.DroppedOperations.Should().HaveCount(25,
			because: "only the named list is capped, so a pathological body cannot bury the response");
		projection.ProjectedOperationCount.Should().Be(30,
			because: "each component keeps exactly one operation after the merge");
	}

	[Test]
	[Description("The collapsed-incoming list is capped the same way, with its own exact count.")]
	public void Merge_WhenCollapsedIncomingExceedsTheNamingCap_KeepsItsCountExactToo() {
		// Arrange — 30 components, each named twice in the FRAGMENT, so all 30 collapses are caller-side.
		List<string> incomingEntries = [];
		for (int i = 0; i < 30; i++) {
			incomingEntries.Add("{\"operation\":\"merge\",\"name\":\"UsrPanel" + i + "\",\"values\":{\"title\":\"A\"}}");
			incomingEntries.Add("{\"operation\":\"merge\",\"name\":\"UsrPanel" + i + "\",\"values\":{\"title\":\"B\"}}");
		}

		// Act
		PageBodyMerger.Merge(
			WebBody("[]"), WebBody($"[{string.Join(",", incomingEntries)}]"), out PageAppendProjection projection);

		// Assert
		projection.CollapsedIncomingOperationCount.Should().Be(30,
			because: "the caller-side count is exact and unbounded, like its server-side counterpart");
		projection.CollapsedIncomingOperations.Should().HaveCount(25,
			because: "the same naming cap applies to every loss list");
		projection.ProjectedOperationCount.Should().Be(30,
			because: "one entry per identity survives the collapse");
	}
}
