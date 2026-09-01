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
/// rather than appended to it because the assertions here are about the REPORT, not the merged array —
/// every test would otherwise re-parse a body it does not look at.
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

	private static string MobileBody(string viewConfigDiffInner) =>
		$$"""
		{
			"viewConfigDiff": {{viewConfigDiffInner}},
			"viewModelConfigDiff": [],
			"modelConfigDiff": []
		}
		""";

	private static PageAppendProjection ProjectWeb(string currentInner, string incomingInner) {
		PageBodyMerger.Merge(WebBody(currentInner), WebBody(incomingInner), out PageAppendProjection projection);
		projection.Should().NotBeNull(because: "a successful merge always reports what it did");
		return projection;
	}

	private static PageAppendProjection ProjectMobile(string currentInner, string incomingInner) {
		PageBodyMerger.Merge(MobileBody(currentInner), MobileBody(incomingInner), out PageAppendProjection projection);
		projection.Should().NotBeNull(because: "a successful merge always reports what it did");
		return projection;
	}

	[Test]
	[Description("Counts a purely additive append: nothing replaced, nothing dropped, totals add up.")]
	public void Merge_WhenFragmentOnlyAdds_ProjectsTheSumWithNoLoss() {
		// The GH-1150 arithmetic in miniature: the reporter expected 106 + 16 = 122, got 121, and had no way
		// to see that before the write. Both counts and the empty loss lists are now in the response.
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

		PageAppendProjection projection = ProjectWeb(current, incoming);

		projection.CurrentOperationCount.Should().Be(2, because: "the page's own body carries two operations");
		projection.IncomingOperationCount.Should().Be(2, because: "the fragment carries two inserts");
		projection.ProjectedOperationCount.Should().Be(4,
			because: "no incoming identity collides with a current one, so every operation survives");
		projection.AddedOperationCount.Should().Be(2, because: "both incoming entries introduce a new identity");
		projection.ReplacedOperationCount.Should().Be(0);
		projection.ReplacedOperations.Should().BeEmpty();
		projection.DroppedOperationCount.Should().Be(0,
			because: "this is the case GH-1150 reported — an unrelated existing operation must not be lost");
		projection.DroppedOperations.Should().BeEmpty();
	}

	[Test]
	[Description("Names a replaced current operation, and counts it as neither a loss nor an addition.")]
	public void Merge_WhenFragmentCollidesWithCurrentIdentity_NamesTheReplacementWithoutInflatingTheTotal() {
		string current = """
			[
				{"operation":"merge","name":"UsrPanel","values":{"title":"Old"}},
				{"operation":"move","name":"UsrPanel","parentName":"Tab","propertyName":"items","index":1}
			]
			""";
		string incoming = """[{"operation":"merge","name":"UsrPanel","values":{"title":"New"}}]""";

		PageAppendProjection projection = ProjectWeb(current, incoming);

		projection.ProjectedOperationCount.Should().Be(2,
			because: "the incoming merge takes the current merge's place rather than adding to the array");
		projection.AddedOperationCount.Should().Be(0, because: "the only incoming entry replaced rather than added");
		projection.ReplacedOperationCount.Should().Be(1);
		projection.ReplacedOperations.Should().ContainSingle()
			.Which.Should().Be("merge UsrPanel", because: "the label names the verb and the component");
		projection.DroppedOperationCount.Should().Be(0,
			because: "a replacement is not a loss — the operation survives carrying the caller's values");
	}

	[Test]
	[Description("Reports the one remaining lossy case: a further current entry of an already-superseded identity.")]
	public void Merge_WhenCurrentRepeatsAnIdentityTheFragmentSupersedes_ReportsTheFurtherEntryAsDropped() {
		// The merger replaces the FIRST occurrence and drops any later one rather than re-applying stale
		// values after the replacement. That is deliberate, but it IS a lost operation, and before #1150
		// nothing said so.
		string current = """
			[
				{"operation":"merge","name":"UsrPanel","values":{"title":"First"}},
				{"operation":"merge","name":"UsrPanel","values":{"visible":true}}
			]
			""";
		string incoming = """[{"operation":"merge","name":"UsrPanel","values":{"title":"Incoming"}}]""";

		PageAppendProjection projection = ProjectWeb(current, incoming);

		projection.CurrentOperationCount.Should().Be(2);
		projection.ProjectedOperationCount.Should().Be(1,
			because: "the replacement takes the first slot and the further duplicate is not carried over");
		projection.DroppedOperationCount.Should().Be(1);
		projection.DroppedOperations.Should().ContainSingle().Which.Should().Be("merge UsrPanel");
		projection.ReplacedOperationCount.Should().Be(1,
			because: "the first occurrence was replaced, which is reported separately from the drop");
	}

	[Test]
	[Description("A property remove and an element remove for one name are labelled distinctly.")]
	public void Merge_WhenPropertyRemoveIsReplaced_LabelDistinguishesItFromAnElementRemove() {
		// The two are different identities because JsonDiffApplier routes them into different groups. If the
		// label collapsed them, two rows would read as one repeated line.
		string current = """
			[
				{"operation":"remove","name":"UsrPanel"},
				{"operation":"remove","name":"UsrPanel","properties":["layoutConfig"]}
			]
			""";
		string incoming = """[{"operation":"remove","name":"UsrPanel","properties":["layoutConfig"]}]""";

		PageAppendProjection projection = ProjectWeb(current, incoming);

		projection.ReplacedOperations.Should().ContainSingle()
			.Which.Should().Be("remove(properties) UsrPanel",
				because: "the property-targeting discriminator must be visible in the label");
		projection.ProjectedOperationCount.Should().Be(2,
			because: "the element remove is a different identity and survives untouched");
		projection.DroppedOperationCount.Should().Be(0);
	}

	[Test]
	[Description("An entry carrying no operation verb is named rather than blanked.")]
	public void Merge_WhenEntryHasNoOperationVerb_LabelsItExplicitly() {
		string current = """[{"name":"UsrPanel","values":{"title":"Old"}}]""";
		string incoming = """[{"name":"UsrPanel","values":{"title":"New"}}]""";

		PageAppendProjection projection = ProjectWeb(current, incoming);

		projection.ReplacedOperations.Should().ContainSingle()
			.Which.Should().Be("(no operation) UsrPanel",
				because: "a missing verb is a real, distinct identity in the merge and must not render as a blank");
	}

	[Test]
	[Description("Mobile bodies project identically — both dialects share the viewConfigDiff merge.")]
	public void Merge_WhenBodyIsMobile_ProjectsTheSameWayAsWeb() {
		string current = """
			[
				{"operation":"merge","name":"UsrPanel","values":{"title":"Old"}},
				{"operation":"merge","name":"UsrPanel","values":{"visible":true}}
			]
			""";
		string incoming = """[{"operation":"merge","name":"UsrPanel","values":{"title":"New"}}]""";

		PageAppendProjection projection = ProjectMobile(current, incoming);

		projection.ReplacedOperations.Should().ContainSingle().Which.Should().Be("merge UsrPanel");
		projection.DroppedOperationCount.Should().Be(1,
			because: "MergeMobile routes through the same MergeViewConfigDiffOperations as MergeWeb");
	}

	[Test]
	[Description("Unidentified entries are counted but never reported as replaced or dropped.")]
	public void Merge_WhenEntriesLackAUsableName_CountsThemWithoutClaimingAnIdentity() {
		// An entry whose name is absent or not a JSON string is never merged and never reordered. It still
		// occupies a slot, so the counts must include it, but it has no identity to name.
		string current = """[{"operation":"merge","name":123,"values":{"title":"Old"}}]""";
		string incoming = """[{"operation":"merge","values":{"title":"New"}}]""";

		PageAppendProjection projection = ProjectWeb(current, incoming);

		projection.CurrentOperationCount.Should().Be(1);
		projection.IncomingOperationCount.Should().Be(1);
		projection.ProjectedOperationCount.Should().Be(2,
			because: "neither entry can collide with anything, so both are preserved");
		projection.ReplacedOperationCount.Should().Be(0);
		projection.DroppedOperationCount.Should().Be(0);
		projection.AddedOperationCount.Should().Be(0,
			because: "an unidentified incoming entry is emitted without being counted as a new identity");
	}

	[Test]
	[Description("The named lists are capped while the counts stay exact, so a cap never understates the scale.")]
	public void Merge_WhenLossExceedsTheNamingCap_KeepsCountsExactAndTruncatesOnlyTheNames() {
		// 30 distinct components, each with a duplicate current merge the fragment supersedes: 30 drops,
		// which is above the 25-entry naming cap.
		List<string> currentEntries = [];
		List<string> incomingEntries = [];
		for (int i = 0; i < 30; i++) {
			// Concatenated rather than interpolated: a raw string ending in }} would be read as an
			// interpolation close, not as JSON.
			currentEntries.Add("{\"operation\":\"merge\",\"name\":\"UsrPanel" + i + "\",\"values\":{\"title\":\"A\"}}");
			currentEntries.Add("{\"operation\":\"merge\",\"name\":\"UsrPanel" + i + "\",\"values\":{\"visible\":true}}");
			incomingEntries.Add("{\"operation\":\"merge\",\"name\":\"UsrPanel" + i + "\",\"values\":{\"title\":\"B\"}}");
		}

		PageAppendProjection projection = ProjectWeb(
			$"[{string.Join(",", currentEntries)}]",
			$"[{string.Join(",", incomingEntries)}]");

		projection.DroppedOperationCount.Should().Be(30, because: "the count is exact and unbounded");
		projection.DroppedOperations.Should().HaveCount(25, because: "only the named list is capped");
		projection.ProjectedOperationCount.Should().Be(30,
			because: "each component keeps exactly one operation after the merge");
	}
}
