using System;
using System.Collections.Generic;
using System.IO;
using Clio.Command;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Hand-port of the non-mock cases in the client <c>json-applier.service.spec.ts</c> (aliases, move+remove,
/// index-less insert/move, position swaps, and the DisableApplyMoveIfIndirectParentMoved feature flag).
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class JsonDiffApplierTests {

	private static JArray Arr(string json) => (JArray)JToken.Parse(json);

	private static void AssertEqual(JToken actual, JToken expected) =>
		JToken.DeepEquals(actual, expected).Should().BeTrue(
			because: $"expected: {expected}\nactual:   {actual}");

	// ----- aliases -----

	[Test]
	[Description("merge with alias name resolves to the aliased element's real name.")]
	public void Apply_Alias_MergeByAliasName_Works() {
		var applier = new JsonDiffApplier();
		JToken source = applier.Apply(new JArray(), Arr("""
			[ { "operation": "insert", "name": "NewName", "alias": { "name": "Name", "excludeProperties": ["layout"] } } ]
			"""));

		JToken result = applier.Apply(source, Arr("""
			[ { "operation": "merge", "name": "Name", "values": { "test": true } } ]
			"""));

		AssertEqual(result, Arr("""[ { "name": "NewName", "test": true } ]"""));
	}

	[Test]
	[Description("alias excludeProperties drops the excluded property from a merge.")]
	public void Apply_Alias_ExcludeProperties() {
		var applier = new JsonDiffApplier();
		JToken source = applier.Apply(new JArray(), Arr("""
			[ { "operation": "insert", "name": "NewName", "alias": { "name": "Name", "excludeProperties": ["layout"] } } ]
			"""));

		JToken result = applier.Apply(source, Arr("""
			[ { "operation": "merge", "name": "Name", "values": { "layout": false, "second": true } } ]
			"""));

		AssertEqual(result, Arr("""[ { "name": "NewName", "second": true } ]"""));
	}

	[Test]
	[Description("A merge whose values is a non-object (array/scalar) is a tolerant no-op — the target element is left unchanged instead of throwing an InvalidCastException.")]
	public void Apply_Merge_NonObjectValues_IsNoOp() {
		var applier = new JsonDiffApplier();
		JToken source = applier.Apply(new JArray(), Arr("""
			[ { "operation": "insert", "name": "Name", "values": { "keep": true } } ]
			"""));

		// Pre-fix this threw InvalidCastException from casting a JArray to JObject; now it is a tolerant no-op.
		JToken result = applier.Apply(source, Arr("""
			[ { "operation": "merge", "name": "Name", "values": [ 1, 2 ] } ]
			"""));

		AssertEqual(result, Arr("""[ { "name": "Name", "keep": true } ]"""));
	}

	[Test]
	[Description("alias excludeOperations makes move / remove / merge no-ops, but remove-properties still applies.")]
	public void Apply_Alias_ExcludeOperations() {
		const string insert = """
			[ { "operation": "insert", "name": "NewName",
				"alias": { "name": "Name", "excludeOperations": ["merge", "move", "remove"] },
				"values": { "property": "value" } } ]
			""";
		const string unchanged = """
			[ { "name": "ParentName", "items": [] }, { "name": "NewName", "property": "value" } ]
			""";

		// move excluded
		var a1 = new JsonDiffApplier();
		JToken s1 = a1.Apply(Arr("""[ { "name": "ParentName", "items": [] } ]"""), Arr(insert));
		AssertEqual(a1.Apply(s1, Arr("""[ { "operation": "move", "name": "Name", "parentName": "ParentName", "propertyName": "items" } ]""")), Arr(unchanged));

		// remove excluded
		var a2 = new JsonDiffApplier();
		JToken s2 = a2.Apply(Arr("""[ { "name": "ParentName", "items": [] } ]"""), Arr(insert));
		AssertEqual(a2.Apply(s2, Arr("""[ { "operation": "remove", "name": "Name" } ]""")), Arr(unchanged));

		// merge excluded
		var a3 = new JsonDiffApplier();
		JToken s3 = a3.Apply(Arr("""[ { "name": "ParentName", "items": [] } ]"""), Arr(insert));
		AssertEqual(a3.Apply(s3, Arr("""[ { "operation": "merge", "name": "Name", "values": { "property": "value" } } ]""")), Arr(unchanged));

		// remove-properties is NOT excluded — property removed
		var a4 = new JsonDiffApplier();
		JToken s4 = a4.Apply(Arr("""[ { "name": "ParentName", "items": [] } ]"""), Arr(insert));
		AssertEqual(
			a4.Apply(s4, Arr("""[ { "operation": "remove", "name": "Name", "properties": ["property"] } ]""")),
			Arr("""[ { "name": "ParentName", "items": [] }, { "name": "NewName" } ]"""));
	}

	// ----- move + remove / index-less -----

	[Test]
	[Description("move combined with remove of the same name: removed names win, TestName0/1 dropped.")]
	public void Apply_MoveUsedWithRemove_RemovesNames() {
		JToken result = new JsonDiffApplier().Apply(
			Arr("""[ { "name": "TestName0" }, { "name": "TestName1" }, { "name": "TestName2" } ]"""),
			Arr("""
				[ { "operation": "remove", "name": "TestName0" },
				  { "operation": "remove", "name": "TestName1" },
				  { "operation": "move", "name": "TestName0" },
				  { "operation": "move", "name": "TestName2" } ]
				"""));
		AssertEqual(result, Arr("""[ { "name": "TestName2" } ]"""));
	}

	[Test]
	[Description("insert and move operations without indexes append in operation order.")]
	public void Apply_InsertAndMoveWithoutIndexes() {
		JToken result = new JsonDiffApplier().Apply(
			Arr("""[ { "name": "a1" }, { "name": "a2" } ]"""),
			Arr("""
				[ { "operation": "insert", "name": "a3" },
				  { "operation": "insert", "name": "a4" },
				  { "operation": "move", "name": "a2" } ]
				"""));
		AssertEqual(result, Arr("""[ { "name": "a1" }, { "name": "a3" }, { "name": "a4" }, { "name": "a2" } ]"""));
	}

	[Test]
	[Description("elements inside items can switch positions via two move operations.")]
	public void Apply_ItemsSwitchPositions() {
		JToken result = new JsonDiffApplier().Apply(
			Arr("""
				[ { "name": "root", "items": [
					{ "name": "tab", "items": [ { "name": "firstCG" }, { "name": "secondCG" }, { "name": "thirdCG" } ] } ] } ]
				"""),
			Arr("""
				[ { "operation": "move", "name": "secondCG", "parentName": "tab", "propertyName": "items", "index": 1 },
				  { "operation": "move", "name": "thirdCG", "parentName": "tab", "propertyName": "items", "index": 0 } ]
				"""));
		AssertEqual(result, Arr("""
			[ { "name": "root", "items": [
				{ "name": "tab", "items": [ { "name": "thirdCG" }, { "name": "secondCG" }, { "name": "firstCG" } ] } ] } ]
			"""));
	}

	// ----- feature flag -----

	[Test]
	[Description("When DisableApplyMoveIfIndirectParentMoved is true, applying Move_v2[0] with applyMoveIfIndirectParentMoved does NOT produce the expected (full-path) result.")]
	public void Apply_DisableApplyMoveIfIndirectParentMoved_DoesNotApplyIndirect() {
		JObject moveV2 = LoadFixtureCase("Move_v2", 0);
		var source = (JArray)moveV2["sourceObject"];
		var diff = (JArray)moveV2["diff"];
		JToken expected = moveV2["expectedResultObject"];

		JToken result = new JsonDiffApplier(disableApplyMoveIfIndirectParentMoved: true)
			.Apply(source, diff, new JsonApplierOperationsOptions { ApplyMoveIfIndirectParentMoved = true });

		JToken.DeepEquals(result, expected).Should().BeFalse(
			because: "the feature flag forces relative-path ordering, so the indirect-parent move is not applied");
	}

	// ----- cycle guards (do not StackOverflow the MCP server process) -----

	[Test]
	[Description("A 2-cycle insert diff (A parented to B, B parented to A) surfaces the catchable LoopDependency error instead of recursing forever in path ordering (StackOverflow).")]
	public void Apply_CyclicInsertParentChain_ThrowsLoopDependency() {
		var applier = new JsonDiffApplier();
		Action act = () => applier.Apply(new JArray(), Arr("""
			[ { "operation": "insert", "name": "A", "parentName": "B", "propertyName": "items", "values": { "type": "x" } },
			  { "operation": "insert", "name": "B", "parentName": "A", "propertyName": "items", "values": { "type": "y" } } ]
			"""));

		act.Should().Throw<JsonDiffApplierException>().WithMessage("*Cyclic dependency*");
	}

	[Test]
	[Description("Moving an element into its own descendant terminates (the insert-retry pipeline stops when its unsuccessful set stops shrinking) instead of looping forever and crashing the process.")]
	public void Apply_MoveIntoOwnDescendant_Terminates() {
		var applier = new JsonDiffApplier();
		JToken source = applier.Apply(new JArray(), Arr("""
			[ { "operation": "insert", "name": "A", "propertyName": "items", "values": { "type": "x", "items": [] } },
			  { "operation": "insert", "name": "B", "parentName": "A", "propertyName": "items", "values": { "type": "y", "items": [] } } ]
			"""));

		Action act = () => applier.Apply(source, Arr("""
			[ { "operation": "move", "name": "A", "parentName": "B", "propertyName": "items" } ]
			"""));

		act.Should().NotThrow();
	}

	[Test]
	[Description("A view-config insert carrying a path but no parentName (the retired tolerant applier's shape) is NOT routed by path in the client-faithful applier: path is ignored, the element lands at the root, and nothing throws — pinning the strict behavior the ported-tests comment describes.")]
	public void Apply_ViewConfigInsertWithPathNoParentName_IgnoresPathAndInsertsAtRoot() {
		var applier = new JsonDiffApplier();
		JToken source = applier.Apply(new JArray(), Arr("""
			[ { "operation": "insert", "name": "MainContainer", "values": { "type": "crt.FlexContainer", "items": [] } } ]
			"""));

		JToken result = applier.Apply(source, Arr("""
			[ { "operation": "insert", "name": "Orphan", "path": ["MainContainer", "items"], "values": { "type": "crt.Input" } } ]
			"""));

		(result as JArray).Should().HaveCount(2,
			because: "the base view-config applier targets by parentName+propertyName only, so a path-only op is applied at the root rather than routed by path");
		result[1]!["name"]!.ToString().Should().Be("Orphan",
			because: "with no parentName the element lands at the root level, its path array ignored");
		(result[0]!["items"] as JArray).Should().BeEmpty(
			because: "the path did not route the element into MainContainer.items");
	}

	private static JObject LoadFixtureCase(string group, int index) {
		string path = Path.Combine(AppContext.BaseDirectory, "Command/McpServer/Fixtures/JsonDiffApplierMock.json");
		var fixture = JObject.Parse(File.ReadAllText(path));
		return (JObject)((JArray)fixture[group])[index];
	}

	// ----- single-object slot inserts (mobile Scaffold.floatAction) -----

	[Test]
	[Description("Reproduces the mobile template chain that seeds Scaffold.floatAction to {} then inserts FloatingActionButton into that single-object slot: the client-faithful applier sets the slot to the component (no floatAction.floatAction nesting), the follow-up menuItems insert appends, and the leaf merge applies onto the resolved FAB")]
	public void ApplyDiff_WhenInsertTargetsSeededObjectSlot_ResolvesFabWithoutNesting() {
		// Arrange - trimmed BaseMobileTemplate -> BaseMobilePageTemplate -> MobilePageWithTabsFreedomTemplate chain
		var applier = new JsonDiffApplier();
		JArray baseTemplate = Arr("""
			[ { "operation": "insert", "name": "Scaffold", "values": { "type": "crt.Scaffold", "items": [] } } ]
			""");
		JArray basePageTemplate = Arr("""
			[
				{ "operation": "merge",  "name": "Scaffold", "values": { "leadingWidth": 100, "floatAction": {} } },
				{ "operation": "insert", "name": "FloatingActionButton", "parentName": "Scaffold", "propertyName": "floatAction", "index": 3,
					"values": { "type": "crt.FloatingActionButton", "icon": "more-vertical-button-icon", "menuItems": [] } },
				{ "operation": "insert", "name": "FloatingActionButtonCopyMenuItem", "parentName": "FloatingActionButton", "propertyName": "menuItems", "index": 0,
					"values": { "type": "crt.MenuItem", "caption": "#ResourceString(CopyMenuItem_caption)#" } }
			]
			""");
		JArray leafTemplate = Arr("""
			[ { "operation": "merge", "name": "FloatingActionButton", "values": { "visible": "$PrimaryModelMode | crt.IsEqual : 'update'" } } ]
			""");

		// Act
		JToken result = applier.ApplyDiff(new JArray(), [baseTemplate, basePageTemplate, leafTemplate]);

		// Assert
		JArray resultArray = result as JArray;
		resultArray.Should().ContainSingle(because: "only the Scaffold root should remain at the top level");
		JObject scaffold = resultArray![0] as JObject;
		JObject floatAction = scaffold!["floatAction"] as JObject;
		floatAction.Should().NotBeNull(
			because: "the single-object floatAction slot must hold one object, not be lost or wrapped");
		floatAction!["name"]!.ToString().Should().Be("FloatingActionButton",
			because: "insert into a seeded object slot must set the slot to the component");
		floatAction["type"]!.ToString().Should().Be("crt.FloatingActionButton",
			because: "the inserted component payload must be preserved on the slot");
		floatAction["floatAction"].Should().BeNull(
			because: "the client-faithful applier must not re-nest the component under the same property name");
		floatAction["visible"]!.ToString().Should().Be("$PrimaryModelMode | crt.IsEqual : 'update'",
			because: "the leaf merge must resolve the correctly-placed FAB and apply onto it");
		(floatAction["menuItems"] as JArray).Should().ContainSingle(
			because: "a follow-up insert must resolve the FAB and append into its menuItems array slot");
		floatAction["menuItems"]![0]!["name"]!.ToString().Should().Be("FloatingActionButtonCopyMenuItem",
			because: "the array-slot child must be appended, not replace the array");
	}

	// ----- resolve-behavior coverage ported from the retired PageJsonDiffApplierTests (real parentName+propertyName
	//       form; the retired tests' `path`-targeted view-config ops are not a real Creatio shape — the strict
	//       applier ignores the path and appends at the root, pinned by
	//       Apply_ViewConfigInsertWithPathNoParentName_IgnoresPathAndInsertsAtRoot above) -----

	private static JArray Diff(string json) => Arr(json);

	private static readonly IReadOnlyList<JsonApplierOperationsOptions> IndirectMove =
		[new JsonApplierOperationsOptions { ApplyMoveIfIndirectParentMoved = true }];

	[Test]
	[Description("All operation families in one view-config diff (merge, set, move, remove-properties, remove, insert) using parentName+propertyName targeting")]
	public void ApplyDiff_WhenOperationsUseAllSupportedFamilies_ProducesExpectedViewConfig() {
		// Arrange
		var applier = new JsonDiffApplier();
		JArray source = Arr("""
			[ { "name": "Container", "caption": "Base", "items": [
				{ "name": "Field1", "label": "Original" },
				{ "name": "Field2", "label": "Second" } ] } ]
			""");
		JArray operations = Diff("""
			[
				{ "operation": "merge", "name": "Container", "values": { "caption": "Merged" } },
				{ "operation": "set", "name": "Field2", "parentName": "Container", "propertyName": "items", "values": { "label": "Updated" } },
				{ "operation": "move", "name": "Field2", "parentName": "Container", "propertyName": "items", "index": 0 },
				{ "operation": "remove", "name": "Container", "properties": ["caption"] },
				{ "operation": "remove", "name": "Field1" },
				{ "operation": "insert", "name": "Field3", "parentName": "Container", "propertyName": "items", "index": 1, "values": { "label": "Inserted" } }
			]
			""");

		// Act
		JArray result = applier.ApplyDiff(source, [operations], IndirectMove) as JArray;

		// Assert
		result.Should().ContainSingle(because: "the diff should keep the root container while mutating its children");
		result![0]!["caption"].Should().BeNull(because: "remove-properties should delete the requested object property");
		(result[0]!["items"] as JArray).Should().HaveCount(2, because: "one item removed, one inserted");
		result[0]!["items"]![0]!["name"]!.ToString().Should().Be("Field2", because: "move should place the item at the target index");
		result[0]!["items"]![0]!["label"]!.ToString().Should().Be("Updated", because: "set should update nested properties after positional changes");
		result[0]!["items"]![1]!["name"]!.ToString().Should().Be("Field3", because: "insert should add the new named item into the collection");
		result[0]!["items"]![1]!["label"]!.ToString().Should().Be("Inserted", because: "insert should preserve the provided payload");
	}

	[Test]
	[Description("Insert that creates a new parent, moves existing elements into it, and nested inserts target the moved elements (BaseFreedomTemplate-style relocation)")]
	public void ApplyDiff_WhenInsertCreatesParentAndMovesRelocate_NestedChildrenEndUpInsideMovedElements() {
		// Arrange
		var applier = new JsonDiffApplier();
		JArray source = Arr("""
			[ { "name": "MainHeader", "items": [ { "name": "ActionContainer", "items": [] } ] },
			  { "name": "MainContainer", "items": [] } ]
			""");
		JArray operations = Diff("""
			[
				{ "operation": "remove", "name": "ActionContainer" },
				{ "operation": "move", "name": "MainHeader", "parentName": "Main", "propertyName": "items", "index": 0 },
				{ "operation": "move", "name": "MainContainer", "parentName": "Main", "propertyName": "items", "index": 1 },
				{ "operation": "insert", "name": "Main", "index": 0, "values": { "type": "crt.FlexContainer", "items": [] } },
				{ "operation": "insert", "name": "CardContentWrapper", "parentName": "MainContainer", "propertyName": "items", "index": 0, "values": { "type": "crt.GridContainer", "items": [] } }
			]
			""");

		// Act
		JArray result = applier.ApplyDiff(source, [operations], IndirectMove) as JArray;

		// Assert
		result.Should().ContainSingle(because: "only Main should remain at the root after the relocations");
		result![0]!["name"]!.ToString().Should().Be("Main");
		JArray mainItems = result[0]!["items"] as JArray;
		mainItems.Should().HaveCount(2, because: "MainHeader and MainContainer must be moved into the created Main");
		mainItems![0]!["name"]!.ToString().Should().Be("MainHeader");
		mainItems[1]!["name"]!.ToString().Should().Be("MainContainer");
		(mainItems[1]!["items"] as JArray).Should().ContainSingle(because: "CardContentWrapper must land inside the moved MainContainer");
		mainItems[1]!["items"]![0]!["name"]!.ToString().Should().Be("CardContentWrapper");
	}

	[Test]
	[Description("An insert whose parent is created by a later insert in the same diff is retried until the parent exists")]
	public void ApplyDiff_WhenInsertTargetsParentCreatedLater_IsRetriedAfterParentExists() {
		// Arrange
		var applier = new JsonDiffApplier();
		JArray operations = Diff("""
			[
				{ "operation": "insert", "name": "Child", "parentName": "Parent", "propertyName": "items", "index": 0, "values": { "label": "ChildLabel" } },
				{ "operation": "insert", "name": "Parent", "index": 0, "values": { "type": "crt.Container", "items": [] } }
			]
			""");

		// Act
		JArray result = applier.ApplyDiff(new JArray(), [operations], IndirectMove) as JArray;

		// Assert
		result.Should().ContainSingle();
		result![0]!["name"]!.ToString().Should().Be("Parent");
		(result[0]!["items"] as JArray).Should().ContainSingle(because: "Child must be retried once Parent exists");
		result[0]!["items"]![0]!["name"]!.ToString().Should().Be("Child");
	}

	[Test]
	[Description("Nested inserts supplied out of order resolve by path length ascending so each parent exists before its child")]
	public void ApplyDiff_WhenMultipleNestedInsertsExist_OrdersByPathLengthAscending() {
		// Arrange
		var applier = new JsonDiffApplier();
		JArray operations = Diff("""
			[
				{ "operation": "insert", "name": "Leaf", "parentName": "Mid", "propertyName": "items", "index": 0, "values": {} },
				{ "operation": "insert", "name": "Mid", "parentName": "Root", "propertyName": "items", "index": 0, "values": { "items": [] } },
				{ "operation": "insert", "name": "Root", "index": 0, "values": { "items": [] } }
			]
			""");

		// Act
		JArray result = applier.ApplyDiff(new JArray(), [operations], IndirectMove) as JArray;

		// Assert
		result.Should().ContainSingle();
		result![0]!["name"]!.ToString().Should().Be("Root");
		JArray rootItems = result[0]!["items"] as JArray;
		rootItems.Should().ContainSingle();
		rootItems![0]!["name"]!.ToString().Should().Be("Mid");
		(rootItems[0]!["items"] as JArray)![0]!["name"]!.ToString().Should().Be("Leaf");
	}

	[Test]
	[Description("A null-name item reached mid-iteration (after a valid first sibling) is skipped by the lookup cache rather than cached under a null key, so a named item after it still resolves and merges — exercises the FindItemInfo null-name guard")]
	public void ApplyDiff_WhenNullNameItemAppearsMidIteration_IsNotCachedAndNamedSiblingStillResolves() {
		// Arrange - the first sibling is a valid item-config (so iteration proceeds); the null-name middle sibling
		// must be walked past without a cache write, letting the trailing named sibling resolve.
		var applier = new JsonDiffApplier();
		JArray source = Arr("""
			[
				{ "name": "First", "items": [] },
				{ "name": null, "caption": "Ignored" },
				{ "name": "Container", "caption": "Base" }
			]
			""");
		JArray operations = Diff("""
			[ { "operation": "merge", "name": "Container", "values": { "caption": "Updated" } } ]
			""");

		// Act
		Action act = () => applier.ApplyDiff(source, [operations]);

		// Assert
		act.Should().NotThrow(
			because: "a null-name item reached during lookup must be skipped, not cached under a null key or throw");
		JArray result = applier.ApplyDiff(source, [operations]) as JArray;
		result![2]!["caption"]!.ToString().Should().Be("Updated",
			because: "a named item after a null-name sibling must still be found and merged");
	}
}
