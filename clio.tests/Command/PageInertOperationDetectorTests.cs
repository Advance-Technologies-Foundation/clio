using System.Collections.Generic;
using System.Linq;
using System.Text;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Covers <c>PageInertOperationDetector</c>: which same-name operation pairs the differ provably
/// discards, and — just as load-bearing — which pairs it does NOT, so nobody "completes the table"
/// with a row that reading <c>JsonDiffApplier</c> disproves.
/// </summary>
/// <remarks>
/// Traited <c>Module = Command</c> because <c>clio/Command/PageInertOperationDetector.cs</c> maps
/// there. Note that the sibling merger's ~30 <c>PageBodyMerger_*</c> tests live in the
/// <c>Module = McpServer</c> fixture <c>PageToolsTests.cs</c>, so run BOTH module filters when
/// touching this area.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class PageInertOperationDetectorTests {

	private static string Body(PageSchemaType kind, string viewConfigDiffInner) =>
		kind == PageSchemaType.Mobile ? MobileBody(viewConfigDiffInner) : WebBody(viewConfigDiffInner);

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

	// ----- fragments: the seven shipped shapes ------------------------------------------------------

	private const string InsertAndMerge =
		"""
		[
			{ "operation": "insert", "name": "UsrName", "values": { "type": "crt.Input" } },
			{ "operation": "merge", "name": "UsrName", "values": { "label": "X" } }
		]
		""";

	private const string InsertAndMove =
		"""
		[
			{ "operation": "insert", "name": "UsrName", "values": { "type": "crt.Input" } },
			{ "operation": "move", "name": "UsrName", "parentName": "Other", "propertyName": "items", "index": 0 }
		]
		""";

	private const string InsertAndElementRemove =
		"""
		[
			{ "operation": "insert", "name": "UsrName", "values": { "type": "crt.Input" } },
			{ "operation": "remove", "name": "UsrName" }
		]
		""";

	private const string ElementRemoveAndMove =
		"""
		[
			{ "operation": "remove", "name": "UsrName" },
			{ "operation": "move", "name": "UsrName", "parentName": "Other", "propertyName": "items", "index": 0 }
		]
		""";

	private const string MergeAndElementRemove =
		"""
		[
			{ "operation": "merge", "name": "UsrName", "values": { "label": "X" } },
			{ "operation": "remove", "name": "UsrName" }
		]
		""";

	private const string MergeAndSet =
		"""
		[
			{ "operation": "merge", "name": "UsrName", "values": { "label": "X" } },
			{ "operation": "set", "name": "UsrName", "parentName": "Other", "values": { "type": "crt.Input" } }
		]
		""";

	private const string ElementRemoveAndPropertyRemove =
		"""
		[
			{ "operation": "remove", "name": "UsrName" },
			{ "operation": "remove", "name": "UsrName", "properties": ["layoutConfig"] }
		]
		""";

	// ----- fragments: shapes that must stay silent --------------------------------------------------

	private const string InsertAndPropertyRemove =
		"""
		[
			{ "operation": "insert", "name": "UsrName", "values": { "type": "crt.Input" } },
			{ "operation": "remove", "name": "UsrName", "properties": ["layoutConfig"] }
		]
		""";

	private const string InsertAndSet =
		"""
		[
			{ "operation": "insert", "name": "UsrName", "values": { "type": "crt.Input" } },
			{ "operation": "set", "name": "UsrName", "values": { "type": "crt.Button" } }
		]
		""";

	private const string MergeAndDisjointPropertyRemove =
		"""
		[
			{ "operation": "merge", "name": "UsrName", "values": { "label": "X" } },
			{ "operation": "remove", "name": "UsrName", "properties": ["tooltip"] }
		]
		""";

	private const string MisCasedMergeBesideInsert =
		"""
		[
			{ "operation": "insert", "name": "UsrName", "values": { "type": "crt.Input" } },
			{ "operation": "Merge", "name": "UsrName", "values": { "label": "X" } }
		]
		""";

	private const string InsertAndMergeOnDifferentNames =
		"""
		[
			{ "operation": "insert", "name": "UsrName", "values": { "type": "crt.Input" } },
			{ "operation": "merge", "name": "UsrPhone", "values": { "label": "X" } }
		]
		""";

	private const string TwoMergesOneName =
		"""
		[
			{ "operation": "merge", "name": "UsrName", "values": { "label": "X" } },
			{ "operation": "merge", "name": "UsrName", "values": { "tooltip": "Y" } }
		]
		""";

	private const string NumericAndStringName =
		"""
		[
			{ "operation": "insert", "name": 123, "values": { "type": "crt.Input" } },
			{ "operation": "merge", "name": "123", "values": { "label": "X" } }
		]
		""";

	private const string InsertMergeAndTwoMoreMergesOneName =
		"""
		[
			{ "operation": "insert", "name": "UsrName", "values": { "type": "crt.Input" } },
			{ "operation": "merge", "name": "UsrName", "values": { "label": "X" } },
			{ "operation": "merge", "name": "UsrName", "values": { "tooltip": "Y" } },
			{ "operation": "merge", "name": "UsrName", "values": { "visible": false } }
		]
		""";

	/// <summary>Builds a web body whose diff carries an insert+merge pair for each of many names.</summary>
	private static string ManyInertPairsBody(int nameCount) {
		var diff = new StringBuilder("[");
		for (int i = 0; i < nameCount; i++) {
			if (i > 0) {
				diff.Append(',');
			}
			diff.Append($$"""
				{ "operation": "insert", "name": "UsrField{{i}}", "values": { "type": "crt.Input" } },
				{ "operation": "merge", "name": "UsrField{{i}}", "values": { "label": "X" } }
				""");
		}
		diff.Append(']');
		return WebBody(diff.ToString());
	}

	// ----- positives: the four shapes GH-1240 names, web and mobile ---------------------------------

	[Test]
	[Description("Detect warns when a merge sits beside an insert for one name — the merge group runs first (web and mobile)")]
	public void Detect_ShouldWarn_WhenMergeSitsBesideInsertForSameName(
		[Values(PageSchemaType.Web, PageSchemaType.Mobile)] PageSchemaType kind) {
		// Arrange
		string body = Body(kind, InsertAndMerge);

		// Act
		IReadOnlyList<string> warnings = PageInertOperationDetector.Detect(body);

		// Assert
		warnings.Should().ContainSingle(w => w.Contains("UsrName") && w.Contains("'merge'"),
			$"because the differ applies the merge group before the insert group, so the merge resolves against a base without the component and is discarded ({kind})");
	}

	[Test]
	[Description("Detect warns when a move sits beside an insert for one name — the move resolves against the pristine base (web and mobile)")]
	public void Detect_ShouldWarn_WhenMoveSitsBesideInsertForSameName(
		[Values(PageSchemaType.Web, PageSchemaType.Mobile)] PageSchemaType kind) {
		// Arrange
		string body = Body(kind, InsertAndMove);

		// Act
		IReadOnlyList<string> warnings = PageInertOperationDetector.Detect(body);

		// Assert
		warnings.Should().ContainSingle(w => w.Contains("UsrName") && w.Contains("'move'"),
			$"because an unresolved move yields neither a remove nor a generated insert, so it vanishes entirely rather than being partially applied ({kind})");
	}

	[Test]
	[Description("Detect warns when an element remove sits beside an insert for one name — all removes run before all inserts (web and mobile)")]
	public void Detect_ShouldWarn_WhenElementRemoveSitsBesideInsertForSameName(
		[Values(PageSchemaType.Web, PageSchemaType.Mobile)] PageSchemaType kind) {
		// Arrange
		string body = Body(kind, InsertAndElementRemove);

		// Act
		IReadOnlyList<string> warnings = PageInertOperationDetector.Detect(body);

		// Assert
		warnings.Should().ContainSingle(w => w.Contains("UsrName") && w.Contains("element 'remove'"),
			$"because the position pipeline applies every remove before any insert, so a remove cannot delete what the same body inserts ({kind})");
		warnings.Single().Should().Contain("parent schema",
			because: "this is the one shipped row a correct body can trip — the replace-an-inherited-component idiom — so the message must name that case rather than asserting the author is wrong");
	}

	[Test]
	[Description("Detect warns when a move sits beside an element remove for one name — FilterMoveOperation drops it unconditionally (web and mobile)")]
	public void Detect_ShouldWarn_WhenMoveSitsBesideElementRemoveForSameName(
		[Values(PageSchemaType.Web, PageSchemaType.Mobile)] PageSchemaType kind) {
		// Arrange
		string body = Body(kind, ElementRemoveAndMove);

		// Act
		IReadOnlyList<string> warnings = PageInertOperationDetector.Detect(body);

		// Assert
		warnings.Should().ContainSingle(w => w.Contains("UsrName") && w.Contains("'move'"),
			$"because FilterMoveOperation drops every move whose name matches an element remove before anything is applied, whether or not the remove itself resolves ({kind})");
	}

	// ----- positives: the three cheap unconditional rows, web only ----------------------------------

	[Test]
	[Description("Detect warns when a merge sits beside an element remove for one name — the merge is patched then deleted")]
	public void Detect_ShouldWarn_WhenMergeSitsBesideElementRemoveForSameName() {
		// Arrange
		string body = WebBody(MergeAndElementRemove);

		// Act
		IReadOnlyList<string> warnings = PageInertOperationDetector.Detect(body);

		// Assert
		warnings.Should().ContainSingle(w => w.Contains("UsrName") && w.Contains("'merge'"),
			because: "merges are applied first and removes second, so the element the merge patched is deleted before runtime sees it");
	}

	[Test]
	[Description("Detect warns when a merge sits beside a set for one name — set runs last and replaces the element wholesale")]
	public void Detect_ShouldWarn_WhenMergeSitsBesideSetForSameName() {
		// Arrange
		string body = WebBody(MergeAndSet);

		// Act
		IReadOnlyList<string> warnings = PageInertOperationDetector.Detect(body);

		// Assert
		warnings.Should().ContainSingle(w => w.Contains("UsrName") && w.Contains("'set'"),
			because: "the set group is applied last and replaces the element with its own values, so the merged values never reach runtime");
	}

	[Test]
	[Description("Detect warns when a property remove sits beside an element remove for one name — the element is gone first")]
	public void Detect_ShouldWarn_WhenPropertyRemoveSitsBesideElementRemoveForSameName() {
		// Arrange
		string body = WebBody(ElementRemoveAndPropertyRemove);

		// Act
		IReadOnlyList<string> warnings = PageInertOperationDetector.Detect(body);

		// Assert
		warnings.Should().ContainSingle(w => w.Contains("UsrName") && w.Contains("property 'remove'"),
			because: "element removals are applied in the group before property removals, so the property removal targets an element that no longer exists");
	}

	// ----- the division of labour between the two detectors -----------------------------------------

	[Test]
	[Description("An insert kept beside a merge is silent for the downgrade detector and reported by the inert-operation detector")]
	public void Detect_ShouldWarn_WhereDowngradeDetectorStaysSilent_ForInsertPlusMerge() {
		// Arrange — one body carrying the shape GH-1240 was split out for.
		string prior = WebBody(
			"""
			[
				{ "operation": "insert", "name": "UsrName", "values": { "type": "crt.Input" } }
			]
			""");
		string final = WebBody(InsertAndMerge);

		// Act
		IReadOnlyList<string> downgradeWarnings = PageInsertDowngradeDetector.Detect(prior, final);
		IReadOnlyList<string> inertWarnings = PageInertOperationDetector.Detect(final);

		// Assert
		downgradeWarnings.Should().BeEmpty(
			because: "the insert survives, so nothing is orphaned — orphaning is the only thing PageInsertDowngradeDetector reports");
		inertWarnings.Should().ContainSingle(w => w.Contains("UsrName"),
			because: "the surviving merge is inert at apply time, and reporting that is exactly this detector's job — the gap GH-1240 filed");
	}

	// ----- negatives: rows that reading JsonDiffApplier disproves ------------------------------------

	[Test]
	[Description("Detect stays silent when a property remove sits beside an insert for one name — that pair actually works")]
	public void Detect_ShouldNotWarn_WhenPropertyRemoveSitsBesideInsertForSameName() {
		// Arrange
		string body = WebBody(InsertAndPropertyRemove);

		// Act
		IReadOnlyList<string> warnings = PageInertOperationDetector.Detect(body);

		// Assert
		warnings.Should().BeEmpty(
			because: "inserts are applied in the position pipeline and property removals in the group AFTER it, so the property removal successfully strips keys off the just-inserted element");
	}

	[Test]
	[Description("Detect stays silent when a set sits beside an insert for one name — the insert is not inert, only its values are overwritten")]
	public void Detect_ShouldNotWarn_WhenSetSitsBesideInsertForSameName() {
		// Arrange
		string body = WebBody(InsertAndSet);

		// Act
		IReadOnlyList<string> warnings = PageInertOperationDetector.Detect(body);

		// Assert
		warnings.Should().BeEmpty(
			because: "Set removes first and copies the removed item's index and propertyName back onto its own config before inserting, so the insert establishes the existence and position the set reuses — reporting it would be a style opinion dressed as a proof");
	}

	[Test]
	[Description("Detect stays silent when a property remove beside a merge names keys the merge does not write")]
	public void Detect_ShouldNotWarn_WhenPropertyRemoveSitsBesideMergeForDisjointKeys() {
		// Arrange
		string body = WebBody(MergeAndDisjointPropertyRemove);

		// Act
		IReadOnlyList<string> warnings = PageInertOperationDetector.Detect(body);

		// Assert
		warnings.Should().BeEmpty(
			because: "Remove's property branch deletes only the NAMED properties and Merge writes only the keys in its values, so only the intersection of the two key sets could be lost — this pair is fully effective");
	}

	[Test]
	[Description("Detect treats a mis-cased verb as dropped whole, not as a live merge beside the insert")]
	public void Detect_ShouldNotWarn_WhenMisCasedVerbSitsBesideInsert() {
		// Arrange
		string body = WebBody(MisCasedMergeBesideInsert);

		// Act
		IReadOnlyList<string> warnings = PageInertOperationDetector.Detect(body);

		// Assert
		warnings.Should().BeEmpty(
			because: "the differ switches on the raw verb with no default branch, so \"Merge\" lands in no group and is discarded whole — it can neither cancel nor be cancelled, and folding verb case here would report a pair that does not exist");
	}

	[Test]
	[Description("Detect stays silent when the two operations target different component names")]
	public void Detect_ShouldNotWarn_WhenOperationsTargetDifferentNames() {
		// Arrange
		string body = WebBody(InsertAndMergeOnDifferentNames);

		// Act
		IReadOnlyList<string> warnings = PageInertOperationDetector.Detect(body);

		// Assert
		warnings.Should().BeEmpty(
			because: "the differ groups operations per name, so operations on different components never interact");
	}

	[Test]
	[Description("Detect stays silent when two merges target one name — same-group operations compose in array order")]
	public void Detect_ShouldNotWarn_WhenTwoMergesTargetOneName() {
		// Arrange
		string body = WebBody(TwoMergesOneName);

		// Act
		IReadOnlyList<string> warnings = PageInertOperationDetector.Detect(body);

		// Assert
		warnings.Should().BeEmpty(
			because: "operations within one group ARE applied in array order, so two merges compose and the later one wins only on overlapping keys");
	}

	[Test]
	[Description("Detect stays silent when a numeric name and its string spelling appear, rather than conflating them")]
	public void Detect_ShouldNotWarn_WhenNameIsNumeric() {
		// Arrange
		string body = WebBody(NumericAndStringName);

		// Act
		IReadOnlyList<string> warnings = PageInertOperationDetector.Detect(body);

		// Assert
		warnings.Should().BeEmpty(
			because: "the name must be a JSON string, matching PageBodyMerger's identity — a bare ToString() would let {\"name\":123} and {\"name\":\"123\"} share a name and manufacture a pair");
	}

	[Test]
	[Description("Detect stays silent for an empty viewConfigDiff")]
	public void Detect_ShouldNotWarn_WhenViewConfigDiffIsEmpty(
		[Values(PageSchemaType.Web, PageSchemaType.Mobile)] PageSchemaType kind) {
		// Arrange
		string body = Body(kind, "[]");

		// Act
		IReadOnlyList<string> warnings = PageInertOperationDetector.Detect(body);

		// Assert
		warnings.Should().BeEmpty(because: $"there are no operations at all, so no pair can exist ({kind})");
	}

	[Test]
	[Description("Detect fails open for a null or blank body rather than throwing")]
	public void Detect_ShouldNotWarn_WhenBodyIsNullOrBlank([Values(null, "", "   ")] string body) {
		// Act
		IReadOnlyList<string> warnings = PageInertOperationDetector.Detect(body);

		// Assert
		warnings.Should().BeEmpty(
			because: "this is an advisory check; a missing body must never make it throw into the save path");
	}

	[Test]
	[Description("Detect fails open when the web body's viewConfigDiff section is not valid JSON")]
	public void Detect_ShouldNotWarn_WhenWebBodyIsUnparseable() {
		// Arrange
		string body = WebBody("[ {\"operation\": ");

		// Act
		IReadOnlyList<string> warnings = PageInertOperationDetector.Detect(body);

		// Assert
		warnings.Should().BeEmpty(
			because: "an unparseable body must skip the heuristic rather than guess — a parse hiccup must not affect an otherwise-valid save");
	}

	[Test]
	[Description("Detect fails open when the mobile body is not valid JSON")]
	public void Detect_ShouldNotWarn_WhenMobileBodyIsUnparseable() {
		// Arrange
		string body = "{ \"viewConfigDiff\": [ {\"operation\": ";

		// Act
		IReadOnlyList<string> warnings = PageInertOperationDetector.Detect(body);

		// Assert
		warnings.Should().BeEmpty(
			because: "the mobile path parses the whole body as JSON, so it must fail open on the same terms as the web path");
	}

	// ----- volume: dedupe per (name, shape) and the global cap ---------------------------------------

	[Test]
	[Description("Detect reports one finding per name and shape even when the discarded operation repeats")]
	public void Detect_ShouldReportOneFindingPerNameAndShape_WhenThePairRepeats() {
		// Arrange — one insert and three merges on the same name.
		string body = WebBody(InsertMergeAndTwoMoreMergesOneName);

		// Act
		IReadOnlyList<string> warnings = PageInertOperationDetector.Detect(body);

		// Assert
		warnings.Should().HaveCount(1,
			because: "presence is tested per apply GROUP, so a name carrying several merges beside one insert is one finding — repeating the message per merge would say nothing new");
	}

	[Test]
	[Description("Detect caps its findings and names how many it did not list, rather than truncating silently")]
	public void Detect_ShouldCapFindings_WhenManyNamesCarryInertPairs() {
		// Arrange — 30 distinct names, each with an insert+merge pair.
		string body = ManyInertPairsBody(30);

		// Act
		IReadOnlyList<string> warnings = PageInertOperationDetector.Detect(body);

		// Assert
		warnings.Should().HaveCount(13,
			because: "the cap is 12 findings plus exactly one summary line, so a pathological body cannot bury the response");
		warnings.Last().Should().Contain("18 further inert-operation finding(s)",
			because: "silent truncation would read as \"that is all of them\"; the count of what was dropped has to be stated");
	}
}
