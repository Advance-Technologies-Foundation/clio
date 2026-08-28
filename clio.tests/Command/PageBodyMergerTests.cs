using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Append-merge semantics of <see cref="PageBodyMerger"/>'s viewConfigDiff merge (GitHub #1132).
/// </summary>
/// <remarks>
/// Deliberately traited <c>Module = Command</c>, not <c>Module = McpServer</c>: the subject
/// (<c>clio/Command/PageBodyMerger.cs</c>) is a root-level <c>clio/Command/</c> file, which the
/// AGENTS.md module-to-source table maps to <c>Command</c>. The pre-existing
/// <c>PageBodyMerger_*</c> tests live in the <c>Module = McpServer</c> fixture
/// <c>Command/McpServer/PageToolsTests.cs</c>, so a change confined to the merger selects
/// <c>Module=Command</c> under the smart-regression policy and runs NONE of them. This fixture adds
/// <c>Module=Command</c> coverage for the merge identity and mirrors the sibling
/// <see cref="PageInsertDowngradeDetectorTests"/> layout. It does NOT relocate the pre-existing
/// <c>PageBodyMerger_*</c> tests, which remain in the McpServer fixture and are still not selected by a
/// merger-only change — run both module filters when touching this file.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class PageBodyMergerTests {

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

	private static JArray MergeWebViewConfigDiff(string currentInner, string incomingInner) {
		string merged = PageBodyMerger.Merge(WebBody(currentInner), WebBody(incomingInner));
		PageSchemaSectionReader.TryRead(merged, out string content, "SCHEMA_VIEW_CONFIG_DIFF")
			.Should().BeTrue(because: "the merge must return a body whose viewConfigDiff marker pair is intact");
		return JArray.Parse(content.Trim());
	}

	private static JArray MergeMobileViewConfigDiff(string currentInner, string incomingInner) {
		string merged = PageBodyMerger.Merge(MobileBody(currentInner), MobileBody(incomingInner));
		JToken viewConfigDiff = JObject.Parse(merged)["viewConfigDiff"];
		viewConfigDiff.Should().BeOfType<JArray>(
			because: "the mobile merge must return a body whose viewConfigDiff is still a JSON array");
		return (JArray)viewConfigDiff;
	}

	private static string Op(JToken entry) => entry["operation"]?.ToString();
	private static string Name(JToken entry) => entry["name"]?.ToString();

	/// <summary>The #1132 precondition: two valid operations targeting one component name.</summary>
	private const string MoveAndMergeOneName =
		"""
		[
			{
				"operation": "move",
				"name": "ExamplePanel",
				"parentName": "OverviewTab",
				"propertyName": "items",
				"index": 2
			},
			{
				"operation": "merge",
				"name": "ExamplePanel",
				"values": { "title": "$Resources.Strings.ExamplePanel_title" }
			}
		]
		""";

	/// <summary>An append fragment that never references <c>ExamplePanel</c>.</summary>
	private const string UnrelatedInsert =
		"""
		[
			{
				"operation": "insert",
				"name": "UsrNewButton",
				"parentName": "ActionButtonsContainer",
				"propertyName": "items",
				"index": 0,
				"values": { "type": "crt.Button", "caption": "New" }
			}
		]
		""";

	[Test]
	[Description("GitHub #1132: an append whose fragment is unrelated must keep BOTH existing web operations that share one component name")]
	public void Merge_Should_PreserveEveryCurrentOperation_WhenTwoShareOneName_Web() {
		// Arrange — current body carries a move AND a merge for "ExamplePanel"; the incoming fragment
		// inserts an unrelated, uniquely named component and never mentions "ExamplePanel".
		string current = MoveAndMergeOneName;
		string incoming = UnrelatedInsert;

		// Act
		JArray merged = MergeWebViewConfigDiff(current, incoming);

		// Assert
		// Asserted BY INDEX, not with Any(...): viewConfigDiff is an ordered operation list, so a
		// regression that silently reorders operations must fail here too.
		merged.Should().HaveCount(3,
			because: "append must add the incoming insert and drop nothing — before #1132 the move was silently deduped away, leaving 2");
		Op(merged[0]).Should().Be("move",
			because: "the existing move must survive an append that never referenced its component");
		Name(merged[0]).Should().Be("ExamplePanel",
			because: "the surviving move must still target the original component");
		merged[0]["index"]?.Value<int>().Should().Be(2,
			because: "the move's placement must be preserved verbatim, not re-derived");
		merged[0]["parentName"]?.ToString().Should().Be("OverviewTab",
			because: "losing parentName would relocate the panel just as dropping the whole move did");
		Op(merged[1]).Should().Be("merge",
			because: "the existing merge must stay after the move, in its original relative order");
		Name(merged[1]).Should().Be("ExamplePanel",
			because: "the merge still targets the same component as the move");
		Name(merged[2]).Should().Be("UsrNewButton",
			because: "the incoming operation is appended after every existing operation");
	}

	[Test]
	[Description("GitHub #1132 on the mobile surface: MergeMobile must also keep both existing operations that share one component name")]
	public void Merge_Should_PreserveEveryCurrentOperation_WhenTwoShareOneName_Mobile() {
		// Arrange — the same precondition expressed as a plain-JSON mobile body, routed through MergeMobile.
		string current = MoveAndMergeOneName;
		string incoming = UnrelatedInsert;

		// Act
		JArray merged = MergeMobileViewConfigDiff(current, incoming);

		// Assert
		merged.Should().HaveCount(3,
			because: "mobile and web share one merge helper, so the mobile surface must not lose the move either");
		Op(merged[0]).Should().Be("move",
			because: "the existing mobile move must survive an unrelated append");
		Op(merged[1]).Should().Be("merge",
			because: "the existing mobile merge must keep its position after the move");
		Name(merged[2]).Should().Be("UsrNewButton",
			because: "the incoming mobile operation is appended last");
	}

	[Test]
	[Description("An incoming merge must not destroy the current insert for the same name; both survive, the insert first (note the merge is inert at apply time — PageInsertDowngradeDetector reports that separately)")]
	public void Merge_Should_KeepInsertAndAddMerge_WhenIncomingMergesAnInsertedName() {
		// Arrange
		const string currentInsert = """[{"operation":"insert","name":"UsrName","values":{"type":"crt.Input"}}]""";
		const string incomingMerge = """[{"operation":"merge","name":"UsrName","values":{"visible":false}}]""";

		// Act
		JArray merged = MergeWebViewConfigDiff(currentInsert, incomingMerge);

		// Assert
		merged.Should().HaveCount(2,
			because: "operation identity is (operation, name), so a merge no longer collides with — and destroys — an insert");
		Op(merged[0]).Should().Be("insert",
			because: "the insert must come first: a merge patches an element that must already exist");
		Op(merged[1]).Should().Be("merge",
			because: "the incoming merge is preserved rather than deleted — it is NOT applied at runtime (the differ runs the merge group before the insert group), which PageInsertDowngradeDetector warns about; preserving it keeps the caller's intent visible in the body instead of destroying the insert as the pre-#1132 merger did");
	}

	[Test]
	[Description("When operation AND name both match, the incoming entry replaces the current one at the current entry's position")]
	public void Merge_Should_ReplaceInPlace_WhenOperationAndNameBothMatch() {
		// Arrange
		const string current = """
			[
				{"operation":"insert","name":"A","values":{"type":"crt.Input"}},
				{"operation":"merge","name":"B","values":{"size":"large"}},
				{"operation":"insert","name":"C","values":{"type":"crt.Button"}}
			]
			""";
		const string incoming = """[{"operation":"merge","name":"B","values":{"size":"small"}}]""";

		// Act
		JArray merged = MergeWebViewConfigDiff(current, incoming);

		// Assert
		merged.Should().HaveCount(3,
			because: "a same-identity collision replaces rather than appends, so the count is unchanged");
		Name(merged[1]).Should().Be("B",
			because: "the replacement keeps the current entry's position — appending it to the tail would change when it applies");
		merged[1]["values"]?["size"]?.ToString().Should().Be("small",
			because: "incoming wins on a genuine (operation, name) collision");
		Name(merged[0]).Should().Be("A", because: "entries before the collision are untouched");
		Name(merged[2]).Should().Be("C", because: "entries after the collision are untouched");
	}

	[Test]
	[Description("A further current entry of an identity the incoming fragment already superseded is dropped, not re-applied after the replacement")]
	public void Merge_Should_DropFurtherCurrentDuplicates_WhenIncomingSupersedesTheIdentity() {
		// Arrange
		// The duplicates are deliberately NON-adjacent: an intervening unrelated entry is where
		// position-tracking bugs hide, and adjacent-only fixtures cannot catch them.
		const string current = """
			[
				{"operation":"merge","name":"B","values":{"v":1}},
				{"operation":"insert","name":"Other","values":{"type":"crt.Button"}},
				{"operation":"merge","name":"B","values":{"v":2}}
			]
			""";
		const string incoming = """[{"operation":"merge","name":"B","values":{"v":3}}]""";

		// Act
		JArray merged = MergeWebViewConfigDiff(current, incoming);

		// Assert
		merged.Should().HaveCount(2,
			because: "the differ applies a per-name group in array order, so keeping the second stale merge would re-apply v:2 after the replacement; the unrelated entry between them is untouched");
		merged[0]["values"]?["v"]?.Value<int>().Should().Be(3,
			because: "the replacement lands at the FIRST occurrence's position");
		Name(merged[1]).Should().Be("Other",
			because: "an unrelated entry sitting between two superseded duplicates must keep its place");
	}

	[Test]
	[Description("Operation verbs are compared ordinally: the differ switches on the raw string with no default case, so a mis-cased 'Merge' must not collide with — and delete — a working 'merge'")]
	public void Merge_Should_TreatOperationCaseSensitively() {
		// Arrange
		const string current = """[{"operation":"merge","name":"B","values":{"v":1}}]""";
		const string incoming = """[{"operation":"Merge","name":"B","values":{"v":2}}]""";

		// Act
		JArray merged = MergeWebViewConfigDiff(current, incoming);

		// Assert
		merged.Should().HaveCount(2,
			because: "JsonDiffApplier.GetSplittedOperations switches on the exact-case operation string with no default branch, so 'Merge' is discarded at apply time — folding case here would let it replace and therefore delete the working 'merge'");
		merged[0]["values"]?["v"]?.Value<int>().Should().Be(1,
			because: "the working lower-case merge must survive untouched at its original position");
	}

	[Test]
	[Description("Component names are compared case-sensitively, mirroring the platform differ's Ordinal grouping")]
	public void Merge_Should_TreatNameCaseSensitively() {
		// Arrange
		const string current = """[{"operation":"merge","name":"Panel","values":{"v":1}}]""";
		const string incoming = """[{"operation":"merge","name":"panel","values":{"v":2}}]""";

		// Act
		JArray merged = MergeWebViewConfigDiff(current, incoming);

		// Assert
		merged.Should().HaveCount(2,
			because: "JsonDiffApplier groups operations by name with StringComparer.Ordinal, so the merger must never collapse two names the differ keeps apart");
		Name(merged[0]).Should().Be("Panel",
			because: "the current entry keeps its position; asserting only the count would pass under a regression that swapped the two");
		Name(merged[1]).Should().Be("panel",
			because: "the incoming entry is appended as a distinct operation");
	}

	[Test]
	[Description("A missing 'operation' forms its own identity rather than being defaulted to some assumed platform operation")]
	public void Merge_Should_TreatMissingOperationAsItsOwnIdentity() {
		// Arrange
		const string current = """[{"name":"X","values":{"v":1}}]""";
		const string incoming = """[{"operation":"merge","name":"X","values":{"v":2}}]""";

		// Act
		JArray merged = MergeWebViewConfigDiff(current, incoming);

		// Assert
		merged.Should().HaveCount(2,
			because: "guessing a default for a missing operation would silently re-introduce the #1132 replacement of an operation the caller never named");
		merged[0]["values"]?["v"]?.Value<int>().Should().Be(1,
			because: "the operation-less current entry keeps its original position and values");
	}

	[Test]
	[Description("An entry with no 'name' keeps its original position instead of being relocated to the end of the array")]
	public void Merge_Should_PreserveUnnamedEntryPosition() {
		// Arrange
		const string current = """
			[
				{"operation":"merge","name":"A","values":{"v":1}},
				{"operation":"merge","values":{"v":2}},
				{"operation":"merge","name":"C","values":{"v":3}}
			]
			""";
		const string incoming = """[{"operation":"merge","name":"C","values":{"v":9}}]""";

		// Act
		JArray merged = MergeWebViewConfigDiff(current, incoming);

		// Assert
		merged.Should().HaveCount(3,
			because: "the only collision is on C, so nothing is added or lost");
		merged[1]["name"].Should().BeNull(
			because: "viewConfigDiff is an ordered operation list — relocating the name-less entry to the tail changes when it applies");
		merged[1]["values"]?["v"]?.Value<int>().Should().Be(2,
			because: "the name-less entry must be preserved verbatim at index 1");
	}

	[Test]
	[Description("A non-object array element has no identity: it is preserved verbatim and never relocated")]
	public void Merge_Should_PreserveNonObjectArrayEntry() {
		// Arrange
		const string current = """["stray", {"operation":"merge","name":"A","values":{"v":1}}]""";
		const string incoming = """[{"operation":"merge","name":"A","values":{"v":2}}]""";

		// Act
		JArray merged = MergeWebViewConfigDiff(current, incoming);

		// Assert
		merged.Should().HaveCount(2,
			because: "the stray element is kept and the named entry is replaced in place");
		merged[0].Type.Should().Be(JTokenType.String,
			because: "an element the merger cannot identify must stay exactly where the caller put it");
	}

	[Test]
	[Description("Incoming entries with no current counterpart are appended after all current entries, in incoming order")]
	public void Merge_Should_AppendUnmatchedIncomingEntriesInIncomingOrder() {
		// Arrange
		const string current = """[{"operation":"merge","name":"A","values":{"v":1}}]""";
		const string incoming = """
			[
				{"operation":"insert","name":"X","values":{"type":"crt.Button"}},
				{"operation":"insert","name":"Y","values":{"type":"crt.Button"}},
				{"operation":"insert","name":"Z","values":{"type":"crt.Button"}}
			]
			""";

		// Act
		JArray merged = MergeWebViewConfigDiff(current, incoming);

		// Assert
		merged.Should().HaveCount(4, because: "one current entry plus three new incoming entries");
		Name(merged[0]).Should().Be("A", because: "current entries come first");
		Name(merged[1]).Should().Be("X", because: "incoming order must be preserved");
		Name(merged[2]).Should().Be("Y", because: "incoming order must be preserved");
		Name(merged[3]).Should().Be("Z", because: "incoming order must be preserved");
	}

	[Test]
	[Description("An incoming fragment's own order is preserved when it mixes identified and unidentified entries")]
	public void Merge_Should_PreserveIncomingOrder_WhenFragmentMixesIdentifiedAndUnidentifiedEntries() {
		// Arrange
		const string current = """[{"operation":"merge","name":"A","values":{"v":1}}]""";
		const string incoming = """
			[
				{"operation":"merge","values":{"v":2}},
				{"operation":"insert","name":"X","values":{"type":"crt.Button"}}
			]
			""";

		// Act
		JArray merged = MergeWebViewConfigDiff(current, incoming);

		// Assert
		merged.Should().HaveCount(3, because: "one current entry plus both incoming entries");
		Name(merged[0]).Should().Be("A", because: "current entries come first");
		merged[1]["name"].Should().BeNull(
			because: "the caller wrote the name-less operation BEFORE the insert, and a viewConfigDiff is an ordered operation list — sorting identified entries ahead of unidentified ones would change when each applies");
		Name(merged[2]).Should().Be("X",
			because: "the identified incoming entry keeps its position after the name-less one the caller wrote first");
	}

	[Test]
	[Description("When one incoming fragment repeats an identity, the last spelling wins")]
	public void Merge_Should_KeepLastIncomingEntry_WhenIncomingRepeatsOneIdentity() {
		// Arrange
		const string current = "[]";
		const string incoming = """
			[
				{"operation":"merge","name":"B","values":{"v":1}},
				{"operation":"merge","name":"B","values":{"v":2}}
			]
			""";

		// Act
		JArray merged = MergeWebViewConfigDiff(current, incoming);

		// Assert
		merged.Should().HaveCount(1,
			because: "a caller who repeats an identity within one fragment means the later spelling");
		merged[0]["values"]?["v"]?.Value<int>().Should().Be(2,
			because: "the last incoming entry wins within a single fragment");
	}

	[Test]
	[Description("A property removal and an element removal for one component are distinct operations: the differ routes them into different groups, so an incoming property removal must not delete a current element removal")]
	public void Merge_Should_NotConflate_PropertyRemove_With_ElementRemove() {
		// Arrange
		const string current = """[{"operation":"remove","name":"Panel"}]""";
		const string incoming = """[{"operation":"remove","name":"Panel","properties":["caption"]}]""";

		// Act
		JArray merged = MergeWebViewConfigDiff(current, incoming);

		// Assert
		merged.Should().HaveCount(2,
			because: "JsonDiffApplier.GetSplittedOperations routes a remove carrying a properties array into RemoveProperties and a bare remove into Remove — two groups applied in two passes — so collapsing them would silently resurrect the deleted Panel");
		merged[0]["properties"].Should().BeNull(
			because: "the current element removal must survive at its original position");
		merged[1]["properties"].Should().NotBeNull(
			because: "the incoming property removal is added as a separate operation");
	}

	[Test]
	[Description("Two property removals for one component still collide on identity, so the incoming one replaces the current one in place")]
	public void Merge_Should_ReplaceInPlace_WhenBothRemovesTargetProperties() {
		// Arrange
		const string current = """[{"operation":"remove","name":"Header","properties":["caption"]}]""";
		const string incoming = """[{"operation":"remove","name":"Header","properties":["tooltip"]}]""";

		// Act
		JArray merged = MergeWebViewConfigDiff(current, incoming);

		// Assert
		merged.Should().HaveCount(1,
			because: "both entries are property removals for one component, so they share an identity and the incoming one wins");
		merged[0]["properties"]!.Single().ToString().Should().Be("tooltip",
			because: "incoming wins a genuine same-identity collision");
	}

	[Test]
	[Description("A non-string 'name' yields no identity, so the entry is preserved in place and never conflated with the string spelling of the same value")]
	public void Merge_Should_NotConflate_NumericName_With_StringName() {
		// Arrange
		const string current = """[{"operation":"merge","name":123,"values":{"v":1}}]""";
		const string incoming = """[{"operation":"merge","name":"123","values":{"v":2}}]""";

		// Act
		JArray merged = MergeWebViewConfigDiff(current, incoming);

		// Assert
		merged.Should().HaveCount(2,
			because: "a bare ToString() would make {\"name\":123} and {\"name\":\"123\"} share an identity; requiring a JSON string keeps the unidentifiable entry on the safe preserve-in-place branch");
		merged[0]["values"]?["v"]?.Value<int>().Should().Be(1,
			because: "the entry the merger cannot identify keeps its original position and values");
	}

	[Test]
	[Description("An append that merges an inserted name no longer produces the orphaning downgrade PageInsertDowngradeDetector warns about, because the insert is kept instead of being replaced")]
	public void Merge_Should_ProduceBodyWithNoOrphanWarning_WhenIncomingMergesAnInsertedName() {
		// Arrange
		string currentBody = WebBody("""[{"operation":"insert","name":"UsrName","values":{"type":"crt.Input"}}]""");
		string incomingBody = WebBody("""[{"operation":"merge","name":"UsrName","values":{"visible":false}}]""");

		// Act
		string mergedBody = PageBodyMerger.Merge(currentBody, incomingBody);
		IReadOnlyList<string> warnings = PageInsertDowngradeDetector.Detect(currentBody, mergedBody);

		// Assert
		warnings.Should().BeEmpty(
			because: "the merge no longer replaces the insert, so the component is not orphaned and the downgrade warning correctly does not fire");
		// Deliberately NOT asserted here: that the preserved merge actually takes effect. It does not —
		// the differ runs the merge group before the insert group, so a merge beside an insert for one
		// name is inert. Preserving it is still better than destroying the insert, and reporting the
		// inertness is tracked separately in GH-1240 rather than widened into this fix.
	}
}
