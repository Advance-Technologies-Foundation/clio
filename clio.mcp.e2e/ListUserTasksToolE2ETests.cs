using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for <c>list-user-tasks</c>. NOT in CI — run manually. The advertised-tool test is
/// hermetic; the functional test is gated on a reachable environment with the ProcessDesignService package.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(ListUserTasksTool.ListUserTasksToolName)]
[NonParallelizable]
public sealed class ListUserTasksToolE2ETests {

	private const string ToolName = ListUserTasksTool.ListUserTasksToolName;

	[Test]
	[Description("Starts the real clio MCP server and verifies list-user-tasks is advertised (hermetic).")]
	[AllureTag(ToolName)]
	[AllureName("list-user-tasks is advertised by the clio MCP server")]
	public async Task ListUserTasks_Should_Be_Advertised_By_Mcp_Server() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: false);

		// Act
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);

		// Assert
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the list-user-tasks tool must be discoverable on the real clio MCP server");
	}

	[Test]
	[Description("Over the real MCP path, list-user-tasks returns the user task palette for a reachable environment.")]
	[AllureTag(ToolName)]
	[AllureName("list-user-tasks returns the user task palette of an environment")]
	public async Task ListUserTasks_Should_ReturnPalette_ForReachableEnvironment() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a successful palette read should return a normal MCP tool result, not a transport error");
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain("UserTask",
			because: "the user task palette always includes built-in *UserTask schemas (run against an environment with the ProcessDesignService package)");
	}

	private static async Task<CallToolResult> CallToolAsync(ArrangeContext context, Dictionary<string, object?> args) {
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the list-user-tasks tool must be advertised before the end-to-end call");
		return await context.Session.CallToolAsync(
			ToolName, new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
	}

	private static async Task<ArrangeContext> ArrangeAsync(bool requireReachableEnvironment) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = settings.Sandbox.EnvironmentName;
		if (requireReachableEnvironment) {
			if (string.IsNullOrWhiteSpace(environmentName)) {
				Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName (with the ProcessDesignService package) to run list-user-tasks MCP E2E.");
			}
			if (!await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, environmentName)) {
				Assert.Ignore($"list-user-tasks MCP E2E requires a reachable configured sandbox environment. '{environmentName}' was not reachable.");
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
