using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using static Clio.Mcp.E2E.Support.Mcp.LookupMacroReads;
using static Clio.Mcp.E2E.Support.Mcp.ProcessDesignerE2ESupport;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for how a fixed lookup value is stored in the column values of Modify data
/// (<c>changeData.values</c>), Add data (<c>addData.values</c>) and Open edit page (<c>openEditPage.defaultValues</c>):
/// a <c>[#Lookup…#]</c> macro naming its object by name is stored in the form the designer stores -
/// <c>[#Lookup.{root object schema UId}.{record id}#]</c> - with a "stored as" warning, and describe reads that form
/// back; a plain record id on a Lookup column is refused with the exact macro to send. Send chat message column values
/// take the same path and are covered in <see cref="SendChatMessageElementToolE2ETests"/>.
/// <para>Stand prerequisites: a Studio stand - the objects (Contact, ContactType, Account, AccountCategory) and the page
/// AccountPageV2 ship with it; the record ids and the root schema UIds are read at arrange time. Every case builds its own
/// process and removes it in <c>finally</c>; nothing else is written. Strictly sequential.</para>
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(CreateBusinessProcessTool.CreateBusinessProcessToolName)]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class ColumnValueLookupMacroToolE2ETests {

	private const string CreateToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;

	/// <summary>The first CrtProcessBuilder that stores column-value lookup macros in the designer's form.</summary>
	private const string MinimumPackageVersion = "1.6.6.177";

	private const string Subject = "column-value lookup macro";

	/// <summary>The member of each element's block (named like its type) that carries the column values.</summary>
	private static readonly Dictionary<string, string> ValuesMember = new() {
		["changeData"] = "values",
		["addData"] = "values",
		["openEditPage"] = "defaultValues"
	};

	[TestCase("changeData", "Contact", "Type", "ContactType")]
	[TestCase("addData", "Contact", "Type", "ContactType")]
	[TestCase("openEditPage", "AccountPageV2", "AccountCategory", "AccountCategory")]
	[Description("TC-I-11: a fixed lookup value sent as [#Lookup.<object name>.<record id>#] in an element's column values builds, comes back with a 'stored as' warning, and describe reads it back as [#Lookup.<root object schema UId>.<record id>#] - the only form the designer card can show.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process stores a column-value lookup macro in the designer's form")]
	public async Task CreateBusinessProcess_Should_StoreANamedLookupMacro_InTheDesignersForm(string elementType,
			string objectOrPage, string column, string referencedObject) {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync(Subject, MinimumPackageVersion, TimeSpan.FromMinutes(8));
		string rootUId = await RootSchemaUIdAsync(context, referencedObject);
		string recordId = await AnyRecordIdAsync(context, referencedObject);
		string canonical = $"[#Lookup.{rootUId}.{recordId}#]";
		string entry = $$"""{ "column": "{{column}}", "expression": "[#Lookup.{{referencedObject}}.{{recordId}}#]" }""";
		string processName = NewProcessName("Named");
		List<string> created = [processName];
		try {
			// Act
			CallToolResult callResult = await CreateAsync(context, Descriptor(processName, elementType, objectOrPage, entry));

			// Assert
			ShouldHaveBuilt(callResult, "a named lookup macro on a Lookup column is accepted");
			WarningText(callResult).Should().ContainEquivalentOf($"stored as '{canonical}'",
				because: "the rewrite to the designer's form is reported, naming what was stored");
			JsonObject stored = ValueEntry(await DescribeElementAsync(context, processName), elementType, column);
			(Text(stored, "expression") ?? string.Empty).Should().BeEquivalentTo(canonical,
				because: "describe reads back the canonical macro: the root object schema UId and the record id");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-12: a plain record id in 'value' on a Lookup column is refused - it would be stored as raw text and fail at run time - and the refusal quotes the exact macro to send, with the column's object UId, so the caller does not have to guess the object segment.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process refuses a plain record id on a Lookup column and quotes the macro")]
	public async Task CreateBusinessProcess_Should_RefuseAPlainRecordId_AndQuoteTheMacro() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync(Subject, MinimumPackageVersion, TimeSpan.FromMinutes(8));
		string rootUId = await RootSchemaUIdAsync(context, "ContactType");
		string recordId = await AnyRecordIdAsync(context, "ContactType");
		string entry = $$"""{ "column": "Type", "value": "{{recordId}}" }""";
		string processName = NewProcessName("PlainId");
		// Nothing is built when the refusal holds, so there is nothing to clean up unless it does not.
		List<string> created = [];
		try {
			// Act
			CallToolResult callResult = await CreateAsync(context, Descriptor(processName, "changeData", "Contact", entry));
			if (LogText(callResult).Contains(CreatedLine, StringComparison.Ordinal)) {
				created.Add(processName);
			}

			// Assert
			ShouldBeRefused(callResult, "a plain constant on a Lookup column cannot be stored in a form the runtime reads");
			string log = LogText(callResult);
			log.Should().Contain("cannot take a plain 'value' constant",
				because: "the refusal says why the constant is refused");
			log.Should().ContainEquivalentOf($"[#Lookup.{rootUId}.<record id>#]",
				because: "the refusal quotes the exact macro to send, with the column's object UId");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	#region Methods: Arrange and helpers

	private static string NewProcessName(string caseName) => $"UsrClioBpLookup{caseName}E2e{Guid.NewGuid():N}";

	/// <summary>
	/// Start -> one element of the given kind carrying the one column value -> End. <paramref name="objectOrPage"/> is
	/// the data element's object, or the Open edit page element's page.
	/// </summary>
	private static string Descriptor(string processName, string elementType, string objectOrPage, string entry) {
		string block = elementType switch {
			"changeData" or "addData" => $$"""{ "source": "{{objectOrPage}}", "values": [ {{entry}} ] }""",
			"openEditPage" => $$"""
				{ "page": "{{objectOrPage}}", "editMode": "add", "recommendation": "Check the category",
				  "defaultValues": [ {{entry}} ], "completion": { "mode": "onSave" } }
				""",
			_ => throw new ArgumentOutOfRangeException(nameof(elementType), elementType, null)
		};
		return $$"""
			{
			  "name": "{{processName}}",
			  "caption": "Clio BP column-value lookup E2E",
			  "packageName": "Custom",
			  "elements": [
			    { "name": "StartEvent1", "type": "startEvent" },
			    { "name": "Element1", "type": "{{elementType}}", "caption": "Element1", "{{elementType}}": {{block}} },
			    { "name": "EndEvent1", "type": "endEvent" }
			  ],
			  "flows": [ { "source": "StartEvent1", "target": "Element1" }, { "source": "Element1", "target": "EndEvent1" } ]
			}
			""";
	}

	/// <summary>Builds a process; ignores the test when clio refused the call because the package is behind.</summary>
	private static async Task<CallToolResult> CreateAsync(ProcessDesignerArrangeContext context, string descriptor) {
		CallToolResult result = await ProcessDesignerE2EArrange.CallToolAsync(context, CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = descriptor
			});
		IgnoreWhenProcessBuilderIsBehind(ToolText(result), MinimumPackageVersion);
		return result;
	}

	/// <summary>The described Element1 of the process.</summary>
	private static async Task<JsonObject> DescribeElementAsync(ProcessDesignerArrangeContext context,
			string processName) {
		JsonObject graph = DescribedProcessGraph.Read(await ProcessDesignerE2EArrange.DescribeAsync(context, processName));
		return (graph["elements"] as JsonArray ?? []).OfType<JsonObject>()
			.Should().ContainSingle(element => Text(element, "name") == "Element1",
				because: "the process carries the one element under test").Which;
	}

	/// <summary>The described column-value entry for <paramref name="column"/> in the element's block.</summary>
	private static JsonObject ValueEntry(JsonObject element, string elementType, string column) {
		string blockKey = elementType;
		string valuesKey = ValuesMember[elementType];
		element[blockKey].Should().BeOfType<JsonObject>(
			because: $"describe reports the {blockKey} block; what was read: {{0}}", element.ToJsonString());
		return (element[blockKey]![valuesKey] as JsonArray ?? []).OfType<JsonObject>()
			.Should().ContainSingle(entry => Text(entry, "column") == column,
				because: $"the {valuesKey} list reads back the one column value sent").Which;
	}

	private static async Task DeleteProcessesAsync(ProcessDesignerArrangeContext context, List<string> created) {
		for (int index = created.Count - 1; index >= 0; index--) {
			await DeleteProcessAsync(context.EnvironmentName, created[index]);
		}
	}

	#endregion

}
