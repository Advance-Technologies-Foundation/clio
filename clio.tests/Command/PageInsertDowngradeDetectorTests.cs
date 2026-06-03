using System.Collections.Generic;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class PageInsertDowngradeDetectorTests {

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

	private const string InsertName =
		"""
		[
			{
				"operation": "insert",
				"name": "UsrName",
				"values": { "type": "crt.Input" }
			}
		]
		""";

	private const string MergeName =
		"""
		[
			{
				"operation": "merge",
				"name": "UsrName",
				"values": { "label": "X" }
			}
		]
		""";

	private const string MoveName =
		"""
		[
			{
				"operation": "move",
				"name": "UsrName",
				"parentName": "Other",
				"index": 0
			}
		]
		""";

	private const string RemoveName =
		"""
		[
			{
				"operation": "remove",
				"name": "UsrName"
			}
		]
		""";

	private const string InsertUpdatedName =
		"""
		[
			{
				"operation": "insert",
				"name": "UsrName",
				"values": { "type": "crt.Input", "required": true }
			}
		]
		""";

	private const string InsertAndMergeName =
		"""
		[
			{
				"operation": "insert",
				"name": "UsrName",
				"values": { "type": "crt.Input" }
			},
			{
				"operation": "merge",
				"name": "UsrName",
				"values": { "label": "X" }
			}
		]
		""";

	private const string TwoInsertsNamePriorBody =
		"""
		[
			{
				"operation": "insert",
				"name": "UsrName",
				"values": { "type": "crt.Input" }
			},
			{
				"operation": "insert",
				"name": "UsrPhone",
				"values": { "type": "crt.Input" }
			}
		]
		""";

	private const string OneMergedOneInsertedFinalBody =
		"""
		[
			{
				"operation": "merge",
				"name": "UsrName",
				"values": { "label": "X" }
			},
			{
				"operation": "insert",
				"name": "UsrPhone",
				"values": { "type": "crt.Input" }
			}
		]
		""";

	[Test]
	[Description("Detect warns when an own-body insert is downgraded to a merge (web and mobile)")]
	public void Detect_ShouldWarn_WhenInsertDowngradedToMerge([Values(PageSchemaType.Web, PageSchemaType.Mobile)] PageSchemaType kind) {
		// Arrange
		string prior = Body(kind, InsertName);
		string final = Body(kind, MergeName);

		// Act
		IReadOnlyList<string> warnings = PageInsertDowngradeDetector.Detect(prior, final);

		// Assert
		warnings.Should().ContainSingle(w => w.Contains("UsrName") && w.Contains("merge"),
			$"because demoting an own-body insert to a merge orphans the component ({kind})");
	}

	[Test]
	[Description("Detect warns when an own-body insert is downgraded to a move (web and mobile)")]
	public void Detect_ShouldWarn_WhenInsertDowngradedToMove([Values(PageSchemaType.Web, PageSchemaType.Mobile)] PageSchemaType kind) {
		// Arrange — move relocates an existing element, so dropping the insert orphans it like merge does.
		string prior = Body(kind, InsertName);
		string final = Body(kind, MoveName);

		// Act
		IReadOnlyList<string> warnings = PageInsertDowngradeDetector.Detect(prior, final);

		// Assert
		warnings.Should().ContainSingle(w => w.Contains("UsrName") && w.Contains("move"),
			$"because a move has no element to relocate once the prior insert is gone ({kind})");
	}

	[Test]
	[Description("Detect warns when an own-body insert is replaced by a remove (web and mobile)")]
	public void Detect_ShouldWarn_WhenInsertReplacedByRemove([Values(PageSchemaType.Web, PageSchemaType.Mobile)] PageSchemaType kind) {
		// Arrange
		string prior = Body(kind, InsertName);
		string final = Body(kind, RemoveName);

		// Act
		IReadOnlyList<string> warnings = PageInsertDowngradeDetector.Detect(prior, final);

		// Assert
		warnings.Should().ContainSingle(
			w => w.Contains("UsrName") && w.Contains("remove") && w.Contains("dangling") && w.Contains("omit its insert"),
			$"because the remove downgrade must produce the distinct dangling-remove guidance (not the orphan-merge wording) ({kind})");
	}

	[Test]
	[Description("Detect does not warn when the final body keeps the insert alongside a merge (web and mobile)")]
	public void Detect_ShouldNotWarn_WhenInsertIsKeptWithMerge([Values(PageSchemaType.Web, PageSchemaType.Mobile)] PageSchemaType kind) {
		// Arrange — both ops present in order compose fine at runtime.
		string prior = Body(kind, InsertName);
		string final = Body(kind, InsertAndMergeName);

		// Act
		IReadOnlyList<string> warnings = PageInsertDowngradeDetector.Detect(prior, final);

		// Assert
		warnings.Should().BeEmpty(
			$"because an insert that remains present is not orphaned even when a sibling merge exists ({kind})");
	}

	[Test]
	[Description("Detect does not warn when an insert is updated by re-sending an insert (web and mobile)")]
	public void Detect_ShouldNotWarn_WhenInsertIsUpdatedWithInsert([Values(PageSchemaType.Web, PageSchemaType.Mobile)] PageSchemaType kind) {
		// Arrange — the correct way to change a self-inserted component.
		string prior = Body(kind, InsertName);
		string final = Body(kind, InsertUpdatedName);

		// Act
		IReadOnlyList<string> warnings = PageInsertDowngradeDetector.Detect(prior, final);

		// Assert
		warnings.Should().BeEmpty(
			$"because editing the insert in place is the supported way to modify a self-inserted component ({kind})");
	}

	[Test]
	[Description("Detect does not warn for a merge against a name the prior own body did not insert (web and mobile)")]
	public void Detect_ShouldNotWarn_WhenMergeTargetsInheritedComponent([Values(PageSchemaType.Web, PageSchemaType.Mobile)] PageSchemaType kind) {
		// Arrange — prior own body has no insert for UsrName (it comes from a parent schema).
		string prior = Body(kind, "[]");
		string final = Body(kind, MergeName);

		// Act
		IReadOnlyList<string> warnings = PageInsertDowngradeDetector.Detect(prior, final);

		// Assert
		warnings.Should().BeEmpty(
			$"because a merge onto a component introduced by a parent schema is a legitimate operation ({kind})");
	}

	[Test]
	[Description("Detect warns only for the downgraded component when several components are present (web and mobile)")]
	public void Detect_ShouldWarnOnlyForDowngradedName_WhenMultipleComponentsExist([Values(PageSchemaType.Web, PageSchemaType.Mobile)] PageSchemaType kind) {
		// Arrange
		string prior = Body(kind, TwoInsertsNamePriorBody);
		string final = Body(kind, OneMergedOneInsertedFinalBody);

		// Act
		IReadOnlyList<string> warnings = PageInsertDowngradeDetector.Detect(prior, final);

		// Assert
		warnings.Should().ContainSingle(w => w.Contains("UsrName"),
			$"because only the component whose insert became a merge is at risk of being orphaned ({kind})");
	}

	[Test]
	[Description("Detect returns no warnings when the prior body is empty / new replacing schema (web and mobile)")]
	public void Detect_ShouldNotWarn_WhenPriorBodyIsEmpty([Values(PageSchemaType.Web, PageSchemaType.Mobile)] PageSchemaType kind) {
		// Arrange — a brand-new replacing schema has no own-body inserts to downgrade.
		string final = Body(kind, MergeName);

		// Act
		IReadOnlyList<string> warnings = PageInsertDowngradeDetector.Detect(null, final);

		// Assert
		warnings.Should().BeEmpty(
			$"because with no prior own body there is no insert that could be downgraded ({kind})");
	}

	[Test]
	[Description("Detect warns on an append-merged web body where dedupe left only a merge for an inserted name")]
	public void Detect_ShouldWarn_WhenAppendMergeResultDropsInsert() {
		// Arrange — the append merge result keeps only the incoming merge (insert deduped away by name).
		string prior = WebBody(InsertName);
		string mergedFinal = WebBody(MergeName);

		// Act
		IReadOnlyList<string> warnings = PageInsertDowngradeDetector.Detect(prior, mergedFinal);

		// Assert
		warnings.Should().HaveCount(1,
			"because append dedupe by name drops the prior insert, leaving an orphaned merge");
	}

	[Test]
	[Description("Detect returns no warnings when a web body cannot be parsed (fails open)")]
	public void Detect_ShouldNotWarn_WhenWebBodyIsUnparseable() {
		// Arrange — a web body whose viewConfigDiff section is not valid JSON must not affect the save.
		string prior = WebBody(InsertName);
		string final = WebBody("[ this is not json");

		// Act
		IReadOnlyList<string> warnings = PageInsertDowngradeDetector.Detect(prior, final);

		// Assert
		warnings.Should().BeEmpty(
			"because an unparseable body skips the heuristic rather than guessing");
	}

	[Test]
	[Description("Detect returns no warnings when a mobile body cannot be parsed (fails open)")]
	public void Detect_ShouldNotWarn_WhenMobileBodyIsUnparseable() {
		// Arrange — malformed mobile JSON must not affect the save.
		string prior = MobileBody(InsertName);
		string final = """{ "viewConfigDiff": [ this is not json """;

		// Act
		IReadOnlyList<string> warnings = PageInsertDowngradeDetector.Detect(prior, final);

		// Assert
		warnings.Should().BeEmpty(
			"because an unparseable body skips the heuristic rather than guessing");
	}
}
