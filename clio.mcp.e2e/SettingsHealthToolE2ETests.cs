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

[TestFixture]
[AllureNUnit]
[AllureFeature(SettingsHealthTool.ToolName)]
[NonParallelizable]
public sealed class SettingsHealthToolE2ETests {
	[Test]
	[AllureTag(SettingsHealthTool.ToolName)]
	[AllureName("settings-health reports repaired bootstrap status when ActiveEnvironmentKey is stale")]
	public async Task SettingsHealth_Should_Report_Repaired_Status_When_Active_Environment_Key_Is_Invalid() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		settings.ProcessEnvironmentVariables["HOME"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		TemporaryClioSettingsOverride settingsOverride = TemporaryClioSettingsOverride.SetWrongActiveEnvironmentKey(
			settings.ClioProcessPath,
			settings.ProcessEnvironmentVariables);
		File.ReadAllText(settingsOverride.AppSettingsPath).Should().Contain("\"ActiveEnvironmentKey\": \"wrong-dev\"",
			because: "the E2E fixture should overwrite the exact appsettings.json file before the MCP server starts");
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3), settingsOverride);

		// Act
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);
		CallToolResult callResult = await context.Session.CallToolAsync(
			SettingsHealthTool.ToolName,
			new Dictionary<string, object?>(),
			context.CancellationTokenSource.Token);
		SettingsHealthResult result = EntitySchemaStructuredResultParser.Extract<SettingsHealthResult>(callResult);

		// Assert
		tools.Select(tool => tool.Name).Should().Contain(SettingsHealthTool.ToolName,
			because: "the repaired bootstrap server should advertise the settings-health diagnostics tool");
		callResult.IsError.Should().NotBeTrue(
			because: "settings-health should return a normal MCP tool result envelope");
		result.SettingsFilePath.Should().Be(settingsOverride.AppSettingsPath,
			because: "the MCP diagnostics tool should report the same appsettings.json path that the E2E fixture overwrote");
		result.Status.Should().Be("repaired",
			because: "the stale ActiveEnvironmentKey should be repaired during MCP bootstrap");
		result.ActiveEnvironmentKey.Should().Be("wrong-dev",
			because: "the tool should expose the original configured active environment key");
		result.ResolvedActiveEnvironmentKey.Should().Be("dev",
			because: "bootstrap should deterministically select the first configured environment");
		result.RepairsApplied.Should().Contain(repair => repair.Code == "set-active-environment",
			because: "the MCP diagnostics payload should explain the applied active-environment repair");
		result.CanExecuteEnvTools.Should().BeTrue(
			because: "a repaired bootstrap state should allow named-environment tool execution");
	}

	private static async Task<ArrangeContext> ArrangeAsync(
		McpE2ESettings settings,
		TimeSpan timeout,
		TemporaryClioSettingsOverride settingsOverride) {
		CancellationTokenSource cancellationTokenSource = new(timeout);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource, settingsOverride);
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		TemporaryClioSettingsOverride SettingsOverride) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
			SettingsOverride.Dispose();
		}
	}
}
