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
/// Story 7 (browser-session-handoff) end-to-end coverage. NOT in CI — run manually against a
/// configured sandbox. The advertised-tool test is hermetic (no Creatio needed); the happy-path
/// test is gated on a reachable forms-auth environment.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(GetBrowserSessionTool.ToolName)]
[NonParallelizable]
public sealed class GetBrowserSessionToolE2ETests : McpContractFixtureBase {

	private const string ToolName = GetBrowserSessionTool.ToolName;

	[Test]
	[Description("Starts the real clio MCP server and verifies get-browser-session is discoverable via the get-tool-contract compact index (hermetic — no Creatio environment required).")]
	[AllureTag(ToolName)]
	[AllureName("get-browser-session is discoverable on the lazy surface of the clio MCP server")]
	public async Task GetBrowserSession_Should_Be_Advertised_By_Mcp_Server() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3), requireReachableEnvironment: false);

		// Act
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: $"the {ToolName} MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes get-browser-session against the configured sandbox, and verifies a structured session-file-path is returned with no cookie values.")]
	[AllureTag(ToolName)]
	[AllureName("get-browser-session returns a structured session-file-path payload")]
	public async Task GetBrowserSession_Should_Return_Session_Path_For_Reachable_Environment() {
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
		GetBrowserSessionResult response = EntitySchemaStructuredResultParser.Extract<GetBrowserSessionResult>(callResult);

		// Assert — basic success
		callResult.IsError.Should().NotBeTrue(
			because: "get-browser-session should return a structured payload for a reachable forms-auth environment");
		response.Success.Should().BeTrue(
			because: "authenticating against a reachable forms-auth sandbox should succeed");
		response.SessionFilePath.Should().NotBeNullOrWhiteSpace(
			because: "a successful call returns the path to the storageState file");
		response.Error.Should().BeNull(
			because: "no error message should be present when the tool call succeeds");

		// AC3 — no-secrets guarantee: cookie VALUES must never appear in the agent-facing MCP surface.
		// Serialize the full CallToolResult to JSON and assert every cookie VALUE is absent — if a
		// regression echoed session JSON into the result the test would catch it.
		string callResultJson = JsonSerializer.Serialize(callResult);
		string sessionFileJson = await System.IO.File.ReadAllTextAsync(response.SessionFilePath,
			arrangeContext.CancellationTokenSource.Token);
		using JsonDocument sessionDoc = JsonDocument.Parse(sessionFileJson);
		if (sessionDoc.RootElement.TryGetProperty("cookies", out JsonElement cookiesArray)) {
			foreach (JsonElement cookie in cookiesArray.EnumerateArray()) {
				if (!cookie.TryGetProperty("value", out JsonElement valElem)) {
					continue;
				}
				string cookieValue = valElem.GetString();
				if (string.IsNullOrEmpty(cookieValue)) {
					continue;
				}
				string cookieName = cookie.TryGetProperty("name", out JsonElement nameElem)
					? nameElem.GetString() ?? "unknown"
					: "unknown";
				callResultJson.Should().NotContain(cookieValue,
					because: $"cookie '{cookieName}' value is a bearer secret and must never appear in the agent-facing MCP result");
			}
		}
	}

	private static async Task<CallToolResult> CallToolAsync(
		ArrangeContext arrangeContext,
		string toolName,
		Dictionary<string, object?> args) {
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);
		toolNames.Should().Contain(toolName,
			because: "the get-browser-session tool must be discoverable via the get-tool-contract compact index before the end-to-end call can be executed");
		return await arrangeContext.Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> {
				["args"] = args
			},
			arrangeContext.CancellationTokenSource.Token);
	}

	private async Task<ArrangeContext> ArrangeAsync(
		McpE2ESettings settings,
		TimeSpan timeout,
		bool requireReachableEnvironment) {
		CancellationTokenSource cancellationTokenSource = new(timeout);
		McpServerSession session = Session;
		string? environmentName = requireReachableEnvironment
			? await ResolveReachableEnvironmentAsync(settings)
			: settings.Sandbox.EnvironmentName;
		return new ArrangeContext(session, cancellationTokenSource, environmentName);
	}

	private static async Task<string> ResolveReachableEnvironmentAsync(McpE2ESettings settings) {
		string? configuredEnvironmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(configuredEnvironmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName to run get-browser-session MCP E2E tests.");
		}

		if (!await CanReachEnvironmentAsync(settings, configuredEnvironmentName!)) {
			Assert.Ignore($"get-browser-session MCP E2E requires a reachable sandbox environment. '{configuredEnvironmentName}' was not reachable.");
		}

		return configuredEnvironmentName!;
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
			settings,
			["ping-app", "-e", environmentName]);
		return result.ExitCode == 0;
	}

	private new sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		string? EnvironmentName) : IAsyncDisposable {
		public ValueTask DisposeAsync() {
			CancellationTokenSource.Dispose();
			return ValueTask.CompletedTask;
		}
	}
}
