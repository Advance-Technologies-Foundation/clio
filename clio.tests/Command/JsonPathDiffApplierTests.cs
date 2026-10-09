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
	[Test]
	[Description("GH-1753: a merge whose path does not resolve is skipped, and the optional UnresolvedMerges sink receives it while the resolvable merge beside it still applies.")]
	public void Apply_ShouldReportUnresolvedMerge_WhenSinkIsProvided() {
		// Arrange
		var unresolved = new System.Collections.Generic.List<JObject>();
		var options = new JsonApplierOperationsOptions { UnresolvedMerges = unresolved };
		JArray operations = (JArray)JToken.Parse("""
			[
				{ "operation": "merge", "path": ["dataSources", "NewDS"], "values": { "type": "crt.EntityDataSource" } },
				{ "operation": "merge", "path": ["dataSources"], "values": { "OtherDS": { "type": "crt.EntityDataSource" } } }
			]
			""");

		// Act
		JToken result = new JsonPathDiffApplier().Apply(
			JToken.Parse("""{ "dataSources": { "PDS": {} } }"""), operations, options);

		// Assert
		AssertEqual(result, """{ "dataSources": { "PDS": {}, "OtherDS": { "type": "crt.EntityDataSource" } } }""");
		unresolved.Should().ContainSingle(because: "only the merge into the missing 'NewDS' key fails to resolve");
		unresolved[0]["path"]!.ToString(Newtonsoft.Json.Formatting.None).Should().Be("[\"dataSources\",\"NewDS\"]",
			because: "the sink receives the skipped operation itself, so a caller can name its path");
	}

	[Test]
	[Description("GH-1753: the UnresolvedMerges sink only observes - the applied result is identical with and without it.")]
	public void Apply_ShouldProduceSameResult_WhenUnresolvedMergeSinkIsProvided() {
		// Arrange
		const string source = """{ "attributes": { "A": { "x": 1 } } }""";
		const string operations = """
			[
				{ "operation": "merge", "path": ["attributes", "Missing"], "values": { "y": 1 } },
				{ "operation": "merge", "path": ["attributes", "A"], "values": { "x": 2 } }
			]
			""";

		// Act
		JToken withoutSink = new JsonPathDiffApplier().Apply(JToken.Parse(source), (JArray)JToken.Parse(operations));
		JToken withSink = new JsonPathDiffApplier().Apply(JToken.Parse(source), (JArray)JToken.Parse(operations),
			new JsonApplierOperationsOptions { UnresolvedMerges = new System.Collections.Generic.List<JObject>() });

		// Assert
		JToken.DeepEquals(withSink, withoutSink).Should().BeTrue(
			because: "the sink is a diagnostic and must never change what the differ applies");
	}
	[TestCase("null")]
	[TestCase("false")]
	[TestCase("0")]
	[TestCase("\"\"")]
	[Description("GH-1753: like the client's `!itemInfo.item` test, a falsy value at the merge path is treated as a missing target - the merge is skipped and reported, not merged into (which threw InvalidCastException).")]
	public void Merge_ShouldSkipAndReport_WhenPathPointsAtFalsyValue(string falsyValue) {
		// Arrange
		var unresolved = new System.Collections.Generic.List<JObject>();
		JToken source = JToken.Parse("{ \"attributes\": { \"A\": " + falsyValue + " } }");

		// Act
		JToken result = new JsonPathDiffApplier().Apply(source,
			(JArray)JToken.Parse("""[ { "operation": "merge", "path": ["attributes", "A"], "values": { "x": 1 } } ]"""),
			new JsonApplierOperationsOptions { UnresolvedMerges = unresolved });

		// Assert
		JToken.DeepEquals(result, source).Should().BeTrue(because: "the client skips a merge whose target is falsy");
		unresolved.Should().ContainSingle(because: "the skipped merge must reach the diagnostic sink");
	}

	[TestCase("null")]
	[TestCase("false")]
	[TestCase("0")]
	[TestCase("\"\"")]
	[Description("GH-1753: remove with properties shares the falsy fallback with merge; a falsy value at the path with no element of that _id is a no-op, as on the client (it used to cast the value to an object and throw).")]
	public void Remove_ShouldBeNoOp_WhenPropertiesPathPointsAtFalsyValue(string falsyValue) {
		// Arrange
		JToken source = JToken.Parse("{ \"attributes\": { \"A\": " + falsyValue + " } }");

		// Act
		JToken result = new JsonPathDiffApplier().Apply(source,
			(JArray)JToken.Parse("""[ { "operation": "remove", "path": ["attributes", "A"], "properties": ["x"] } ]"""));

		// Assert
		JToken.DeepEquals(result, source).Should().BeTrue(because: "the client finds no item to remove properties from");
	}

	[TestCase("5")]
	[TestCase("true")]
	[TestCase("\"text\"")]
	[Description("GH-1753: a merge into a single value throws, like the client, which sets the merged keys on a primitive in strict mode.")]
	public void Merge_ShouldThrow_WhenTargetIsSingleValue(string value) {
		// Arrange
		JToken source = JToken.Parse("{ \"attributes\": { \"A\": " + value + " } }");

		// Act
		System.Action act = () => new JsonPathDiffApplier().Apply(source,
			(JArray)JToken.Parse("""[ { "operation": "merge", "path": ["attributes", "A"], "values": { "x": 1 } } ]"""));

		// Assert
		act.Should().Throw<JsonDiffApplierException>(because: "the client throws a TypeError for this merge")
			.WithMessage("*[\"attributes\",\"A\"]*not an object*");
	}

	[Test]
	[Description("GH-1753: a merge into an array reports success and changes nothing, like the client, and reaches the ArrayTargetMerges sink.")]
	public void Merge_ShouldChangeNothingAndReport_WhenTargetIsArray() {
		// Arrange
		var arrayTargets = new System.Collections.Generic.List<JObject>();
		var unresolved = new System.Collections.Generic.List<JObject>();
		JToken source = JToken.Parse("""{ "attributes": { "A": [1, 2] } }""");

		// Act
		JToken result = new JsonPathDiffApplier().Apply(source,
			(JArray)JToken.Parse("""[ { "operation": "merge", "path": ["attributes", "A"], "values": { "x": 1 } } ]"""),
			new JsonApplierOperationsOptions { UnresolvedMerges = unresolved, ArrayTargetMerges = arrayTargets });

		// Assert
		JToken.DeepEquals(result, source).Should().BeTrue(because: "keys set on an array are lost");
		arrayTargets.Should().ContainSingle(because: "the merge has no effect and must reach the diagnostic sink");
		unresolved.Should().BeEmpty(because: "the client reports this merge as applied");
	}

	[TestCase("""{ "operation": "merge", "path": ["attributes", "A"] }""")]
	[TestCase("""{ "operation": "merge", "path": ["attributes", "A"], "values": null }""")]
	[Description("GH-1753: a merge with missing or null values on a target that resolves throws, like the client's Object.keys(undefined or null).")]
	public void Merge_ShouldThrow_WhenValuesMissingAndTargetResolves(string merge) {
		// Arrange
		JToken source = JToken.Parse("""{ "attributes": { "A": { "y": 1 } } }""");

		// Act
		System.Action act = () => new JsonPathDiffApplier().Apply(source, (JArray)JToken.Parse($"[{merge}]"));

		// Assert
		act.Should().Throw<JsonDiffApplierException>(because: "the client throws a TypeError for this merge")
			.WithMessage("*has no \"values\"*");
	}

	[TestCase("""["p", "q"]""", """{ "y": 1, "0": "p", "1": "q" }""")]
	[TestCase("\"pq\"", """{ "y": 1, "0": "p", "1": "q" }""")]
	[TestCase("5", """{ "y": 1 }""")]
	[TestCase("true", """{ "y": 1 }""")]
	[Description("GH-1753: values that are not an object are keyed like JS Object.keys - an array or a string by index, a number or a boolean not at all.")]
	public void Merge_ShouldKeyNonObjectValuesLikeObjectKeys(string values, string expected) {
		// Arrange
		JToken source = JToken.Parse("""{ "attributes": { "A": { "y": 1 } } }""");

		// Act
		JToken result = new JsonPathDiffApplier().Apply(source,
			(JArray)JToken.Parse("""[ { "operation": "merge", "path": ["attributes", "A"], "values": """ + values + " } ]"));

		// Assert
		AssertEqual(result["attributes"]!["A"]!, expected);
	}

	[Test]
	[Description("GH-1753: when the first path segment names an element by _id and the rest of the path does not resolve, the client merges into undefined and throws; the clone throws too instead of a NullReferenceException.")]
	public void Merge_ShouldThrow_WhenIdFallbackRemainderDoesNotResolve() {
		// Arrange
		JToken source = JToken.Parse("""{ "attributes": { "A": { "_id": "Root", "inner": {} } } }""");

		// Act
		System.Action act = () => new JsonPathDiffApplier().Apply(source,
			(JArray)JToken.Parse("""[ { "operation": "merge", "path": ["Root", "missing"], "values": { "x": 1 } } ]"""));

		// Assert
		act.Should().Throw<JsonDiffApplierException>(because: "the client throws a TypeError for this merge");
	}

	[TestCase("""{ "attributes": { "A": 5 } }""", """{ "operation": "merge", "path": ["attributes", "A"], "values": { "x": 1 } }""", JsonDiffApplierMergeFailure.TargetNotObject)]
	[TestCase("""{ "attributes": { "A": {} } }""", """{ "operation": "merge", "path": ["attributes", "A"], "values": null }""", JsonDiffApplierMergeFailure.ValuesMissing)]
	[TestCase("""{ "attributes": { "A": { "_id": "Root" } } }""", """{ "operation": "merge", "path": ["Root", "missing"], "values": { "x": 1 } }""", JsonDiffApplierMergeFailure.TargetUnresolved)]
	[TestCase("""{ "attributes": { "A": { "_id": "Root" } } }""", """{ "operation": "merge", "path": ["Root", "missing"], "values": {} }""", JsonDiffApplierMergeFailure.TargetUnresolved)]
	[TestCase("""{ "attributes": { "A": "s" } }""", """{ "operation": "merge", "path": ["attributes", "A"], "values": {} }""", JsonDiffApplierMergeFailure.TargetNotObject)]
	[TestCase("""{ "attributes": { "A": "2026-10-08T00:00:00" } }""", """{ "operation": "merge", "path": ["attributes", "A"], "values": {} }""", JsonDiffApplierMergeFailure.TargetNotObject)]
	[Description("GH-1753: a merge the client throws on carries the cause, so a caller reports it instead of guessing it from the operation; an undefined target and a non-empty string target (a date-like one too) throw even with empty values, as deepmerge does.")]
	public void Merge_ShouldReportWhyItThrows(string source, string merge, JsonDiffApplierMergeFailure expected) {
		// Act
		System.Action act = () => new JsonPathDiffApplier().Apply(JToken.Parse(source), (JArray)JToken.Parse($"[{merge}]"));

		// Assert
		act.Should().Throw<JsonDiffApplierException>(because: "the client throws a TypeError for this merge")
			.Which.MergeFailure.Should().Be(expected, because: "the caller names the cause from this value");
	}

	[TestCase("""{ "attributes": { "A": 5 } }""", """["attributes", "A"]""")]
	[TestCase("""{ "attributes": { "A": true } }""", """["attributes", "A"]""")]
	[TestCase("""{ "attributes": { "A": { "_id": "Root", "inner": "" } } }""", """["Root", "inner"]""")]
	[Description("GH-1753: with empty values, a merge into a number, a boolean or an empty string changes nothing and does not throw, as on the client.")]
	public void Merge_ShouldBeNoOp_WhenValuesAreEmptyAndTargetIsSingleValue(string source, string path) {
		// Arrange
		JToken sourceToken = JToken.Parse(source);

		// Act
		JToken result = new JsonPathDiffApplier().Apply(sourceToken.DeepClone(),
			(JArray)JToken.Parse("[{ \"operation\": \"merge\", \"path\": " + path + ", \"values\": {} }]"));

		// Assert
		JToken.DeepEquals(result, sourceToken).Should().BeTrue(because: "deepmerge has no key to write back");
	}
}
