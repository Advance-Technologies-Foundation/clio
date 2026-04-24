using System;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageBodyEditorTests {

	private const string FormPageBody = """
		define("UsrTest_FormPage", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {
			return {
				viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
					{
						"operation": "insert",
						"name": "Name",
						"values": {
							"layoutConfig": {"column": 1, "row": 1, "colSpan": 1, "rowSpan": 1},
							"type": "crt.Input",
							"label": "$Resources.Strings.Name",
							"labelPosition": "auto",
							"control": "$Name"
						},
						"parentName": "SideAreaProfileContainer",
						"propertyName": "items",
						"index": 0
					},
					{
						"operation": "merge",
						"name": "Feed",
						"values": {"type": "crt.Feed", "dataSourceName": "PDS"},
						"parentName": "FeedTabContainer",
						"propertyName": "items",
						"index": 0
					}
				]/**SCHEMA_VIEW_CONFIG_DIFF*/,
				viewModelConfig: /**SCHEMA_VIEW_MODEL_CONFIG*/{
					"attributes": {
						"Name": {"modelConfig": {"path": "PDS.Name"}},
						"Id": {"modelConfig": {"path": "PDS.Id"}}
					}
				}/**SCHEMA_VIEW_MODEL_CONFIG*/,
				modelConfig: /**SCHEMA_MODEL_CONFIG*/{
					"dataSources": {"PDS": {"type": "crt.EntityDataSource", "config": {"entitySchemaName": "UsrTest"}}}
				}/**SCHEMA_MODEL_CONFIG*/,
				handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
				converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
				validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
			};
		});
		""";

	private const string ListPageBody = """
		define("UsrTest_ListPage", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {
			return {
				viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
					{
						"operation": "merge",
						"name": "DataTable",
						"values": {
							"columns": [
								{
									"id": "aaa-bbb",
									"code": "PDS_Name",
									"caption": "#ResourceString(PDS_Name)#",
									"dataValueType": 1
								}
							]
						}
					}
				]/**SCHEMA_VIEW_CONFIG_DIFF*/,
				viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[
					{
						"operation": "merge",
						"path": ["attributes", "Items", "viewModelConfig", "attributes"],
						"values": {
							"PDS_Name": {"modelConfig": {"path": "PDS.Name"}}
						}
					}
				]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
				modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[
					{
						"operation": "merge",
						"path": ["dataSources", "PDS", "config"],
						"values": {"entitySchemaName": "UsrTest"}
					}
				]/**SCHEMA_MODEL_CONFIG_DIFF*/,
				handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
				converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
				validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
			};
		});
		""";

	[Test]
	[Description("AddFormFields inserts an operation with the correct name, type and control into viewConfigDiff")]
	public void AddFormFields_AddsInsertOperation_ToViewConfigDiff() {
		string result = PageBodyEditor.AddFormFields(FormPageBody, [new FormFieldSpec("PDS.UsrStatus", "crt.ComboBox")]);
		JsonArray viewConfig = ParseMarkerJson(result, "SCHEMA_VIEW_CONFIG_DIFF").AsArray();
		JsonObject inserted = viewConfig.OfType<JsonObject>()
			.FirstOrDefault(o => o["name"]?.GetValue<string>() == "PDS_UsrStatus");
		inserted.Should().NotBeNull();
		inserted!["values"]!["type"]!.GetValue<string>().Should().Be("crt.ComboBox");
		inserted["values"]!["control"]!.GetValue<string>().Should().Be("$PDS_UsrStatus");
	}

	[Test]
	[Description("AddFormFields assigns the next row after existing fields and uses the discovered parent container")]
	public void AddFormFields_SetsCorrectRowAndParent() {
		string result = PageBodyEditor.AddFormFields(FormPageBody, [
			new FormFieldSpec("PDS.UsrStatus", "crt.ComboBox"),
			new FormFieldSpec("PDS.UsrDate", "crt.DateTimePicker")
		]);
		JsonArray viewConfig = ParseMarkerJson(result, "SCHEMA_VIEW_CONFIG_DIFF").AsArray();
		JsonObject first = viewConfig.OfType<JsonObject>()
			.First(o => o["name"]?.GetValue<string>() == "PDS_UsrStatus");
		first["values"]!["layoutConfig"]!["row"]!.GetValue<int>().Should().Be(2);
		first["parentName"]!.GetValue<string>().Should().Be("SideAreaProfileContainer");
	}

	[Test]
	[Description("AddFormFields adds the attribute entry with the correct modelConfig path to viewModelConfig")]
	public void AddFormFields_AddsAttributeToViewModelConfig() {
		string result = PageBodyEditor.AddFormFields(FormPageBody, [new FormFieldSpec("PDS.UsrStatus", "crt.ComboBox")]);
		JsonObject vmConfig = ParseMarkerJson(result, "SCHEMA_VIEW_MODEL_CONFIG").AsObject();
		JsonObject attrs = vmConfig["attributes"]!.AsObject();
		attrs.ContainsKey("PDS_UsrStatus").Should().BeTrue();
		attrs["PDS_UsrStatus"]!["modelConfig"]!["path"]!.GetValue<string>().Should().Be("PDS.UsrStatus");
	}

	[Test]
	[Description("AddFormFields does not add a duplicate insert operation when called twice with the same field")]
	public void AddFormFields_SkipsExistingField() {
		string once = PageBodyEditor.AddFormFields(FormPageBody, [new FormFieldSpec("PDS.UsrNew", "crt.Input")]);
		string twice = PageBodyEditor.AddFormFields(once, [new FormFieldSpec("PDS.UsrNew", "crt.Input")]);
		JsonArray viewConfig = ParseMarkerJson(twice, "SCHEMA_VIEW_CONFIG_DIFF").AsArray();
		int count = viewConfig.OfType<JsonObject>()
			.Count(o => o["name"]?.GetValue<string>() == "PDS_UsrNew" && o["operation"]?.GetValue<string>() == "insert");
		count.Should().Be(1);
	}

	[Test]
	[Description("AddFormFields sets the multiline flag when FormFieldSpec.Multiline is true")]
	public void AddFormFields_MultilineInput_SetsMultilineFlag() {
		string result = PageBodyEditor.AddFormFields(FormPageBody, [new FormFieldSpec("PDS.UsrDesc", "crt.Input", Multiline: true)]);
		JsonArray viewConfig = ParseMarkerJson(result, "SCHEMA_VIEW_CONFIG_DIFF").AsArray();
		JsonObject inserted = viewConfig.OfType<JsonObject>()
			.First(o => o["name"]?.GetValue<string>() == "PDS_UsrDesc");
		inserted["values"]!["multiline"]!.GetValue<bool>().Should().BeTrue();
	}

	[Test]
	[Description("AddFormFields sets pickerType when FormFieldSpec.PickerType is provided")]
	public void AddFormFields_DateTimePicker_SetsPickerType() {
		string result = PageBodyEditor.AddFormFields(FormPageBody, [new FormFieldSpec("PDS.UsrDate", "crt.DateTimePicker", PickerType: "date")]);
		JsonArray viewConfig = ParseMarkerJson(result, "SCHEMA_VIEW_CONFIG_DIFF").AsArray();
		JsonObject inserted = viewConfig.OfType<JsonObject>()
			.First(o => o["name"]?.GetValue<string>() == "PDS_UsrDate");
		inserted["values"]!["pickerType"]!.GetValue<string>().Should().Be("date");
	}

	[Test]
	[Description("AddFormFields uses the explicit ParentName from FormFieldSpec instead of discovering it")]
	public void AddFormFields_ExplicitParentName_OverridesDiscovery() {
		string result = PageBodyEditor.AddFormFields(FormPageBody, [new FormFieldSpec("PDS.UsrNew", "crt.Input", ParentName: "MyContainer")]);
		JsonArray viewConfig = ParseMarkerJson(result, "SCHEMA_VIEW_CONFIG_DIFF").AsArray();
		JsonObject inserted = viewConfig.OfType<JsonObject>()
			.First(o => o["name"]?.GetValue<string>() == "PDS_UsrNew");
		inserted["parentName"]!.GetValue<string>().Should().Be("MyContainer");
	}

	[Test]
	[Description("AddFormFields throws InvalidOperationException when the field path is empty")]
	public void AddFormFields_MissingPath_ThrowsInvalidOperationException() {
		Action act = () => PageBodyEditor.AddFormFields(FormPageBody, [new FormFieldSpec("", "crt.Input")]);
		act.Should().Throw<InvalidOperationException>();
	}

	[Test]
	[Description("AddFormFields throws InvalidOperationException when the field type is not a recognized form field type")]
	public void AddFormFields_InvalidType_ThrowsInvalidOperationException() {
		Action act = () => PageBodyEditor.AddFormFields(FormPageBody, [new FormFieldSpec("PDS.Foo", "crt.Unknown")]);
		act.Should().Throw<InvalidOperationException>();
	}

	[Test]
	[Description("AddFormFields preserves pre-existing non-insert operations in viewConfigDiff")]
	public void AddFormFields_PreservesNonInsertOperations() {
		string result = PageBodyEditor.AddFormFields(FormPageBody, [new FormFieldSpec("PDS.UsrStatus", "crt.ComboBox")]);
		JsonArray viewConfig = ParseMarkerJson(result, "SCHEMA_VIEW_CONFIG_DIFF").AsArray();
		JsonObject feed = viewConfig.OfType<JsonObject>()
			.FirstOrDefault(o => o["name"]?.GetValue<string>() == "Feed");
		feed.Should().NotBeNull();
		feed!["operation"]!.GetValue<string>().Should().Be("merge");
	}

	[Test]
	[Description("AddFormFields on a list page with SCHEMA_VIEW_MODEL_CONFIG_DIFF adds the attribute to the merge op values")]
	public void AddFormFields_WithDiffVariant_AddsMergeOp() {
		string result = PageBodyEditor.AddFormFields(ListPageBody, [new FormFieldSpec("PDS.UsrStatus", "crt.ComboBox")]);
		JsonArray vmDiff = ParseMarkerJson(result, "SCHEMA_VIEW_MODEL_CONFIG_DIFF").AsArray();
		JsonObject mergeOp = vmDiff.OfType<JsonObject>()
			.First(o => {
				JsonArray path = o["path"] as JsonArray;
				return path != null && path.Any(p => p?.GetValue<string>() == "attributes");
			});
		mergeOp["values"]!.AsObject().ContainsKey("PDS_UsrStatus").Should().BeTrue();
	}

	[Test]
	[Description("AddListColumns inserts a new column entry with the correct code into the DataTable columns array")]
	public void AddListColumns_AddsColumnToDataTable() {
		string result = PageBodyEditor.AddListColumns(ListPageBody, [new ListColumnSpec("PDS_UsrStatus", 10)]);
		JsonArray viewConfig = ParseMarkerJson(result, "SCHEMA_VIEW_CONFIG_DIFF").AsArray();
		JsonObject dataTable = viewConfig.OfType<JsonObject>()
			.First(o => o["name"]?.GetValue<string>() == "DataTable");
		JsonArray columns = dataTable["values"]!["columns"]!.AsArray();
		columns.OfType<JsonObject>().Any(c => c["code"]?.GetValue<string>() == "PDS_UsrStatus").Should().BeTrue();
	}

	[Test]
	[Description("AddListColumns keeps pre-existing columns when adding a new one")]
	public void AddListColumns_PreservesExistingColumns() {
		string result = PageBodyEditor.AddListColumns(ListPageBody, [new ListColumnSpec("PDS_UsrStatus", 10)]);
		JsonArray viewConfig = ParseMarkerJson(result, "SCHEMA_VIEW_CONFIG_DIFF").AsArray();
		JsonObject dataTable = viewConfig.OfType<JsonObject>()
			.First(o => o["name"]?.GetValue<string>() == "DataTable");
		JsonArray columns = dataTable["values"]!["columns"]!.AsArray();
		columns.OfType<JsonObject>().Any(c => c["code"]?.GetValue<string>() == "PDS_Name").Should().BeTrue();
	}

	[Test]
	[Description("AddListColumns does not add a duplicate entry when a column with the same code already exists")]
	public void AddListColumns_SkipsDuplicateColumn() {
		string result = PageBodyEditor.AddListColumns(ListPageBody, [new ListColumnSpec("PDS_Name", 1)]);
		JsonArray viewConfig = ParseMarkerJson(result, "SCHEMA_VIEW_CONFIG_DIFF").AsArray();
		JsonObject dataTable = viewConfig.OfType<JsonObject>()
			.First(o => o["name"]?.GetValue<string>() == "DataTable");
		JsonArray columns = dataTable["values"]!["columns"]!.AsArray();
		int count = columns.OfType<JsonObject>().Count(c => c["code"]?.GetValue<string>() == "PDS_Name");
		count.Should().Be(1);
	}

	[Test]
	[Description("AddListColumns adds the column attribute with the correct modelConfig path to viewModelConfigDiff")]
	public void AddListColumns_AddsAttributeToViewModelConfigDiff() {
		string result = PageBodyEditor.AddListColumns(ListPageBody, [new ListColumnSpec("PDS_UsrStatus", 10)]);
		JsonArray vmDiff = ParseMarkerJson(result, "SCHEMA_VIEW_MODEL_CONFIG_DIFF").AsArray();
		JsonObject mergeOp = vmDiff.OfType<JsonObject>()
			.First(o => {
				JsonArray path = o["path"] as JsonArray;
				return path != null && path.Any(p => p?.GetValue<string>() == "attributes");
			});
		JsonObject values = mergeOp["values"]!.AsObject();
		values.ContainsKey("PDS_UsrStatus").Should().BeTrue();
		values["PDS_UsrStatus"]!["modelConfig"]!["path"]!.GetValue<string>().Should().Be("PDS.UsrStatus");
	}

	[Test]
	[Description("AddListColumns throws InvalidOperationException when there is no DataTable operation in viewConfigDiff")]
	public void AddListColumns_MissingDataTable_ThrowsInvalidOperationException() {
		Action act = () => PageBodyEditor.AddListColumns(FormPageBody, [new ListColumnSpec("PDS_UsrStatus", 10)]);
		act.Should().Throw<InvalidOperationException>();
	}

	[Test]
	[Description("AddListColumns uses the provided Caption value on the inserted column entry")]
	public void AddListColumns_ColumnWithCaption_UsesProvidedCaption() {
		string result = PageBodyEditor.AddListColumns(ListPageBody, [new ListColumnSpec("PDS_UsrStatus", 10, Caption: "Status")]);
		JsonArray viewConfig = ParseMarkerJson(result, "SCHEMA_VIEW_CONFIG_DIFF").AsArray();
		JsonObject dataTable = viewConfig.OfType<JsonObject>()
			.First(o => o["name"]?.GetValue<string>() == "DataTable");
		JsonArray columns = dataTable["values"]!["columns"]!.AsArray();
		JsonObject col = columns.OfType<JsonObject>().First(c => c["code"]?.GetValue<string>() == "PDS_UsrStatus");
		col["caption"]!.GetValue<string>().Should().Be("Status");
	}

	[Test]
	[Description("Calling AddFormFields twice with the same fields produces the same viewConfigDiff count as calling once")]
	public void AddFormFields_IsIdempotent_WhenCalledTwice() {
		FormFieldSpec[] fields = [new FormFieldSpec("PDS.UsrStatus", "crt.ComboBox")];
		string once = PageBodyEditor.AddFormFields(FormPageBody, fields);
		string twice = PageBodyEditor.AddFormFields(once, fields);
		JsonArray viewConfigOnce = ParseMarkerJson(once, "SCHEMA_VIEW_CONFIG_DIFF").AsArray();
		JsonArray viewConfigTwice = ParseMarkerJson(twice, "SCHEMA_VIEW_CONFIG_DIFF").AsArray();
		viewConfigTwice.Count.Should().Be(viewConfigOnce.Count);
	}

	[Test]
	[Description("Calling AddListColumns twice with the same columns produces the same DataTable column count as calling once")]
	public void AddListColumns_IsIdempotent_WhenCalledTwice() {
		ListColumnSpec[] columns = [new ListColumnSpec("PDS_UsrStatus", 10)];
		string once = PageBodyEditor.AddListColumns(ListPageBody, columns);
		string twice = PageBodyEditor.AddListColumns(once, columns);
		JsonArray viewConfigOnce = ParseMarkerJson(once, "SCHEMA_VIEW_CONFIG_DIFF").AsArray();
		JsonArray viewConfigTwice = ParseMarkerJson(twice, "SCHEMA_VIEW_CONFIG_DIFF").AsArray();
		JsonObject dtOnce = viewConfigOnce.OfType<JsonObject>().First(o => o["name"]?.GetValue<string>() == "DataTable");
		JsonObject dtTwice = viewConfigTwice.OfType<JsonObject>().First(o => o["name"]?.GetValue<string>() == "DataTable");
		dtTwice["values"]!["columns"]!.AsArray().Count.Should().Be(dtOnce["values"]!["columns"]!.AsArray().Count);
	}

	private static JsonNode ParseMarkerJson(string body, string marker) {
		string token = $"/**{marker}*/";
		int start = body.IndexOf(token, StringComparison.Ordinal) + token.Length;
		int end = body.IndexOf(token, start, StringComparison.Ordinal);
		string content = body[start..end].Trim();
		content = System.Text.RegularExpressions.Regex.Replace(content, @",(\s*[\]\}])", "$1");
		return System.Text.Json.Nodes.JsonNode.Parse(content)!;
	}
}
