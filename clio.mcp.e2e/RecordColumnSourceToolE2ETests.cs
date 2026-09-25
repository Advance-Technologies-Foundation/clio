using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for ONE column of a record another element returned as a value source (ENG-91844) over
/// the real MCP path. NOT in CI — run manually, gated on the <c>process-designer</c> feature and a reachable
/// environment carrying CrtProcessBuilder 1.6.6.27 or later.
/// <para>The motivating session: an agent could not assign a Perform task to a read contact's <c>Owner</c>, nor
/// branch on the contact's <c>DoNotUseCall</c>, and built two filtered signal starts instead of one gateway. These
/// tests build exactly that shape by NAME - <c>sourceColumn</c> on the mapping, <c>[#Read.ResultEntity.Column#]</c>
/// in the condition - and read it back, because the JSON member names are exercised nowhere else: the unit tests
/// construct the descriptors in C#, so a renamed or mistyped member would be dropped by the serializer with the
/// whole unit suite green.</para>
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(CreateBusinessProcessTool.CreateBusinessProcessToolName)]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class RecordColumnSourceToolE2ETests {

	private const string CreateToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;
	private const string ModifyToolName = ModifyBusinessProcessTool.ModifyBusinessProcessToolName;

	/// <summary>The cut that resolves <c>sourceColumn</c>; named in the skip message so a developer knows what to install.</summary>
	private const string MinimumPackageVersion = "1.6.6.27";

	#region Methods: Tests

	[Test]
	[Description("create-business-process builds the motivating process in ONE schema: Read contact, an exclusive gateway whose condition names the read record's DoNotUseCall column, and a Perform task whose OwnerId is mapped from the read record's Owner through sourceColumn. describe reads the mapping back as the sourceElement / sourceElementParameter / sourceColumn trio, and the condition comes back expanded into the three-segment meta path.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process maps OwnerId from a read record's column and branches on another")]
	public async Task CreateBusinessProcess_Should_MapAndBranchOnReadRecordColumns() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("Record column source", MinimumPackageVersion);
		string processName = $"UsrClioBpRecordColumnE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await ProcessDesignerE2EArrange.CallToolAsync(context, CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = BuildOwnerTaskDescriptor(processName, "Owner")
			});

		// Assert
		JsonSerializer.Serialize(callResult).Should().Contain("created (UId:",
			because: "a column source and a column condition must build in one schema, which is the whole point");
		JsonObject graph = DescribedProcessGraph.Read(await ProcessDesignerE2EArrange.DescribeAsync(context, processName));
		JsonObject ownerId = ParameterNamed(ElementNamed(graph, "Call"), "OwnerId");
		(ownerId["sourceElement"]?.GetValue<string>()).Should().Be("ReadContact",
			because: "describe names the element whose record the column is read from");
		(ownerId["sourceElementParameter"]?.GetValue<string>()).Should().Be("ResultEntity",
			because: "and the record parameter");
		(ownerId["sourceColumn"]?.GetValue<string>()).Should().Be("Owner",
			because: "and the column, so the trio feeds straight back into addMapping");
		ownerId["value"]!.GetValue<string>().Should().Contain("[EntityColumn:",
			because: "the stored value is the platform's three-segment meta path");
		graph.ToJsonString().Should().NotContain("ReadContact.ResultEntity.DoNotUseCall",
			because: "the condition name is expanded at build; an unexpanded one fails the platform's gate");
		JsonObject mayCall = graph["flows"]!.AsArray().Select(flow => flow!.AsObject())
			.Single(flow => flow["target"]?.GetValue<string>() == "Call");
		mayCall["condition"]!.GetValue<string>().Should().Contain("[EntityColumn:",
			because: "the condition must carry the column segment, not merely have lost the name");
		JsonObject stamp = ElementNamed(graph, "Stamp");
		string stampJson = stamp.ToJsonString();
		stampJson.Should().Contain("\"sourceColumn\":\"Owner\"",
			because: "a Modify data value's sourceColumn crosses the wire and describes back by name");
		string filterReference = stamp["filter"]!["conditions"]!.AsArray().Single()!["expression"]!
			.GetValue<string>();
		filterReference.Should().Contain("[EntityColumn:",
			because: "the filter's elementParameter.column is stored as the three-segment reference - read off "
				+ "the described FILTER, since the value parameter above carries the same segment");
	}

	[Test]
	[Description("modify-business-process addMapping takes the same sourceColumn on an existing process and describe reads it back by name; a column whose lookup object does not fit the target is refused naming the column, and nothing is changed.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process addMapping with sourceColumn, and its type refusal")]
	public async Task ModifyBusinessProcess_Should_AddAColumnMapping_AndRefuseAnIncompatibleOne() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("Record column source", MinimumPackageVersion);
		string processName = $"UsrClioBpRecordColumnModE2e{Guid.NewGuid():N}";
		CallToolResult created = await ProcessDesignerE2EArrange.CallToolAsync(context, CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = BuildPlainTaskDescriptor(processName)
			});
		JsonSerializer.Serialize(created).Should().Contain("created (UId:",
			because: "the process the modify calls edit must exist, or every assertion below fails for the wrong reason");

		// Act
		CallToolResult refused = await ProcessDesignerE2EArrange.CallToolAsync(context, ModifyToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = AddOwnerMapping("Account")
			});
		JsonObject afterRefusal =
			DescribedProcessGraph.Read(await ProcessDesignerE2EArrange.DescribeAsync(context, processName));
		CallToolResult applied = await ProcessDesignerE2EArrange.CallToolAsync(context, ModifyToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = AddOwnerMapping("Owner")
			});

		// Assert
		string refusal = JsonSerializer.Serialize(refused);
		refusal.Should().Contain("Account",
			because: "an Account id in a Contact owner field would silently assign nobody, so the refusal names the column");
		refusal.Should().Contain("incompatible",
			because: "the refusal says WHY - the lookup objects differ");
		JsonObject? ownerAfterRefusal = OptionalParameterNamed(ElementNamed(afterRefusal, "Call"), "OwnerId");
		(ownerAfterRefusal?["sourceColumn"]?.GetValue<string>()).Should().BeNull(
			because: "a refused addMapping changes nothing, so OwnerId carries no column source afterwards");
		(ownerAfterRefusal?["value"]?.GetValue<string>() ?? string.Empty).Should().NotContain("[EntityColumn:",
			because: "nor a stored column reference that describe merely failed to name");
		JsonSerializer.Serialize(applied).Should().NotContain("incompatible",
			because: "Contact.Owner fits a Contact-lookup OwnerId");
		JsonObject graph = DescribedProcessGraph.Read(await ProcessDesignerE2EArrange.DescribeAsync(context, processName));
		(ParameterNamed(ElementNamed(graph, "Call"), "OwnerId")["sourceColumn"]?.GetValue<string>()).Should()
			.Be("Owner", because: "the modify path writes the same token the build path does, and describe names it");
	}

	[Test]
	[Description("A sourceColumn that is a PATH (Owner.Name) is refused at build with the one-column rule, rather than stored as a reference the runtime has not been shown to resolve.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process refuses a sourceColumn path")]
	public async Task CreateBusinessProcess_Should_RefuseASourceColumnPath() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("Record column source", MinimumPackageVersion);
		string processName = $"UsrClioBpRecordColumnPathE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await ProcessDesignerE2EArrange.CallToolAsync(context, CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = BuildOwnerTaskDescriptor(processName, "Owner.Name")
			});

		// Assert
		string resultJson = JsonSerializer.Serialize(callResult);
		resultJson.Should().NotContain("created (UId:", because: "a column path must abort the build");
		resultJson.Should().Contain("is a path", because: "the refusal names the one-column rule");
	}

	#endregion

	#region Methods: Private

	private static string BuildOwnerTaskDescriptor(string processName, string sourceColumn) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Record Column E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "Start1", "type": "startEvent" },
		    { "name": "ReadContact", "type": "readData", "caption": "Read contact",
		      "readData": { "source": "Contact", "mode": "first" } },
		    { "name": "Decide", "type": "exclusiveGateway", "caption": "Can we call?" },
		    { "name": "Call", "type": "performTask", "caption": "Call the contact" },
		    { "name": "Stamp", "type": "changeData", "caption": "Stamp the contact",
		      "changeData": { "source": "Contact", "values": [
		        { "column": "Owner", "sourceElement": "ReadContact", "sourceElementParameter": "ResultEntity",
		          "sourceColumn": "Owner" } ] },
		      "filter": { "object": "Contact", "logicalOperation": "and", "conditions": [
		        { "column": "Id", "comparison": "equal",
		          "elementParameter": { "elementName": "ReadContact", "parameter": "ResultEntity", "column": "Id" } } ] } },
		    { "name": "EndCall", "type": "endEvent" },
		    { "name": "EndSkip", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "Start1", "target": "ReadContact" },
		    { "source": "ReadContact", "target": "Decide" },
		    { "source": "Decide", "target": "Call", "kind": "conditional",
		      "condition": "[#ReadContact.ResultEntity.DoNotUseCall#] == false", "label": "May call" },
		    { "source": "Decide", "target": "Stamp", "kind": "default", "label": "Do not call" },
		    { "source": "Stamp", "target": "EndSkip" },
		    { "source": "Call", "target": "EndCall" }
		  ],
		  "mappings": [
		    { "elementName": "Call", "elementParameter": "OwnerId",
		      "sourceElement": "ReadContact", "sourceElementParameter": "ResultEntity",
		      "sourceColumn": "{{sourceColumn}}" }
		  ]
		}
		""";

	private static string BuildPlainTaskDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Record Column Modify E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "Start1", "type": "startEvent" },
		    { "name": "ReadContact", "type": "readData", "caption": "Read contact",
		      "readData": { "source": "Contact", "mode": "first" } },
		    { "name": "Call", "type": "performTask", "caption": "Call the contact" },
		    { "name": "End1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "Start1", "target": "ReadContact" },
		    { "source": "ReadContact", "target": "Call" },
		    { "source": "Call", "target": "End1" }
		  ]
		}
		""";

	private static string AddOwnerMapping(string sourceColumn) =>
		$$"""
		[
		  { "op": "addMapping", "mapping": { "elementName": "Call", "elementParameter": "OwnerId",
		    "sourceElement": "ReadContact", "sourceElementParameter": "ResultEntity",
		    "sourceColumn": "{{sourceColumn}}" } }
		]
		""";

	/// <summary>The described element with that name, as the graph reports it.</summary>
	private static JsonObject ElementNamed(JsonObject graph, string name) =>
		graph["elements"]!.AsArray()
			.Select(element => element!.AsObject())
			.Single(element => element["name"]!.GetValue<string>() == name);

	/// <summary>The described parameter with that name on an element.</summary>
	private static JsonObject ParameterNamed(JsonObject element, string name) =>
		OptionalParameterNamed(element, name)
		?? throw new InvalidOperationException($"Element parameter '{name}' is not described.");

	/// <summary>
	/// The described parameter with that name on an element, or null: describe omits an unbound input, so
	/// absence is a legitimate answer after a write that must change nothing.
	/// </summary>
	private static JsonObject? OptionalParameterNamed(JsonObject element, string name) =>
		(element["parameters"]?.AsArray() ?? new JsonArray())
			.Select(parameter => parameter!.AsObject())
			.SingleOrDefault(parameter => parameter["name"]!.GetValue<string>() == name);

	#endregion

}
