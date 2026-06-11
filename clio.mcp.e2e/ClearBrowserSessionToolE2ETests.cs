using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Story 8 (browser-session-handoff) end-to-end coverage. NOT in CI — run manually against a
/// configured sandbox. The advertised-tool test is hermetic; the clear happy-path is gated on a
/// reachable environment.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(ClearBrowserSessionTool.ToolName)]
[NonParallelizable]
public sealed class ClearBrowserSessionToolE2ETests {

	private const string ToolName = ClearBrowserSessionTool.ToolName;

	[Test]
	[Description("Starts the real clio MCP server and verifies clear-browser-session is advertised in the tool list (hermetic — no Creatio environment required).")]
	[AllureTag(ToolName)]
	[AllureName("clear-browser-session is advertised by the clio MCP server")]
	public async Task ClearBrowserSession_Should_Be_Advertised_By_Mcp_Server() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3), requireReachableEnvironment: false);

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the clear-browser-session tool must be discoverable on the real clio MCP server");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes clear-browser-session against the configured sandbox, and verifies a structured success response (idempotent).")]
	[AllureTag(ToolName)]
	[AllureName("clear-browser-session returns a structured success payload")]
	public async Task ClearBrowserSession_Should_Succeed_For_Reachable_Environment() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3), requireReachableEnvironment: true);

		// Act
		CallToolResult callResult = await CallToolAsync(
			arrangeContext,
			ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName
			});
		ClearBrowserSessionResult response = EntitySchemaStructuredResultParser.Extract<ClearBrowserSessionResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "clear-browser-session should return a structured payload, not an MCP-level error");
		response.Success.Should().BeTrue(
			because: "clearing a session is idempotent and succeeds even when nothing is cached");
		response.Error.Should().BeNull(
			because: "no error message should be present when the tool call succeeds");
	}

	private static async Task<CallToolResult> CallToolAsync(
		ArrangeContext arrangeContext,
		string toolName,
		Dictionary<string, object?> args) {
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(toolName,
			because: "the clear-browser-session tool must be advertised before the end-to-end call can be executed");
		return await arrangeContext.Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> {
				["args"] = args
			},
			arrangeContext.CancellationTokenSource.Token);
	}

	private static async Task<ArrangeContext> ArrangeAsync(
		McpE2ESettings settings,
		TimeSpan timeout,
		bool requireReachableEnvironment) {
		CancellationTokenSource cancellationTokenSource = new(timeout);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		string? environmentName = requireReachableEnvironment
			? await ResolveReachableEnvironmentAsync(settings)
			: settings.Sandbox.EnvironmentName;
		return new ArrangeContext(session, cancellationTokenSource, environmentName);
	}

	private static async Task<string> ResolveReachableEnvironmentAsync(McpE2ESettings settings) {
		string? configuredEnvironmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(configuredEnvironmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName to run clear-browser-session MCP E2E tests.");
		}

		if (!await CanReachEnvironmentAsync(settings, configuredEnvironmentName!)) {
			Assert.Ignore($"clear-browser-session MCP E2E requires a reachable sandbox environment. '{configuredEnvironmentName}' was not reachable.");
		}

		return configuredEnvironmentName!;
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
			settings,
			["ping-app", "-e", environmentName]);
		return result.ExitCode == 0;
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
