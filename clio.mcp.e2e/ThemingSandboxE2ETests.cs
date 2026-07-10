using System;
using System.Collections.Generic;
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
/// Sandbox-tier end-to-end coverage for the theming server flow: the live behavior of
/// check-theming-access, create-theme, list-themes, update-theme, delete-theme, and clear-themes-cache
/// against the configured sandbox Creatio environment. Theme mutations require the
/// <c>CanCustomizeBranding</c> license and the <c>CanManageThemes</c> operation on the stand, so the
/// mutating tests probe check-theming-access first and ignore themselves (rather than fail) on a stand
/// without theming access. The hermetic contract assertions for the same tools live in the per-tool
/// NoEnvironment fixtures.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature("theming-sandbox")]
[NonParallelizable]
public sealed class ThemingSandboxE2ETests : McpContractFixtureBase {

	private string? _environmentNameForCleanup;
	private string? _createdThemeId;

	/// <summary>
	/// Deletes the theme a failed lifecycle run left behind so the shared sandbox stand is not polluted;
	/// a completed lifecycle clears <see cref="_createdThemeId"/> and makes this a no-op.
	/// </summary>
	[TearDown]
	public async Task DeleteLeakedThemeAsync() {
		if (_createdThemeId is null || _environmentNameForCleanup is null) {
			return;
		}
		using CancellationTokenSource cleanupCts = new(TimeSpan.FromMinutes(1));
		await Session.CallToolAsync(
			DeleteThemeTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = _environmentNameForCleanup,
					["id"] = _createdThemeId
				}
			},
			cleanupCts.Token);
		_createdThemeId = null;
	}

	[Test]
	[AllureTag(CheckThemingAccessTool.ToolName)]
	[AllureName("check-theming-access reports both live verdicts for the sandbox environment")]
	[Description("Calls check-theming-access against the configured sandbox environment and verifies the live composed probe (CanManageThemes operation + CanCustomizeBranding license) completes with both verdicts.")]
	public async Task CheckThemingAccess_Should_Return_Both_Verdicts_When_Sandbox_Is_Reachable() {
		// Arrange
		string environmentName = await ResolveReachableSandboxEnvironmentAsync();
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		ThemingAccessResult result = await CheckThemingAccessAsync(context, environmentName);

		// Assert
		result.Success.Should().BeTrue(
			because: "the composed rights + license probe must complete against a reachable environment");
		result.CanManageThemes.Should().HaveValue(
			because: "the live probe must report the CanManageThemes operation verdict");
		result.CanCustomizeBranding.Should().HaveValue(
			because: "the live probe must report the CanCustomizeBranding license verdict");
		result.Error.Should().BeNull(
			because: "a completed probe carries no error");
	}

	[Test]
	[AllureTag(CreateThemeTool.ToolName)]
	[AllureName("theme CRUD lifecycle: create, list, update, delete on the sandbox environment")]
	[Description("Runs the full no-code theme lifecycle against the configured sandbox environment: create-theme with an explicit id, list-themes sees it, update-theme overwrites the caption, list-themes sees the new caption, delete-theme removes it, and list-themes no longer returns it. Ignored when the stand lacks theming access.")]
	public async Task ThemeTools_Should_Complete_Crud_Lifecycle_When_Theming_Access_Is_Granted() {
		// Arrange
		string environmentName = await ResolveReachableSandboxEnvironmentAsync();
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(5));
		await EnsureThemingAccessAsync(context, environmentName);
		string themeId = $"e2e-theme-{Guid.NewGuid():N}";
		const string createdCaption = "Clio MCP E2E";
		const string updatedCaption = "Clio MCP E2E updated";
		_environmentNameForCleanup = environmentName;

		// Act / Assert — create
		CallToolResult createCallResult = await CallToolAsync(context, CreateThemeTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = environmentName,
				["id"] = themeId,
				["caption"] = createdCaption,
				["css-class-name"] = themeId,
				["css-content"] = $".{themeId}{{color:#003366}}"
			});
		CreateThemeResult created = EntitySchemaStructuredResultParser.Extract<CreateThemeResult>(createCallResult);
		created.Success.Should().BeTrue(
			because: $"a stand with theming access must accept the theme creation (error: {created.Error})");
		created.Id.Should().Be(themeId,
			because: "the tool must echo the explicitly supplied theme id");
		_createdThemeId = themeId;

		// Act / Assert — the created theme is visible in the live catalog
		ListThemesResult afterCreate = await ListThemesAsync(context, environmentName);
		afterCreate.Themes.Should().Contain(theme => theme.Id == themeId && theme.Caption == createdCaption,
			because: "the created theme must be visible in the live catalog");

		// Act / Assert — full overwrite by id
		CommandExecutionEnvelope updateResponse = McpCommandExecutionParser.Extract(
			await CallToolAsync(context, UpdateThemeTool.ToolName, new Dictionary<string, object?> {
				["environment-name"] = environmentName,
				["id"] = themeId,
				["caption"] = updatedCaption,
				["css-class-name"] = themeId,
				["css-content"] = $".{themeId}{{color:#ff6600}}"
			}));
		updateResponse.ExitCode.Should().Be(0,
			because: "the full overwrite of an existing theme must succeed");
		ListThemesResult afterUpdate = await ListThemesAsync(context, environmentName);
		afterUpdate.Themes.Should().Contain(theme => theme.Id == themeId && theme.Caption == updatedCaption,
			because: "the overwritten caption must be visible in the live catalog");

		// Act / Assert — delete
		CommandExecutionEnvelope deleteResponse = McpCommandExecutionParser.Extract(
			await CallToolAsync(context, DeleteThemeTool.ToolName, new Dictionary<string, object?> {
				["environment-name"] = environmentName,
				["id"] = themeId
			}));
		deleteResponse.ExitCode.Should().Be(0,
			because: "deleting the just-created theme must succeed");
		_createdThemeId = null;
		ListThemesResult afterDelete = await ListThemesAsync(context, environmentName);
		afterDelete.Themes.Should().NotContain(theme => theme.Id == themeId,
			because: "the deleted theme must disappear from the live catalog");
	}

	[Test]
	[AllureTag(ClearThemesCacheTool.ToolName)]
	[AllureName("clear-themes-cache refreshes the live theme catalog cache on the sandbox environment")]
	[Description("Calls clear-themes-cache against the configured sandbox environment and verifies the refresh completes with exit code 0. Ignored when the stand lacks theming access.")]
	public async Task ClearThemesCache_Should_Succeed_When_Theming_Access_Is_Granted() {
		// Arrange
		string environmentName = await ResolveReachableSandboxEnvironmentAsync();
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));
		await EnsureThemingAccessAsync(context, environmentName);

		// Act
		CommandExecutionEnvelope response = McpCommandExecutionParser.Extract(
			await CallToolAsync(context, ClearThemesCacheTool.ToolName, new Dictionary<string, object?> {
				["environment-name"] = environmentName
			}));

		// Assert
		response.ExitCode.Should().Be(0,
			because: "refreshing the theme cache is an idempotent maintenance call");
	}

	private static async Task<string> ResolveReachableSandboxEnvironmentAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName to run the theming sandbox E2E tests.");
		}
		ClioCliCommandResult ping = await ClioCliCommandRunner.RunAsync(settings, ["ping-app", "-e", environmentName!]);
		if (ping.ExitCode != 0) {
			Assert.Ignore($"The theming sandbox E2E tests require a reachable sandbox environment; '{environmentName}' was not reachable.");
		}
		return environmentName!;
	}

	private async Task EnsureThemingAccessAsync(ArrangeContext context, string environmentName) {
		ThemingAccessResult access = await CheckThemingAccessAsync(context, environmentName);
		if (access.Success && access.CanManageThemes == true && access.CanCustomizeBranding == true) {
			return;
		}
		Assert.Ignore(
			$"The sandbox environment '{environmentName}' does not grant theming " +
			$"(success={access.Success}, canManageThemes={access.CanManageThemes}, " +
			$"canCustomizeBranding={access.CanCustomizeBranding}, error={access.Error}). " +
			"Theme mutations require the CanCustomizeBranding license and the CanManageThemes operation.");
	}

	private async Task<ThemingAccessResult> CheckThemingAccessAsync(ArrangeContext context, string environmentName) {
		CallToolResult callResult = await CallToolAsync(context, CheckThemingAccessTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = environmentName
			});
		callResult.IsError.Should().NotBeTrue(
			because: "check-theming-access reports its verdicts as a structured payload");
		return EntitySchemaStructuredResultParser.Extract<ThemingAccessResult>(callResult);
	}

	private async Task<ListThemesResult> ListThemesAsync(ArrangeContext context, string environmentName) {
		CallToolResult callResult = await CallToolAsync(context, ListThemesTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = environmentName
			});
		callResult.IsError.Should().NotBeTrue(
			because: "list-themes reports the catalog as a structured payload");
		ListThemesResult result = EntitySchemaStructuredResultParser.Extract<ListThemesResult>(callResult);
		result.Success.Should().BeTrue(
			because: $"reading the live theme catalog must succeed (error: {result.Error})");
		return result;
	}

	private static Task<CallToolResult> CallToolAsync(
		ArrangeContext context,
		string toolName,
		Dictionary<string, object?> args) {
		return context.Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> {
				["args"] = args
			},
			context.CancellationTokenSource.Token);
	}
}
