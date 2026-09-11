using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for <c>modify-business-process-as-new-version</c>. NOT in CI — run manually. The
/// advertised-tool test is hermetic; the functional tests build a uniquely named process and then save
/// versions of it, gated on a reachable environment carrying CrtProcessBuilder 1.6.2.1 or newer and a
/// writable <c>Custom</c> package.
/// </summary>
/// <remarks>
/// Every process these tests create is PERMANENT on the target environment, and so is every version: the
/// platform has no delete for a version, and removing the root cancels every <c>SysProcessLog</c> row of the
/// schema. That is why the names are Guid-suffixed and why this fixture belongs on a disposable sandbox
/// rather than on a stand anyone else uses.
/// </remarks>
[TestFixture]
[AllureNUnit]
[AllureFeature(ModifyProcessAsNewVersionTool.ModifyProcessAsNewVersionToolName)]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class ModifyProcessAsNewVersionToolE2ETests {

	private const string ToolName = ModifyProcessAsNewVersionTool.ModifyProcessAsNewVersionToolName;
	private const string CreateToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;
	private const string DescribeToolName = DescribeProcessTool.ToolName;

	[Test]
	[Description("Starts the real clio MCP server and verifies modify-business-process-as-new-version is discoverable via the get-tool-contract compact index (hermetic).")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process-as-new-version is discoverable on the lazy surface of the clio MCP server")]
	public async Task ModifyProcessAsNewVersion_Should_Be_Advertised_By_Mcp_Server() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: false);

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: $"the {ToolName} MCP tool must be discoverable on the lazy surface (get-tool-contract "
				+ "compact index) even though it is not resident in tools/list");
	}

	[Test]
	[Description("Over the real MCP path, builds a process and then saves an EDITED copy of it as a new version, then reads the family back through describe-business-process to confirm the new member exists, is inactive, and carries the edit.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process-as-new-version creates an inactive version carrying the edit")]
	public async Task ModifyProcessAsNewVersion_Should_CreateAnInactiveVersion_CarryingTheEdit() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpVersionE2e{Guid.NewGuid():N}";
		await CallToolExpectingSuccessAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildDescriptor(processName)
		});

		// Act
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["package-name"] = "Custom",
			["operations"] = BuildAddParameterOperations()
		});

		// Assert
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain("\\u0022exit-code\\u0022:0",
			because: "the transport reports isError:null for a tool that ran and failed, so the exit-code is "
				+ "what actually says the version was saved");
		callResultJson.Should().Contain("Version 1",
			because: "the platform allocates the number and the tool reports the one it got back — the first "
				+ "version in a package is 1, because the root's own stamp is not part of the count");
		callResultJson.Should().Contain($"{processName}Custom1",
			because: "the name is composed SERVER-side as root + package + number, so this is what proves clio "
				+ "reported the platform's name rather than one it guessed");
		callResultJson.Should().Contain("still the actual one",
			because: "the caller must be told on EVERY success that what the environment runs did not change — "
				+ "an agent that assumes otherwise stops one step early");
		callResultJson.Should().Contain(CommandExecutionResult.CompileNotRequiredNote,
			because: "a saved version stays interpreted and needs no compile (ENG-95706)");

		// Reading the family back is the only proof the version really exists and really carries the edit: a
		// server that answered success while saving nothing would satisfy every assertion above.
		//
		// Parsed rather than substring-matched: the graph is a JSON string nested inside the envelope, so
		// System.Text.Json escapes each of its quotes — which is why the exit-code assertion above searches for
		// \\u0022 — and a Contain("\\"isActiveVersion\\": false") looks for a sequence that cannot occur.
		CallToolResult describeResult = await CallToolAsync(context, DescribeToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = $"{processName}Custom1"
			});
		JsonSerializer.Serialize(describeResult).Should().Contain("RecordId",
			because: "the operation was applied to the CLONE, so the parameter must be present on the version");
		JsonObject describedVersion = DescribedProcessGraph.Read(describeResult);
		describedVersion["isActiveVersion"]!.GetValue<bool>().Should().BeFalse(
			because: "creating a version must never change what the environment executes");
		describedVersion["version"]!.GetValue<int>().Should().Be(1,
			because: "the family the environment reports must agree with the number the tool reported, which "
				+ "is what separates a saved version from a response that merely claimed one");
	}

	[Test]
	[Description("Over the real MCP path, an OMITTED operations array is accepted and produces a plain snapshot of the source as a new version — the gesture an agent uses to take a restore point before editing in place.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process-as-new-version snapshots the source when no operations are given")]
	public async Task ModifyProcessAsNewVersion_Should_SnapshotTheSource_WhenNoOperationsGiven() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpSnapshotE2e{Guid.NewGuid():N}";
		await CallToolExpectingSuccessAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildDescriptor(processName)
		});

		// Act
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["package-name"] = "Custom"
		});

		// Assert
		JsonSerializer.Serialize(callResult).Should().Contain("\\u0022exit-code\\u0022:0",
			because: "an edit-free version is a legal request, not a validation error");
		JsonSerializer.Serialize(callResult).Should().Contain("0 operation(s) applied",
			because: "the snapshot applied nothing, and the count has to say so rather than be omitted");
	}

	[Test]
	[Description("Over the real MCP path, supplying neither process-name nor process-uid is refused by the tool itself, naming both options, without reaching the environment.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process-as-new-version refuses a request with no source identity")]
	public async Task ModifyProcessAsNewVersion_Should_Refuse_WhenNoSourceIdentityGiven() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: false);

		// Act
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = "any-environment",
			["operations"] = "[]"
		});

		// Assert
		JsonSerializer.Serialize(callResult).Should().Contain("process-name",
			because: "the refusal must name WHICH rule was broken; a bare failure sends the agent guessing");
	}

	#region Methods: Private

	[Test]
	[Description("Over the real MCP path, a setElement carrying email.template in the operations array lands on the NEW VERSION (ENG-95986): the version's sendEmail element describes as messageSource 'template' with the template id and display name and no body. This route runs no read-back guard, so this describe is the only evidence clio can produce that the template member is not silently discarded on the way to a version. Depends on the stock template 'Case closure notification'.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process-as-new-version carries a template-mode sendEmail into the new version")]
	public async Task ModifyProcessAsNewVersion_Should_CarryATemplateModeEmailIntoTheNewVersion() {
		// Arrange - a custom-message sendEmail element on the source process
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpVersionEmailTplE2e{Guid.NewGuid():N}";
		await CallToolExpectingSuccessAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildSendEmailDescriptor(processName)
		});

		// Act - switch the element to a template ON THE VERSION (one without a macro-source object, so no templateEntity)
		CallToolResult callResult = await CallToolExpectingSuccessAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["package-name"] = "Custom",
			["operations"] = """
				[ { "op": "setElement", "elementName": "SendEmail1",
				    "elementUpdate": { "email": { "template": "Case closure notification" } } } ]
				"""
		});

		// Assert
		JsonSerializer.Serialize(callResult).Should().Contain($"{processName}Custom1",
			because: "the version must have been saved under the platform-composed name before its content can be checked");
		CallToolResult describeResult = await CallToolAsync(context, DescribeToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = $"{processName}Custom1"
			});
		JsonObject describedVersion = DescribedProcessGraph.Read(describeResult);
		JsonObject sendEmail = describedVersion["elements"]!.AsArray()
			.Select(element => element!.AsObject())
			.Single(element => element["name"]!.GetValue<string>() == "SendEmail1");
		JsonObject email = sendEmail["email"]!.AsObject();
		email["messageSource"]!.GetValue<string>().Should().Be("template",
			because: "the template member must reach the version's element - this route has no read-back warning to catch a drop");
		email["template"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace(
			because: "the resolved template id is stored on the version's EmailTemplateId");
		email["templateDisplay"]!.GetValue<string>().Should().Be("Case closure notification",
			because: "the template NAME is stored as the lookup's display value");
		(email["hasBody"]?.GetValue<bool>() ?? false).Should().BeFalse(
			because: "the custom body is cleared on the switch and a template element reports no body");
		describedVersion["isActiveVersion"]!.GetValue<bool>().Should().BeFalse(
			because: "creating a version must never change what the environment executes");
	}

	// A source process with a custom-message sendEmail element, so the version edit has a mode to switch FROM.
	private static string BuildSendEmailDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Version Email Template E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "SendEmail1", "type": "sendEmail",
		      "email": { "mode": "manual", "subject": "Version template probe", "body": "<p>ClioVersionTemplateProbe</p>",
		        "to": [ { "value": "probe@example.com" } ] } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "SendEmail1" },
		    { "source": "SendEmail1", "target": "EndEvent1" }
		  ]
		}
		""";

	private static string BuildDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Version E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "task1", "type": "performTask" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "task1" },
		    { "source": "task1", "target": "EndEvent1" }
		  ]
		}
		""";

	// An addParameter edit rather than a structural one: it survives the layout pass unchanged and shows up in
	// describe under a name this test can look for, so a server that saved an UNEDITED clone is still caught.
	private static string BuildAddParameterOperations() =>
		"""
		[
		  { "op": "addParameter", "parameter": { "name": "RecordId", "type": "Guid", "direction": "In", "caption": "Record Id" } }
		]
		""";

	// The MCP transport reports isError:null for a tool that ran and FAILED - the failure lives in the
	// payload's exit-code. An arrange step checked only for a transport error therefore "succeeds" against
	// a stand where nothing was created, and the act step then fails pointing at the wrong cause.
	private static async Task<CallToolResult> CallToolExpectingSuccessAsync(ArrangeContext context,
		string toolName, Dictionary<string, object?> args) {
		CallToolResult result = await CallToolAsync(context, toolName, args);
		JsonSerializer.Serialize(result).Should().Contain("\\u0022exit-code\\u0022:0",
			because: $"{toolName} had to succeed for the rest of this test to mean anything");
		return result;
	}

	private static async Task<CallToolResult> CallToolAsync(ArrangeContext context, string toolName,
		Dictionary<string, object?> args) {
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		toolNames.Should().Contain(toolName,
			because: $"the {toolName} tool must be discoverable via the get-tool-contract compact index before "
				+ "the end-to-end call");
		return await context.Session.CallToolAsync(
			toolName, new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
	}

	private static async Task<ArrangeContext> ArrangeAsync(bool requireReachableEnvironment) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (requireReachableEnvironment) {
			if (string.IsNullOrWhiteSpace(environmentName)) {
				Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName (carrying CrtProcessBuilder 1.6.2.1 or "
					+ "newer) to run modify-business-process-as-new-version MCP E2E.");
			}
			if (!await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, environmentName!)) {
				Assert.Ignore("modify-business-process-as-new-version MCP E2E requires a reachable configured "
					+ $"sandbox environment. '{environmentName}' was not reachable.");
			}
		}
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource, environmentName);
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		string? EnvironmentName) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}

	#endregion
}
