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
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
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
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class ListUserTasksToolE2ETests {

	private const string ToolName = ListUserTasksTool.ListUserTasksToolName;

	[Test]
	[Description("Starts the real clio MCP server and verifies list-user-tasks is discoverable via the get-tool-contract compact index (hermetic).")]
	[AllureTag(ToolName)]
	[AllureName("list-user-tasks is discoverable on the lazy surface of the clio MCP server")]
	public async Task ListUserTasks_Should_Be_Advertised_By_Mcp_Server() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: false);

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: $"the {ToolName} MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list");
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

	[Test]
	[Description("TC-I-13: over the real MCP path the list-user-tasks contract that get-tool-contract publishes names WebServiceUserTask as REFUSED on the generic userTask route and points to type webService with its webService block, so an agent picking a task from the palette is told before the build refuses it (hermetic).")]
	[AllureTag(ToolName)]
	[AllureName("list-user-tasks publishes the generic-route refusal of WebServiceUserTask")]
	public async Task ListUserTasks_Should_PublishTheGenericRouteRefusalOfWebServiceUserTask() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: false);

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["tool-names"] = new[] { ToolName },
					["detail"] = "full"
				}
			},
			context.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "get-tool-contract should return a normal MCP tool result envelope for a known tool name");
		ToolContractGetResponse response =
			EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(callResult);
		string description = (response.Tools ?? [])
			.Should().ContainSingle(definition => definition.Name == ToolName,
				because: $"the contract request named only {ToolName}")
			.Which.Description;
		description.Should().Contain("WebServiceUserTask (Call web service) is REFUSED on the generic route",
			because: "the palette lists WebServiceUserTask, and the contract must say the generic route refuses it");
		description.Should().Contain("build it as type webService with its webService block",
			because: "the contract points to the element type that builds a Call web service element");
	}

	[Test]
	[Description("Over the real MCP path the list-user-tasks contract that get-tool-contract publishes names SendChatMessageUserTask as REFUSED on the generic userTask route and points to type sendChatMessage with its chatMessage block, so an agent picking the chat task from the palette is told before the build refuses it (hermetic).")]
	[AllureTag(ToolName)]
	[AllureName("list-user-tasks publishes the generic-route refusal of SendChatMessageUserTask")]
	public async Task ListUserTasks_Should_PublishTheGenericRouteRefusalOfSendChatMessageUserTask() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: false);

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["tool-names"] = new[] { ToolName },
					["detail"] = "full"
				}
			},
			context.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "get-tool-contract should return a normal MCP tool result envelope for a known tool name");
		ToolContractGetResponse response =
			EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(callResult);
		string description = (response.Tools ?? [])
			.Should().ContainSingle(definition => definition.Name == ToolName,
				because: $"the contract request named only {ToolName}")
			.Which.Description;
		description.Should().Contain("SendChatMessageUserTask (Send chat message) is REFUSED",
			because: "the palette lists SendChatMessageUserTask, and the contract must say the generic route refuses it");
		description.Should().Contain("build it as type sendChatMessage with its chatMessage block",
			because: "the contract points to the element type that builds a Send chat message element");
	}

	private static async Task<CallToolResult> CallToolAsync(ArrangeContext context, Dictionary<string, object?> args) {
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		toolNames.Should().Contain(ToolName,
			because: "the list-user-tasks tool must be discoverable via the get-tool-contract compact index before the end-to-end call");
		return await context.Session.CallToolAsync(
			ToolName, new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
	}

	private static async Task<ArrangeContext> ArrangeAsync(bool requireReachableEnvironment) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (requireReachableEnvironment) {
			if (string.IsNullOrWhiteSpace(environmentName)) {
				Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName (with the ProcessDesignService package) to run list-user-tasks MCP E2E.");
			}
			if (!await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, environmentName!)) {
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
		string? EnvironmentName) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}
}
