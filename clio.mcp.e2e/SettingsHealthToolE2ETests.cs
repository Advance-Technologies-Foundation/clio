using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(SettingsHealthTool.ToolName)]
[NonParallelizable]
public sealed class SettingsHealthToolE2ETests {
	[Test]
	[Description("Reports a detected (not auto-repaired) bootstrap status through MCP when ActiveEnvironmentKey is stale, because clio deliberately never auto-selects the active environment (see SettingsBootstrapServiceTests).")]
	[AllureTag(SettingsHealthTool.ToolName)]
	[AllureName("check-settings-health reports detected bootstrap issue when ActiveEnvironmentKey is stale")]
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
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		CallToolResult callResult = await context.Session.CallToolAsync(
			SettingsHealthTool.ToolName,
			new Dictionary<string, object?>(),
			context.CancellationTokenSource.Token);
		SettingsHealthResult result = EntitySchemaStructuredResultParser.Extract<SettingsHealthResult>(callResult);

		// Assert
		toolNames.Should().Contain(SettingsHealthTool.ToolName,
			because: "the repaired bootstrap server should keep the check-settings-health diagnostics tool discoverable via the get-tool-contract compact index");
		callResult.IsError.Should().NotBeTrue(
			because: "check-settings-health should return a normal MCP tool result envelope");
		result.SettingsFilePath.Should().Be(settingsOverride.AppSettingsPath,
			because: "the MCP diagnostics tool should report the same appsettings.json path that the E2E fixture overwrote");
		result.Status.Should().Be("issues-detected",
			because: "clio deliberately reports a stale ActiveEnvironmentKey as a configuration issue and never auto-selects the active environment (see SettingsBootstrapServiceTests)");
		result.ActiveEnvironmentKey.Should().Be("wrong-dev",
			because: "the tool should expose the original configured active environment key so the user sees which key is wrong");
		result.ResolvedActiveEnvironmentKey.Should().BeNull(
			because: "bootstrap must not auto-select a fallback environment — only the user may set the active environment");
		result.Issues.Should().Contain(issue => issue.Code == "invalid-active-environment",
			because: "the MCP diagnostics payload should surface the stale active-environment issue so the caller can fix it");
		result.RepairsApplied.Should().NotContain(repair => repair.Code == "set-active-environment",
			because: "no automatic active-environment repair is applied — the user must explicitly call set-active-environment");
		result.CanExecuteEnvTools.Should().BeFalse(
			because: "named-environment tool execution requires a valid resolved active environment, which a stale key does not provide");
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
