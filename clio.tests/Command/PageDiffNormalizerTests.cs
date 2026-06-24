using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageDiffNormalizerTests {

	private static JArray ViewConfigDiff(string mobileBody) =>
		(JArray)JObject.Parse(mobileBody)["viewConfigDiff"]!;

	private static JObject Op(JArray ops, string name) =>
		ops.OfType<JObject>().Single(o => o.Value<string>("name") == name);

	private static bool HasOp(JArray ops, string name) =>
		ops.OfType<JObject>().Any(o => o.Value<string>("name") == name);

	[Test]
	[Description("A merge op whose values carry a nested named element (itemLayout: {name:ListItem,…}) is split into a merge of that ListItem; the emptied parent op is dropped.")]
	public void Normalize_Mobile_NestedNamedElement_SplitsIntoOwnMergeAndDropsEmptyParent() {
		const string body = """
			{ "viewConfigDiff": [
				{ "operation": "merge", "name": "List", "values": {
					"itemLayout": { "name": "ListItem", "type": "crt.ListItem", "title": "$PDS_LeadName",
						"body": [ { "value": "$PDS_Status" } ] } } } ] }
			""";

		(string normalized, IReadOnlyList<string> splits) = PageDiffNormalizer.Normalize(body);

		splits.Should().Equal("List → ListItem");
		JArray ops = ViewConfigDiff(normalized);
		HasOp(ops, "List").Should().BeFalse(because: "the List op's values became empty after the row was lifted out");
		JObject listItem = Op(ops, "ListItem");
		listItem.Value<string>("operation").Should().Be("merge");
		listItem["values"]!["title"]!.Value<string>().Should().Be("$PDS_LeadName");
		listItem["values"]!["type"]!.Value<string>().Should().Be("crt.ListItem");
		((JArray)listItem["values"]!["body"]!).Should().HaveCount(1);
		listItem["values"]!["name"].Should().BeNull(because: "the child name moves to the op's name");
	}

	[Test]
	[Description("The parent op is kept (without the nested element) when its values still carry other properties.")]
	public void Normalize_Mobile_ParentKeptWhenItHasOtherValues() {
		const string body = """
			{ "viewConfigDiff": [
				{ "operation": "merge", "name": "List", "values": {
					"visible": true,
					"itemLayout": { "name": "ListItem", "title": "$X" } } } ] }
			""";

		(string normalized, IReadOnlyList<string> splits) = PageDiffNormalizer.Normalize(body);

		splits.Should().Equal("List → ListItem");
		JArray ops = ViewConfigDiff(normalized);
		JObject list = Op(ops, "List");
		list["values"]!["visible"]!.Value<bool>().Should().BeTrue();
		list["values"]!["itemLayout"].Should().BeNull(because: "the nested named element was lifted out");
		Op(ops, "ListItem")["values"]!["title"]!.Value<string>().Should().Be("$X");
	}

	[Test]
	[Description("Recurses into a lifted child: a grandchild named element becomes its own op too.")]
	public void Normalize_Mobile_RecursesIntoGrandchildren() {
		const string body = """
			{ "viewConfigDiff": [
				{ "operation": "merge", "name": "List", "values": {
					"itemLayout": { "name": "ListItem", "title": "$X",
						"items": [ { "name": "Sub", "type": "crt.Thing", "caption": "$C" } ] } } } ] }
			""";

		(string normalized, IReadOnlyList<string> splits) = PageDiffNormalizer.Normalize(body);

		splits.Should().Contain("List → ListItem").And.Contain("ListItem → Sub");
		JArray ops = ViewConfigDiff(normalized);
		JObject listItem = Op(ops, "ListItem");
		listItem["values"]!["title"]!.Value<string>().Should().Be("$X");
		listItem["values"]!["items"].Should().BeNull(because: "the named Sub item was lifted out and the emptied array removed");
		Op(ops, "Sub")["values"]!["caption"]!.Value<string>().Should().Be("$C");
	}

	[Test]
	[Description("An array of named elements (items: [{name:F1},{name:F2}]) splits each into its own op; the emptied array is removed and the parent dropped.")]
	public void Normalize_Mobile_ArrayOfNamedElements_SplitsEach() {
		const string body = """
			{ "viewConfigDiff": [
				{ "operation": "merge", "name": "Container", "values": {
					"items": [ { "name": "F1", "label": "$A" }, { "name": "F2", "label": "$B" } ] } } ] }
			""";

		(string normalized, IReadOnlyList<string> splits) = PageDiffNormalizer.Normalize(body);

		splits.Should().BeEquivalentTo("Container → F1", "Container → F2");
		JArray ops = ViewConfigDiff(normalized);
		HasOp(ops, "Container").Should().BeFalse();
		Op(ops, "F1")["values"]!["label"]!.Value<string>().Should().Be("$A");
		Op(ops, "F2")["values"]!["label"]!.Value<string>().Should().Be("$B");
	}

	[Test]
	[Description("An insert op with a nested named element (object slot) is split into two inserts: the parent (without the slot) and the child inserted under it via parentName + propertyName; the child keeps its name as identity.")]
	public void Normalize_Mobile_InsertWithNestedNamed_SplitsIntoInserts() {
		const string body = """
			{ "viewConfigDiff": [
				{ "operation": "insert", "name": "List", "parentName": "Main", "values": {
					"type": "crt.List", "itemLayout": { "name": "ListItem", "type": "crt.ListItem", "title": "$X" } } } ] }
			""";

		(string normalized, IReadOnlyList<string> splits) = PageDiffNormalizer.Normalize(body);

		splits.Should().Equal("List → ListItem");
		JArray ops = ViewConfigDiff(normalized);

		JObject list = Op(ops, "List");
		list.Value<string>("operation").Should().Be("insert");
		list.Value<string>("parentName").Should().Be("Main");
		list["values"]!["type"]!.Value<string>().Should().Be("crt.List");
		list["values"]!["itemLayout"].Should().BeNull(because: "the nested element was lifted out");

		JObject listItem = Op(ops, "ListItem");
		listItem.Value<string>("operation").Should().Be("insert");
		listItem.Value<string>("parentName").Should().Be("List", because: "it is inserted under its former parent");
		listItem.Value<string>("propertyName").Should().Be("itemLayout", because: "the object slot it occupied");
		listItem["values"]!["title"]!.Value<string>().Should().Be("$X");
		listItem["values"]!["name"]!.Value<string>().Should().Be("ListItem", because: "an inserted element keeps its name as identity");

		// The parent insert must precede the child insert (the child targets the parent by name).
		ops.OfType<JObject>().Select(o => o.Value<string>("name")).Should().Equal("List", "ListItem");
	}

	[Test]
	[Description("An insert op with an array of named elements splits each into its own insert targeting parentName + path; the parent keeps an empty array so the children can be inserted into it.")]
	public void Normalize_Mobile_InsertWithNamedArray_SplitsIntoIndexedInserts() {
		const string body = """
			{ "viewConfigDiff": [
				{ "operation": "insert", "name": "Container", "parentName": "Main", "values": {
					"type": "crt.Container",
					"items": [ { "name": "F1", "type": "crt.Input" }, { "name": "F2", "type": "crt.Input" } ] } } ] }
			""";

		(string normalized, IReadOnlyList<string> splits) = PageDiffNormalizer.Normalize(body);

		splits.Should().BeEquivalentTo("Container → F1", "Container → F2");
		JArray ops = ViewConfigDiff(normalized);

		JObject container = Op(ops, "Container");
		((JArray)container["values"]!["items"]!).Should().BeEmpty(because: "for insert the array is kept empty for the children to be inserted into");

		foreach (string name in new[] { "F1", "F2" }) {
			JObject field = Op(ops, name);
			field.Value<string>("operation").Should().Be("insert");
			field.Value<string>("parentName").Should().Be("Container");
			((JArray)field["path"]!).Select(t => t.Value<string>()).Should().Equal("items");
			field["values"]!["name"]!.Value<string>().Should().Be(name);
		}
		// Parent precedes its inserted children.
		ops.OfType<JObject>().Select(o => o.Value<string>("name")).First().Should().Be("Container");
	}

	[Test]
	[Description("Idempotent: re-normalizing an already-split body yields no further splits and the same body.")]
	public void Normalize_Mobile_IsIdempotent() {
		const string body = """
			{ "viewConfigDiff": [
				{ "operation": "merge", "name": "List", "values": {
					"itemLayout": { "name": "ListItem", "title": "$X" } } } ] }
			""";

		(string once, _) = PageDiffNormalizer.Normalize(body);
		(string twice, IReadOnlyList<string> secondSplits) = PageDiffNormalizer.Normalize(once);

		secondSplits.Should().BeEmpty();
		twice.Should().Be(once);
	}

	[Test]
	[Description("Tolerant: invalid JSON or a body without viewConfigDiff is returned unchanged.")]
	public void Normalize_Tolerant_OnUnparseableOrMissingDiff() {
		(string bad, IReadOnlyList<string> badSplits) = PageDiffNormalizer.Normalize("{ not json ");
		bad.Should().Be("{ not json ");
		badSplits.Should().BeEmpty();

		const string noDiff = """{ "viewModelConfig": { "attributes": {} } }""";
		(string same, IReadOnlyList<string> noSplits) = PageDiffNormalizer.Normalize(noDiff);
		same.Should().Be(noDiff);
		noSplits.Should().BeEmpty();
	}

	[Test]
	[Description("Web body: the nested named element inside the SCHEMA_VIEW_CONFIG_DIFF marker section is split, markers preserved.")]
	public void Normalize_Web_SplitsInsideMarkerSection() {
		string body =
			"define(\"Card\", [], function() { return { viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/" +
			"[ { \"operation\": \"merge\", \"name\": \"List\", \"values\": { \"itemLayout\": " +
			"{ \"name\": \"ListItem\", \"type\": \"crt.ListItem\", \"title\": \"$X\", \"body\": [] } } } ]" +
			"/**SCHEMA_VIEW_CONFIG_DIFF*/ }; });";

		(string normalized, IReadOnlyList<string> splits) = PageDiffNormalizer.Normalize(body);

		splits.Should().Equal("List → ListItem");
		normalized.Should().Contain("/**SCHEMA_VIEW_CONFIG_DIFF*/", because: "the marker wrappers are preserved");
		normalized.Should().Contain("\"name\": \"ListItem\"");
		normalized.Should().NotContain("itemLayout", because: "the row was lifted into its own merge ListItem op");
	}
}
