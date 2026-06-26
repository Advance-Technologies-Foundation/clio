using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for <c>modify-business-process</c>. NOT in CI — run manually. The advertised-tool test
/// is hermetic; the functional test builds a uniquely named process and then edits it (replace the start event
/// with a record-signal start), gated on a reachable environment with the ProcessDesignService package and a
/// writable "Custom" package.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(ModifyBusinessProcessTool.ModifyBusinessProcessToolName)]
[NonParallelizable]
public sealed class ModifyBusinessProcessToolE2ETests {

	private const string ToolName = ModifyBusinessProcessTool.ModifyBusinessProcessToolName;
	private const string CreateToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;

	[Test]
	[Description("Starts the real clio MCP server and verifies modify-business-process is advertised (hermetic).")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process is advertised by the clio MCP server")]
	public async Task ModifyBusinessProcess_Should_Be_Advertised_By_Mcp_Server() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: false);

		// Act
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);

		// Assert
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the modify-business-process tool must be discoverable on the real clio MCP server");
	}

	[Test]
	[Description("Over the real MCP path, builds a process then edits it (replace start with a record-signal start).")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process edits an existing process")]
	public async Task ModifyBusinessProcess_Should_EditExistingProcess() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpModifyE2e{Guid.NewGuid():N}";
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildDescriptor(processName)
		});

		// Act
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-code"] = processName,
			["operations"] = BuildOperations()
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a successful edit should return a normal MCP tool result, not a transport error");
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain(processName,
			because: "a successful edit reports the edited schema name (run against an environment with the ProcessDesignService package)");

		// Readback: describe the edited process and confirm the signal start really replaced the simple start —
		// a server that returned success but applied nothing would be caught here, unlike the success echo above.
		string describeJson = JsonSerializer.Serialize(await CallToolAsync(context, DescribeProcessTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-code"] = processName
			}));
		describeJson.Should().Contain("signalstart",
			because: "the edit added a signalStart element, which must appear in the read-back structured graph");
	}

	[Test]
	[Description("Over the real MCP path, builds a process then adds process parameters via addParameter, including a Lookup referenceSchema; identifies the process by name only (exercises the optional processUid path).")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process adds parameters including a lookup referenceSchema")]
	public async Task ModifyBusinessProcess_Should_AddParametersIncludingLookup() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpAddParamE2e{Guid.NewGuid():N}";
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildDescriptor(processName)
		});

		// Act — processName only (processUid omitted) also exercises the optional-identity path
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-code"] = processName,
			["operations"] = BuildAddParameterOperations()
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "addParameter (including a Lookup referenceSchema) must succeed over the real MCP path");
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain(processName,
			because: "a successful edit reports the edited schema name (run against an environment with the ProcessDesignService package and a 'City' object)");
	}

	private static string BuildDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Modify E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "id": "StartEvent1", "type": "startEvent" },
		    { "id": "task1", "type": "performTask" },
		    { "id": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "task1" },
		    { "source": "task1", "target": "EndEvent1" }
		  ]
		}
		""";

	private static string BuildOperations() =>
		"""
		[
		  { "op": "removeElement", "elementId": "StartEvent1" },
		  { "op": "addElement", "element": { "id": "SignalStart1", "type": "signalStart", "signal": { "entity": "UsrTestRunButton", "on": "save" } } },
		  { "op": "addFlow", "source": "SignalStart1", "target": "task1" }
		]
		""";

	private static string BuildAddParameterOperations() =>
		"""
		[
		  { "op": "addParameter", "parameter": { "name": "RecordId", "type": "Guid", "direction": "In", "caption": "Record Id" } },
		  { "op": "addParameter", "parameter": { "name": "City", "referenceSchema": "City", "direction": "In" } }
		]
		""";

	private static async Task<CallToolResult> CallToolAsync(ArrangeContext context, string toolName,
		Dictionary<string, object?> args) {
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(toolName,
			because: $"the {toolName} tool must be advertised before the end-to-end call");
		return await context.Session.CallToolAsync(
			toolName, new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
	}

	private static async Task<ArrangeContext> ArrangeAsync(bool requireReachableEnvironment) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = settings.Sandbox.EnvironmentName;
		if (requireReachableEnvironment) {
			if (string.IsNullOrWhiteSpace(environmentName)) {
				Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName (with the ProcessDesignService package) to run modify-business-process MCP E2E.");
			}
			if (!await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, environmentName)) {
				Assert.Ignore($"modify-business-process MCP E2E requires a reachable configured sandbox environment. '{environmentName}' was not reachable.");
			}
		}
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource, environmentName);
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		string EnvironmentName) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}
}
