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
		// This fixture deliberately installs a catalog whose ActiveEnvironmentKey does not resolve, which
		// is precisely the state that makes SettingsBootstrapService report CanExecuteEnvTools = false —
		// so every environment-touching tool in the suite answers "clio settings bootstrap is broken" and
		// every reachability probe skips. Written to the SHARED catalog (setting HOME alone leaves the
		// suite-owned CLIO_HOME in charge) that blast radius is the whole run, and the snapshot/restore
		// that is supposed to contain it is a non-atomic write that takes none of clio's cross-process
		// locks. A private home keeps the deliberate breakage where it belongs.
		IsolatedClioHome.CreateAndRedirect(settings, "settings-health-home");
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

	[Test]
	[Description("Reports a settings file a newer clio wrote as a degraded-but-usable shape mismatch, not as an unreadable file, and keeps environment-scoped execution available (issue #1462).")]
	[AllureTag(SettingsHealthTool.ToolName)]
	[AllureName("check-settings-health reports settings-shape-mismatch and stays usable for a future-shaped section")]
	public async Task SettingsHealth_Should_Report_Shape_Mismatch_When_A_Newer_Clio_Wrote_The_File() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		// A private home for the same reason as the fixture above: the deliberate breakage must not reach
		// the shared catalog the rest of the run reads.
		IsolatedClioHome.CreateAndRedirect(settings, "settings-shape-mismatch-home");
		TemporaryClioSettingsOverride settingsOverride =
			TemporaryClioSettingsOverride.SetFutureShapedAutoupdateSection(
				settings.ClioProcessPath,
				settings.ProcessEnvironmentVariables);
		string originalContent = File.ReadAllText(settingsOverride.AppSettingsPath);
		originalContent.Should().Contain("\"future\"",
			because: "the fixture must install the unbindable section before the MCP server starts");
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3), settingsOverride);

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			SettingsHealthTool.ToolName,
			new Dictionary<string, object?>(),
			context.CancellationTokenSource.Token);
		SettingsHealthResult result = EntitySchemaStructuredResultParser.Extract<SettingsHealthResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "check-settings-health must answer normally about a degraded configuration");
		result.Issues.Should().Contain(issue => issue.Code == "settings-shape-mismatch",
			because: "the file is valid JSON that this build cannot fully bind, which is a version skew and not an unreadable file");
		result.Issues.Should().NotContain(issue => issue.Code == "settings-file-unreadable",
			because: "reporting a valid file as unreadable is what sent users to hand-edit a correct file in issue #1462");
		result.EnvironmentCount.Should().Be(1,
			because: "the environments in the file are intact and must still load in the degraded mode");
		result.CanExecuteEnvTools.Should().BeTrue(
			because: "degrading rather than failing is the point: environment-scoped tools must keep working");
		File.ReadAllText(settingsOverride.AppSettingsPath).Should().Be(originalContent,
			because: "clio must not rewrite a file holding a section it cannot represent, or it would delete what the newer clio wrote");
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
