using Clio.Command;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Tests for <see cref="JsonPathDiffApplier"/> — the C# clone of the client <c>JsonPathApplierService</c>
/// used for viewModelConfigDiff / modelConfigDiff. No client spec/mock exists for it, so these cover its
/// distinguishing behaviors: <c>_id</c> identity, path resolution, deep-merge with array replacement, root
/// merge, insert into a path target, and removals.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class JsonPathDiffApplierTests {

	private static JToken Apply(string source, string operations) =>
		new JsonPathDiffApplier().Apply(JToken.Parse(source), (JArray)JToken.Parse(operations));

	private static void AssertEqual(JToken actual, string expected) =>
		JToken.DeepEquals(actual, JToken.Parse(expected)).Should().BeTrue(
			because: $"expected: {JToken.Parse(expected)}\nactual:   {actual}");

	[Test]
	[Description("merge by _id deep-merges values: nested objects merge, arrays are replaced by the incoming value, new keys are added, _id is kept.")]
	public void Merge_ById_DeepMergesAndReplacesArrays() {
		JToken result = Apply(
			"""{ "a": { "_id": "a", "x": 1, "arr": [1, 2], "obj": { "p": 1, "q": 2 } } }""",
			"""[ { "operation": "merge", "name": "a", "values": { "x": 9, "arr": [7], "obj": { "q": 20, "r": 3 }, "y": 5 } } ]""");
		AssertEqual(result, """{ "a": { "_id": "a", "x": 9, "arr": [7], "obj": { "p": 1, "q": 20, "r": 3 }, "y": 5 } }""");
	}

	[Test]
	[Description("merge by path resolves the target container (e.g. attributes) and merges values into it — the viewModelConfigDiff attributes pattern.")]
	public void Merge_ByPath_MergesIntoResolvedContainer() {
		JToken result = Apply(
			"""{ "attributes": { "PDS_Name": { "modelConfig": { "path": "PDS.Name" } } } }""",
			"""[ { "operation": "merge", "path": ["attributes"], "values": { "PDS_New": { "modelConfig": { "path": "PDS.New" } } } } ]""");
		AssertEqual(result, """
			{ "attributes": {
				"PDS_Name": { "modelConfig": { "path": "PDS.Name" } },
				"PDS_New": { "modelConfig": { "path": "PDS.New" } } } }
			""");
	}

	[Test]
	[Description("a root merge (path: []) deep-merges values into the source root — the modelConfigDiff root-merge pattern.")]
	public void Merge_RootPath_MergesIntoRoot() {
		JToken result = Apply(
			"""{ "dataSources": { "PDS": { "type": "crt.EntityDataSource" } } }""",
			"""[ { "operation": "merge", "path": [], "values": { "dataSources": { "SecondDS": { "type": "crt.EntityDataSource" } } } } ]""");
		AssertEqual(result, """
			{ "dataSources": {
				"PDS": { "type": "crt.EntityDataSource" },
				"SecondDS": { "type": "crt.EntityDataSource" } } }
			""");
	}

	[Test]
	[Description("insert places the value into the array resolved by parentName + path.")]
	public void Insert_IntoPathArray() {
		JToken result = Apply(
			"""{ "wrap": { "_id": "wrap", "items": [ { "_id": "i1" } ] } }""",
			"""[ { "operation": "insert", "name": "i2", "parentName": "wrap", "path": ["items"], "values": { "_id": "i2", "x": 1 } } ]""");
		AssertEqual(result, """{ "wrap": { "_id": "wrap", "items": [ { "_id": "i1" }, { "_id": "i2", "x": 1 } ] } }""");
	}

	[Test]
	[Description("remove deletes an element by _id from its (named) container's array.")]
	public void Remove_ById_FromContainerArray() {
		JToken result = Apply(
			"""{ "wrap": { "_id": "wrap", "items": [ { "_id": "i1" }, { "_id": "i2" } ] } }""",
			"""[ { "operation": "remove", "name": "i1" } ]""");
		AssertEqual(result, """{ "wrap": { "_id": "wrap", "items": [ { "_id": "i2" } ] } }""");
	}

	[Test]
	[Description("remove with properties deletes only the named properties from the matched element.")]
	public void RemoveProperties_ById() {
		JToken result = Apply(
			"""{ "x": { "_id": "x", "a": 1, "b": 2 } }""",
			"""[ { "operation": "remove", "name": "x", "properties": ["a"] } ]""");
		AssertEqual(result, """{ "x": { "_id": "x", "b": 2 } }""");
	}

	[Test]
	[Description("identity is _id, not name: a merge by name against a node that only has a `name` (no `_id`) is a no-op.")]
	public void Merge_IdentityIsId_NotName() {
		JToken result = Apply(
			"""{ "a": { "name": "a", "x": 1 } }""",
			"""[ { "operation": "merge", "name": "a", "values": { "x": 9 } } ]""");
		AssertEqual(result, """{ "a": { "name": "a", "x": 1 } }""");
	}

	[Test]
	[Description("per-operation required parameters are NOT enforced: a merge with a path but no `name` applies (the base view-config applier would reject it as missing `name`).")]
	public void Merge_WithoutName_NotRejected() {
		JToken result = Apply(
			"""{ "attributes": {} }""",
			"""[ { "operation": "merge", "path": ["attributes"], "values": { "PDS_New": { "modelConfig": { "path": "PDS.New" } } } } ]""");
		AssertEqual(result, """{ "attributes": { "PDS_New": { "modelConfig": { "path": "PDS.New" } } } }""");
	}
}
