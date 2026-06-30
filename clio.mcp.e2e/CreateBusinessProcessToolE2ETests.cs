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
/// End-to-end coverage for <c>create-business-process</c>. NOT in CI — run manually. The advertised-tool test
/// is hermetic; the functional test builds a real (uniquely named) process and is gated on a reachable
/// environment with the ProcessDesignService package and a writable "Custom" package.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(CreateBusinessProcessTool.CreateBusinessProcessToolName)]
[NonParallelizable]
public sealed class CreateBusinessProcessToolE2ETests {

	private const string ToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;

	[Test]
	[Description("Starts the real clio MCP server and verifies create-business-process is advertised (hermetic).")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process is advertised by the clio MCP server")]
	public async Task CreateBusinessProcess_Should_Be_Advertised_By_Mcp_Server() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: false);

		// Act
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);

		// Assert
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the create-business-process tool must be discoverable on the real clio MCP server");
	}

	[Test]
	[Description("Over the real MCP path, create-business-process builds a uniquely named process from an inline descriptor.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process builds a process from an inline descriptor")]
	public async Task CreateBusinessProcess_Should_BuildProcess_FromInlineDescriptor() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpE2e{Guid.NewGuid():N}";
		string descriptor = BuildDescriptor(processName);

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = descriptor
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a successful build should return a normal MCP tool result, not a transport error");
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain(processName,
			because: "a successful build reports the created schema name (run against an environment with the ProcessDesignService package and a writable Custom package)");

		// Readback: describe the built process and confirm the structure is really there — a server that
		// returned success but built nothing would be caught here, unlike the success-echo assertion above.
		string describeJson = JsonSerializer.Serialize(await DescribeAsync(context, processName));
		describeJson.Should().Contain("task1",
			because: "the read-back graph must contain the user-task element that was actually built");
		describeJson.Should().Contain("buildType",
			because: "describe returns the structured element graph (buildType tokens), confirming a real build rather than an echo");
	}

	// Reads the built process back as a structured graph via describe-business-process (for build readback).
	private static async Task<CallToolResult> DescribeAsync(ArrangeContext context, string processCode) =>
		await context.Session.CallToolAsync(
			DescribeProcessTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName,
					["process-name"] = processCode
				}
			},
			context.CancellationTokenSource.Token);

	private static string BuildDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP E2E",
		  "packageName": "Custom",
		  "parameters": [ { "name": "MyText", "type": "Text", "direction": "In" } ],
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "task1", "type": "performTask" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "task1" },
		    { "source": "task1", "target": "EndEvent1" }
		  ],
		  "mappings": [
		    { "elementName": "task1", "elementParameter": "Recommendation", "processParameter": "MyText" }
		  ]
		}
		""";

	private static async Task<CallToolResult> CallToolAsync(ArrangeContext context, Dictionary<string, object?> args) {
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the create-business-process tool must be advertised before the end-to-end call");
		return await context.Session.CallToolAsync(
			ToolName, new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
	}

	private static async Task<ArrangeContext> ArrangeAsync(bool requireReachableEnvironment) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = settings.Sandbox.EnvironmentName;
		if (requireReachableEnvironment) {
			if (string.IsNullOrWhiteSpace(environmentName)) {
				Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName (with the ProcessDesignService package) to run create-business-process MCP E2E.");
			}
			if (!await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, environmentName)) {
				Assert.Ignore($"create-business-process MCP E2E requires a reachable configured sandbox environment. '{environmentName}' was not reachable.");
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
