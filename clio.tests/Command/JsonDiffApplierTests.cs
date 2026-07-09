using System;
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

	private static JObject LoadFixtureCase(string group, int index) {
		string path = Path.Combine(AppContext.BaseDirectory, "Command/McpServer/Fixtures/JsonDiffApplierMock.json");
		var fixture = JObject.Parse(File.ReadAllText(path));
		return (JObject)((JArray)fixture[group])[index];
	}
}
