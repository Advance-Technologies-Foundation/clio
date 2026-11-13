using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Mcp.E2E.Support;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using Clio.Package;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using static Clio.Mcp.E2E.Support.Mcp.ProcessDesignerE2ESupport;
using static Clio.Mcp.E2E.Support.Mcp.ProcessDesignerStoredReads;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the Send chat message element (<c>type:"sendChatMessage"</c>, the <c>chatMessage</c>
/// block) over the real MCP path. Run manually against a stand, one framework at a time (<c>-f net10.0</c>, then
/// <c>-f net8.0</c>); the process-designer lane runs it with the whole <c>McpE2E.ProcessDesigner</c> category.
/// <para>What only this level can show is the wire and the platform: the block travels as JSON members the server
/// contract must declare, the channel, contact and chat template resolve against the stand's records, the element is
/// stored with the shapes the designer stores, and describe reads them back from a real saved schema.</para>
/// <para>Stand prerequisites. The OmnichannelMessaging package (the <c>SendChatMessageUserTask</c> schema), an
/// ACTIVE <c>Channel</c> row and, for the template cases, a message template of type "Chat template". Without the
/// package every case is Ignored with "install OmnichannelMessaging"; without a channel or a chat template the cases
/// that need one are Ignored with what to create - so a lane stand without the package stays green but proves
/// nothing for chat (an open question for the lane owner). The read-only parity case needs the designer-built process
/// <c>UsrSendChatDesignerFixture</c>; the run cases need <c>McpE2E:AllowDestructiveMcpTests</c>. Every other case
/// builds its own processes and removes them, newest first, in <c>finally</c>. Strictly sequential: schema writes in a
/// parallel burst take a .NET Framework stand's app pool down. No case creates or deletes a channel, a contact
/// identity or a system setting.</para>
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(CreateBusinessProcessTool.CreateBusinessProcessToolName)]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class SendChatMessageElementToolE2ETests {

	private const string CreateToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;
	private const string ModifyToolName = ModifyBusinessProcessTool.ModifyBusinessProcessToolName;

	/// <summary>
	/// The CrtProcessBuilder the fixture runs against: the first cut that ships the <c>chatMessage</c> block. Below it
	/// every case is Ignored, because an older package does not declare the block and refuses it as an unknown key.
	/// Raise it when a case starts asserting newer package behaviour, not on every rebundle.
	/// </summary>
	private const string MinimumPackageVersion = "1.6.6.174";

	/// <summary>What the skip message calls these tests.</summary>
	private const string Subject = "Send chat message element";

	/// <summary>The user task the element is built on, shipped by OmnichannelMessaging.</summary>
	private const string ChatTaskName = "SendChatMessageUserTask";

	/// <summary>The build token describe reports for a Send chat message element.</summary>
	private const string ChatBuildType = "sendchatmessage";

	/// <summary>The EmailTemplate type "Chat template".</summary>
	private const string ChatTemplateTypeId = "62215de1-23e0-4037-88c9-65ccf72904a4";

	/// <summary>The EmailTemplate type "Email template", for the wrong-type refusal.</summary>
	private const string EmailTemplateTypeId = "74ff0cee-6593-482f-a62f-6dde6e17ab5e";

	/// <summary>The designer-built fixture of the parity case.</summary>
	private const string DesignerFixtureName = "UsrSendChatDesignerFixture";

	#region Methods: Tests - create and describe

	[Test]
	[Description("TC-I-01: a create builds a Send chat message element in template mode - a constant active channel, the contact from a process parameter, a chat template by name, ignoreErrors on - and describe reads the block back in its write shape: from as the channel id, to as the process parameter, messageSource template, the template with its name, ignoreErrors true, no message, both outputs listed. Re-applying the described block through setElement changes nothing.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process builds a template-mode chat element and describe reads it back re-appliably")]
	public async Task CreateBusinessProcess_Should_BuildATemplateModeChatElement_AndDescribeItBackReappliably() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		(string channelId, _) = await ActiveChannelAsync(context);
		string template = await TemplateNameOfTypeAsync(context, ChatTemplateTypeId, "a message template of type Chat template");
		List<string> created = [];
		try {
			string processName = NewProcessName("Template");
			string descriptor = LinearDescriptor(processName, ContactParameterJson, ChatElementJson("Chat1", $$"""
				{ "from": { "value": "{{channelId}}" }, "to": { "processParameter": "ContactId" },
				  "messageSource": "template", "template": {{JsonSerializer.Serialize(template)}}, "ignoreErrors": true }
				"""), "[]");

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName, descriptor);

			// Assert
			ShouldHaveBuilt(callResult, "an active channel, a mapped contact and a chat template make an ordinary create");
			JsonObject element = Element(await DescribeGraphAsync(context, processName), "Chat1");
			Text(element, "buildType").Should().Be(ChatBuildType, because: "describe reports the element's build token");
			JsonObject block = ChatBlock(element);
			(Text(Group(block, "from"), "value") ?? string.Empty).Should().Contain(channelId,
				because: "a constant channel reads back by its id");
			Text(Group(block, "to"), "processParameter").Should().Be("ContactId",
				because: "a contact mapped from a process parameter reads back as that parameter");
			Text(block, "messageSource").Should().Be("template", because: "the message source reads back");
			Text(block, "templateName").Should().Be(template, because: "describe names the stored template");
			block["ignoreErrors"]!.GetValue<bool>().Should().BeTrue(because: "the stored flag reads back");
			block.ContainsKey("message").Should().BeFalse(because: "template mode reports no message");
			(block["outputs"] as JsonArray ?? []).Select(output => output!.GetValue<string>()).Should()
				.Contain(["SendStatus", "SendStatusDetails"], because: "both outputs are listed even when unmapped");
			CallToolResult replayed = await ModifyAsync(context, processName,
				$$"""[ { "op": "setElement", "elementName": "Chat1", "elementUpdate": { "chatMessage": {{block.ToJsonString()}} } } ]""");
			replayed.IsError.Should().NotBeTrue(because: "a described block re-applies through setElement");
			LogText(replayed).Should().NotContainEquivalentOf("is not a key",
				because: "every member describe reports is either a write key or a tolerated read-back");
			JsonObject after = ChatBlock(Element(await DescribeGraphAsync(context, processName), "Chat1"));
			JsonNode.DeepEquals(after, block).Should().BeTrue(
				because: $"re-applying the described block changes nothing; before {block.ToJsonString()}, after {after.ToJsonString()}");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-02: a create builds a Send chat message element in custom mode with a plain message and ignoreErrors omitted; describe reads the message back and states ignoreErrors false - the create default, under which a failed send stops the process.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process builds a custom-mode chat element with ignoreErrors stated false")]
	public async Task CreateBusinessProcess_Should_BuildACustomModeChatElement_WithIgnoreErrorsStatedFalse() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		(string channelId, _) = await ActiveChannelAsync(context);
		List<string> created = [];
		try {
			string processName = NewProcessName("Custom");
			string descriptor = LinearDescriptor(processName, ContactParameterJson, ChatElementJson("Chat1", $$"""
				{ "from": { "value": "{{channelId}}" }, "to": { "processParameter": "ContactId" },
				  "messageSource": "custom", "message": "Your appointment is confirmed." }
				"""), "[]");

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName, descriptor);

			// Assert
			ShouldHaveBuilt(callResult, "a custom message with a channel and a contact is an ordinary create");
			JsonObject block = ChatBlock(Element(await DescribeGraphAsync(context, processName), "Chat1"));
			Text(block, "messageSource").Should().Be("custom", because: "the message source reads back");
			Text(block, "message").Should().Be("Your appointment is confirmed.",
				because: "a custom message reads back in the profile culture");
			block["ignoreErrors"]!.GetValue<bool>().Should().BeFalse(
				because: "an omitted ignoreErrors is written false on create and always stated");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	// The refusals are the package's own wording (or clio's, for the list); each case asserts only what the caller
	// needs: the name sent and where to go instead.
	[TestCase("""{ "name": "Chat1", "type": "userTask", "userTaskName": "SendChatMessageUserTask" }""",
		new[] { "sendChatMessage" }, false, TestName = "Generic route")]
	[TestCase("""{ "name": "Chat1", "type": "sendChatMessage", "chatMessage": { "from": { "expression": "[#SysSettings.UsrClioE2eNoSuchChatChannel#]" }, "to": { "processParameter": "ContactId" }, "messageSource": "custom", "message": "x" } }""",
		new[] { "UsrClioE2eNoSuchChatChannel" }, false, TestName = "Unknown system setting on from")]
	[TestCase("""{ "name": "Chat1", "type": "sendChatMessage", "chatMessage": { "to": [ { "processParameter": "ContactId" }, { "processParameter": "ContactId" } ] } }""",
		new[] { "Nothing was sent", "'chatMessage.to' is a list", "ONE contact" }, false, TestName = "A list of contacts")]
	[TestCase("""{ "name": "Chat1", "type": "sendChatMessage", "chatMessage": { "from": { "value": "{0}" }, "to": { "processParameter": "ContactId" }, "template": "UsrClioE2eNoSuchChatTemplate" } }""",
		new[] { "UsrClioE2eNoSuchChatTemplate" }, true, TestName = "Unknown chat template")]
	[Description("TC-I-03: a generic userTask naming SendChatMessageUserTask, an unknown system setting on from, a list of contacts on to and an unknown chat template are refused - the list by clio's key check before anything is sent, the rest by the server. Nothing is created in any case: describe of the process name answers that it was not found.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process refuses an unusable chat element and creates nothing")]
	public async Task CreateBusinessProcess_Should_RefuseAndCreateNothing(string elementJson, string[] expectedFragments,
			bool needsChannel) {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		string channelId = needsChannel ? (await ActiveChannelAsync(context)).Id : string.Empty;
		List<string> created = [];
		try {
			string processName = NewProcessName("Refuse");
			string descriptor = LinearDescriptor(processName, ContactParameterJson,
				elementJson.Replace("{0}", channelId, StringComparison.Ordinal), "[]");

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName, descriptor);

			// Assert
			ShouldBeRefused(callResult, "an element that cannot send a chat message is refused before anything is saved");
			// The task name is quoted back by any refusal of the generic route, so it is removed before looking for the
			// type the refusal points to; otherwise "sendChatMessage" would match inside it.
			string log = LogText(callResult).Replace(ChatTaskName, string.Empty, StringComparison.Ordinal);
			foreach (string fragment in expectedFragments) {
				log.Should().ContainEquivalentOf(fragment, because: "the refusal names what was sent and what to use instead");
			}
			CallToolResult described = await ProcessDesignerE2EArrange.DescribeAsync(context, processName);
			JsonSerializer.Serialize(described).Should().Contain("was not found",
				because: "a refused build creates nothing, so describing it finds no process");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-04: a message template of type Email template is refused for a chat element, naming the template: only a Chat template renders against the created chat.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process refuses an email template on a chat element")]
	public async Task CreateBusinessProcess_Should_RefuseAnEmailTypedTemplate() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		(string channelId, _) = await ActiveChannelAsync(context);
		string emailTemplate = await TemplateNameOfTypeAsync(context, EmailTemplateTypeId, "a message template of type Email template");
		List<string> created = [];
		try {
			string processName = NewProcessName("EmailTpl");
			string descriptor = LinearDescriptor(processName, ContactParameterJson, ChatElementJson("Chat1", $$"""
				{ "from": { "value": "{{channelId}}" }, "to": { "processParameter": "ContactId" },
				  "template": {{JsonSerializer.Serialize(emailTemplate)}} }
				"""), "[]");

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName, descriptor);

			// Assert
			ShouldBeRefused(callResult, "an email template cannot be sent as a chat message");
			LogText(callResult).Should().Contain(emailTemplate, because: "the refusal names the template sent");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	#endregion

	#region Methods: Tests - modify

	[Test]
	[Description("TC-I-05: setElement switches a template-mode chat element to custom with the new message in the same update, and describe then reports custom mode with that message and no template; a switch to custom WITHOUT a message is refused, because the designer leaves the template body with its macros in Message and the run would send them as literal text.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process switches a chat element to custom only with the new message")]
	public async Task ModifyBusinessProcess_Should_SwitchToCustom_OnlyWithTheNewMessage() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		(string channelId, _) = await ActiveChannelAsync(context);
		string template = await TemplateNameOfTypeAsync(context, ChatTemplateTypeId, "a message template of type Chat template");
		List<string> created = [];
		try {
			string processName = NewProcessName("Switch");
			await ArrangeProcessAsync(context, created, processName, LinearDescriptor(processName, ContactParameterJson,
				ChatElementJson("Chat1", $$"""
					{ "from": { "value": "{{channelId}}" }, "to": { "processParameter": "ContactId" },
					  "template": {{JsonSerializer.Serialize(template)}} }
					"""), "[]"), "the template-mode chat element");

			// Act
			CallToolResult withoutMessage = await ModifyAsync(context, processName,
				"""[ { "op": "setElement", "elementName": "Chat1", "elementUpdate": { "chatMessage": { "messageSource": "custom" } } } ]""");
			CallToolResult withMessage = await ModifyAsync(context, processName,
				"""[ { "op": "setElement", "elementName": "Chat1", "elementUpdate": { "chatMessage": { "messageSource": "custom", "message": "Switched." } } } ]""");

			// Assert
			ShouldBeRefused(withoutMessage, "a switch to custom must carry the text it sends");
			LogText(withoutMessage).Should().Contain("'chatMessage.message'",
				because: "the refusal names the member that is missing by its path in the block");
			ShouldHaveLanded(withMessage, "a switch to custom with its message is an ordinary update");
			JsonObject block = ChatBlock(Element(await DescribeGraphAsync(context, processName), "Chat1"));
			Text(block, "messageSource").Should().Be("custom", because: "the switch landed");
			Text(block, "message").Should().Be("Switched.", because: "the new message was written");
			block.ContainsKey("template").Should().BeFalse(because: "the switch clears the template, as the designer does");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-06: a raw addMapping onto the chat element's Template parameter is refused, naming the chatMessage member that sets it: a raw lookup value is stored in a shape the designer card shows empty and skips the chat-template type check.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process refuses a raw mapping onto a block-owned chat parameter")]
	public async Task ModifyBusinessProcess_Should_RefuseARawMappingOntoTheTemplate() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		(string channelId, _) = await ActiveChannelAsync(context);
		List<string> created = [];
		try {
			string processName = NewProcessName("Guard");
			await ArrangeProcessAsync(context, created, processName, LinearDescriptor(processName, ContactParameterJson,
				ChatElementJson("Chat1", $$"""
					{ "from": { "value": "{{channelId}}" }, "to": { "processParameter": "ContactId" },
					  "messageSource": "custom", "message": "x" }
					"""), "[]"), "the custom-mode chat element");

			// Act
			CallToolResult callResult = await ModifyAsync(context, processName, $$"""
				[ { "op": "addMapping", "mapping": { "elementName": "Chat1", "elementParameter": "Template",
				    "value": "{{Guid.NewGuid()}}" } } ]
				""");

			// Assert
			ShouldBeRefused(callResult, "the Template parameter is owned by the chatMessage block");
			LogText(callResult).Should().Contain("chatMessage",
				because: "the refusal points to the block member that sets the parameter (case-sensitive: the task name "
					+ "SendChatMessageUserTask does not match)");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	#endregion

	#region Methods: Tests - validation, parity and run time

	[Test]
	[Description("TC-I-07: validate-process-graph over a described Send chat message element: the element's build type is read from describe and validated in a Start -> element -> End graph, which validates with no UNKNOWN finding.")]
	[AllureTag(ValidateProcessGraphTool.ToolName)]
	[AllureName("validate-process-graph accepts a described chat element")]
	public async Task ValidateProcessGraph_Should_AcceptADescribedChatElement() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		(string channelId, _) = await ActiveChannelAsync(context);
		List<string> created = [];
		try {
			string processName = NewProcessName("Validate");
			await ArrangeProcessAsync(context, created, processName, LinearDescriptor(processName, ContactParameterJson,
				ChatElementJson("Chat1", $$"""
					{ "from": { "value": "{{channelId}}" }, "to": { "processParameter": "ContactId" },
					  "messageSource": "custom", "message": "x" }
					"""), "[]"), "the process with a chat element");
			string buildType = Text(Element(await DescribeGraphAsync(context, processName), "Chat1"), "buildType") ?? string.Empty;

			// Act
			CallToolResult callResult = await ProcessDesignerE2EArrange.CallToolAsync(context, ValidateProcessGraphTool.ToolName,
				new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName,
					["nodes"] = new[] { GraphNode("s", "startEvent"), GraphNode("c", buildType), GraphNode("e", "endEvent") },
					["edges"] = new[] { GraphEdge("s", "c"), GraphEdge("c", "e") }
				});

			// Assert
			buildType.Should().Be(ChatBuildType,
				because: "the node type validated is the one describe reported, so the graph is the described one");
			ValidateProcessGraphResponse response =
				EntitySchemaStructuredResultParser.Extract<ValidateProcessGraphResponse>(callResult);
			response.Success.Should().BeTrue(because: "the graph is well formed; the error was: {0}", response.Error);
			(response.Findings ?? []).Where(finding => finding.RuleId == "UNKNOWN").Should().BeEmpty(
				because: "sendchatmessage is a known build type");
			response.HasErrors.Should().NotBe(true, because: "Start -> Send chat message -> End violates no connection rule");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-08: read-only parity - describe of the designer-built process UsrSendChatDesignerFixture reports its chat element's block, and re-applying that block to a server-built copy of the element reads back the same block - so what the designer stores and what the builder writes describe alike.")]
	[AllureTag(DescribeProcessTool.ToolName)]
	[AllureName("describe reads a designer-built chat element in the same shape the builder writes")]
	public async Task DescribeProcess_Should_ReadTheDesignerFixture_InTheBuildersShape() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		await IgnoreUnlessDesignerFixtureExistsAsync(context);
		List<string> created = [];
		try {
			JsonObject designerElement = (await DescribeGraphAsync(context, DesignerFixtureName))["elements"]!.AsArray()
				.OfType<JsonObject>().Should().ContainSingle(element => Text(element, "buildType") == ChatBuildType,
					because: "the fixture holds exactly one Send chat message element").Which;
			JsonObject designerBlock = ChatBlock(designerElement);
			string processName = NewProcessName("Parity");

			// Act
			await ArrangeProcessAsync(context, created, processName, LinearDescriptor(processName, ContactParameterJson,
				ChatElementJson("Chat1", designerBlock.ToJsonString()), "[]"), "the server-built copy of the fixture's element");
			JsonObject builtBlock = ChatBlock(Element(await DescribeGraphAsync(context, processName), "Chat1"));

			// Assert
			JsonNode.DeepEquals(Comparable(builtBlock), Comparable(designerBlock)).Should().BeTrue(
				because: $"the builder writes what the designer stores; designer {designerBlock.ToJsonString()}, built {builtBlock.ToJsonString()}");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[TestCase(true, "completed")]
	[TestCase(false, "error")]
	[Description("TC-I-09: a send that fails at run time - the contact parameter left empty, so the element's own input check fails without reaching any provider - reaches the end of the process with SendStatus = Error when ignoreErrors is true, and ends the instance in Error when it is false. Ignored without McpE2E:AllowDestructiveMcpTests.")]
	[AllureTag(RunProcessTool.ToolName)]
	[AllureName("run-process continues past a failed send only when errors are ignored")]
	public async Task RunProcess_Should_HonourIgnoreErrors_WhenTheSendFails(bool ignoreErrors, string expectedStatus) {
		// Arrange
		IgnoreUnlessDestructiveTestsAreAllowed("the ignore-errors run case");
		await using ProcessDesignerArrangeContext context = await StartAsync();
		(string channelId, _) = await ActiveChannelAsync(context);
		List<string> created = [];
		try {
			string processName = NewProcessName(ignoreErrors ? "Ignore" : "Strict");
			await ArrangeProcessAsync(context, created, processName, LinearDescriptor(processName, RunParametersJson,
				ChatElementJson("Chat1", $$"""
					{ "from": { "value": "{{channelId}}" }, "to": { "processParameter": "ContactId" },
					  "messageSource": "custom", "message": "Clio e2e", "ignoreErrors": {{(ignoreErrors ? "true" : "false")}} }
					"""),
				"""[ { "targetProcessParameter": "SendResult", "sourceElement": "Chat1", "sourceElementParameter": "SendStatus" } ]"""),
				"the process that sends to no contact");

			// Act
			RunProcessEnvelope run = await RunAsync(context, processName, ignoreErrors ? ["SendResult"] : []);

			// Assert
			run.Status.Should().Be(expectedStatus,
				because: (ignoreErrors
					? "an ignored send failure lets the instance reach the end event"
					: "an unignored send failure ends the instance in Error") + " (run error: {0})", run.Error);
			if (ignoreErrors) {
				Convert.ToString(run.ResultParameterValues?.GetValueOrDefault("SendResult")).Should().Be("Error",
					because: "SendStatus reports the failure the run ignored");
			}
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	#endregion

	#region Methods: Descriptors

	// The process parameter the contact is mapped from.
	private const string ContactParameterJson = """
		[ { "name": "ContactId", "type": "Lookup", "referenceSchema": "Contact", "direction": "In" } ]
		""";

	// The run-time case: the contact parameter (left empty) and the parameter SendStatus is read into.
	private const string RunParametersJson = """
		[ { "name": "ContactId", "type": "Lookup", "referenceSchema": "Contact", "direction": "In" },
		  { "name": "SendResult", "type": "Text", "direction": "Out" } ]
		""";

	/// <summary>A Send chat message element with the given <c>chatMessage</c> block.</summary>
	private static string ChatElementJson(string name, string blockJson) =>
		new JsonObject {
			["name"] = name,
			["type"] = "sendChatMessage",
			["caption"] = name,
			["chatMessage"] = JsonNode.Parse(blockJson)
		}.ToJsonString();

	/// <summary>Start -> the element -> End, with the given descriptor-level mappings.</summary>
	private static string LinearDescriptor(string processName, string parametersJson, string elementJson,
			string mappingsJson) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Send chat message E2E",
		  "packageName": "Custom",
		  "parameters": {{parametersJson}},
		  "elements": [ { "name": "StartEvent1", "type": "startEvent" }, {{elementJson}}, { "name": "EndEvent1", "type": "endEvent" } ],
		  "flows": [ { "source": "StartEvent1", "target": "Chat1" }, { "source": "Chat1", "target": "EndEvent1" } ],
		  "mappings": {{mappingsJson}}
		}
		""";

	private static Dictionary<string, object?> GraphNode(string name, string type) =>
		new() { ["name"] = name, ["type"] = type };

	private static Dictionary<string, object?> GraphEdge(string source, string target) =>
		new() { ["source"] = source, ["target"] = target, ["flow-kind"] = "sequence" };

	/// <summary>The block without the describe-only members that legitimately differ between two elements.</summary>
	private static JsonObject Comparable(JsonObject block) {
		JsonObject copy = block.DeepClone().AsObject();
		copy.Remove("issues");
		// display is the designer's caption of the bound parameter or record; the copy's parameter has none of its own.
		foreach (string member in new[] { "from", "to" }) {
			(copy[member] as JsonObject)?.Remove("display");
		}
		return copy;
	}

	#endregion

	#region Methods: Arrange and cleanup

	/// <summary>
	/// Opens a session, or ignores the test when the stand is missing, unreachable, behind the cut, or lacks the
	/// OmnichannelMessaging package that ships the chat task.
	/// </summary>
	private static async Task<ProcessDesignerArrangeContext> StartAsync() {
		ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync(Subject, MinimumPackageVersion, TimeSpan.FromMinutes(8));
		JsonElement rows = await SelectInlineAsync(context, SelectQueryHelper.BuildSelectQuery("SysSchema",
			[new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")],
			[new SelectQueryHelper.SelectQueryFilterDefinition("Name", ChatTaskName, SelectQueryHelper.TextDataValueType)],
			rowCount: 1), $"the {ChatTaskName} schema row");
		if (rows.GetArrayLength() == 0) {
			Assert.Ignore($"{ChatTaskName} not found on '{context.EnvironmentName}': install OmnichannelMessaging to run "
				+ $"the {Subject} cases.");
		}
		return context;
	}

	/// <summary>An active chat channel of the stand; ignores the test, with what to create, when there is none.</summary>
	private static async Task<(string Id, string Name)> ActiveChannelAsync(ProcessDesignerArrangeContext context) {
		JsonElement rows = await SelectInlineAsync(context, SelectQueryHelper.BuildSelectQuery("Channel",
			[
				new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id"),
				new SelectQueryHelper.SelectQueryColumnDefinition("Name", "Name"),
				new SelectQueryHelper.SelectQueryColumnDefinition("IsActive", "IsActive")
			], [], rowCount: 500), "the chat channels");
		JsonElement? active = rows.EnumerateArray()
			.Where(row => row.TryGetProperty("IsActive", out JsonElement flag) && flag.ValueKind == JsonValueKind.True)
			.Select(row => (JsonElement?)row).FirstOrDefault();
		if (active is null) {
			Assert.Ignore($"No active Channel on '{context.EnvironmentName}'. Add one in the chat settings (an SMS "
				+ "provider channel without messenger settings is enough for the build cases).");
		}
		return (active!.Value.GetProperty("Id").GetString()!, active.Value.GetProperty("Name").GetString()!);
	}

	/// <summary>The name of a message template of the given type; ignores the test when the stand has none.</summary>
	private static async Task<string> TemplateNameOfTypeAsync(ProcessDesignerArrangeContext context, string typeId,
			string what) {
		JsonElement rows = await SelectInlineAsync(context, SelectQueryHelper.BuildSelectQuery("EmailTemplate",
			[new SelectQueryHelper.SelectQueryColumnDefinition("Name", "Name")],
			[new SelectQueryHelper.SelectQueryFilterDefinition("TemplateType", typeId, SelectQueryHelper.GuidDataValueType)],
			rowCount: 50), what);
		List<string> names = [.. rows.EnumerateArray().Select(row => row.GetProperty("Name").GetString())
			.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name!)];
		// A name shared by two templates of the type is refused as ambiguous, so take one that is unique.
		string? unique = names.GroupBy(name => name, StringComparer.Ordinal).Where(group => group.Count() == 1)
			.Select(group => group.Key).FirstOrDefault();
		if (unique is null) {
			Assert.Ignore($"No uniquely named {what} on '{context.EnvironmentName}'. Create one in Message templates.");
		}
		return unique!;
	}

	/// <summary>A process code unique to this run, in the <c>UsrClioBpChat&lt;Case&gt;E2e</c> family.</summary>
	private static string NewProcessName(string caseName) => $"UsrClioBpChat{caseName}E2e{Guid.NewGuid():N}";

	/// <summary>
	/// Builds a process, recording its name for cleanup BEFORE the call: a build the server saved but whose answer
	/// never arrived must still be removed. Ignores the test when clio refused the call because the stand's
	/// CrtProcessBuilder is behind.
	/// </summary>
	private static async Task<CallToolResult> CreateAsync(ProcessDesignerArrangeContext context,
			List<string> created, string processName, string descriptor) {
		created.Add(processName);
		CallToolResult result = await ProcessDesignerE2EArrange.CallToolAsync(context, CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = descriptor
			});
		IgnoreWhenProcessBuilderIsBehind(ToolText(result), MinimumPackageVersion);
		return result;
	}

	/// <summary>Builds a process for an ARRANGE step and fails the test on the spot unless it was created.</summary>
	private static async Task ArrangeProcessAsync(ProcessDesignerArrangeContext context, List<string> created,
			string processName, string descriptor, string what) {
		CallToolResult result = await CreateAsync(context, created, processName, descriptor);
		ShouldHaveBuilt(result, $"{what} is an arrange step, so every assertion below is about the wrong failure "
			+ "if it was not built");
	}

	private static async Task<CallToolResult> ModifyAsync(ProcessDesignerArrangeContext context, string processName,
			string operations) {
		CallToolResult result = await ProcessDesignerE2EArrange.CallToolAsync(context, ModifyToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = operations
			});
		IgnoreWhenProcessBuilderIsBehind(ToolText(result), MinimumPackageVersion);
		return result;
	}

	/// <summary>Reads a process back in full and returns its graph.</summary>
	private static async Task<JsonObject> DescribeGraphAsync(ProcessDesignerArrangeContext context,
			string processName) =>
		DescribedProcessGraph.Read(await ProcessDesignerE2EArrange.DescribeAsync(context, processName));

	/// <summary>Removes every process a test built, newest first; a failed delete is reported, not thrown.</summary>
	private static async Task DeleteProcessesAsync(ProcessDesignerArrangeContext context, List<string> created) {
		for (int index = created.Count - 1; index >= 0; index--) {
			await DeleteProcessAsync(context.EnvironmentName, created[index]);
		}
	}

	private static void IgnoreUnlessDestructiveTestsAreAllowed(string what) {
		if (!TestConfiguration.Load().AllowDestructiveMcpTests) {
			Assert.Ignore($"Set McpE2E:AllowDestructiveMcpTests=true to run {what}.");
		}
	}

	/// <summary>Ignores the test, with the build instructions, when the stand does not carry the designer fixture.</summary>
	private static async Task IgnoreUnlessDesignerFixtureExistsAsync(ProcessDesignerArrangeContext context) {
		JsonElement rows = await SelectInlineAsync(context, SelectQueryHelper.BuildSelectQuery("SysSchema",
			[new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")],
			[
				new SelectQueryHelper.SelectQueryFilterDefinition("Name", DesignerFixtureName,
					SelectQueryHelper.TextDataValueType),
				new SelectQueryHelper.SelectQueryFilterDefinition("ManagerName", "ProcessSchemaManager",
					SelectQueryHelper.TextDataValueType)
			], rowCount: 1), "the designer fixture's SysSchema row");
		if (rows.GetArrayLength() == 0) {
			Assert.Ignore($"Designer fixture {DesignerFixtureName} not found on '{context.EnvironmentName}'. Build it in "
				+ "the classic designer: one Send chat message element - From an active channel, To a Lookup process "
				+ "parameter on Contact named ContactId, template mode with a chat template, Ignore sending errors ticked - and save.");
		}
	}

	/// <summary>
	/// Runs the process and waits for it. <paramref name="resultParameters"/> must be parameters the process declares:
	/// run-process refuses an unknown one before starting anything.
	/// </summary>
	private static async Task<RunProcessEnvelope> RunAsync(ProcessDesignerArrangeContext context, string processName,
			string[] resultParameters) {
		Dictionary<string, object?> args = new() {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["wait-timeout"] = 60
		};
		if (resultParameters.Length > 0) {
			args["result-parameters"] = resultParameters;
		}
		CallToolResult result = await context.Session.CallToolAsync(ClioRunTool.ToolName,
			new Dictionary<string, object?> { ["command"] = RunProcessTool.ToolName, ["args"] = args },
			context.CancellationTokenSource.Token);
		return EntitySchemaStructuredResultParser.Extract<RunProcessEnvelope>(result);
	}

	#endregion

	#region Methods: Graph reading

	/// <summary>The described <c>chatMessage</c> block of an element; absent fails the test.</summary>
	private static JsonObject ChatBlock(JsonObject element) => Group(element, "chatMessage");

	private static JsonObject Element(JsonObject graph, string name) =>
		(graph["elements"] as JsonArray ?? []).OfType<JsonObject>()
			.Should().ContainSingle(element => Text(element, "name") == name,
				because: $"the process carries one element named {name}").Which;

	private static JsonObject Group(JsonObject owner, string key) {
		owner[key].Should().BeOfType<JsonObject>(
			because: $"'{key}' must be reported as an object; what was read: {{0}}", owner.ToJsonString());
		return owner[key]!.AsObject();
	}

	#endregion

}
