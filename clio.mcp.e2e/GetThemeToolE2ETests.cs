using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.Theming;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Discovery and argument-validation coverage for the get-theme MCP tool: the real clio MCP server
/// advertises get-theme and binds its args wrapper to a structured validation error. A SUCCESSFUL read is
/// covered by <see cref="GetThemeHappyPathE2ETests"/> below in this file; the live create → read → edit →
/// update → delete round-trip lives in <see cref="ThemingSandboxE2ETests"/>.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("get-theme")]
[NonParallelizable]
public sealed class GetThemeToolE2ETests : McpContractFixtureBase {
	[Test]
	[AllureTag(GetThemeTool.ToolName)]
	[AllureName("get-theme tool is discoverable on the lazy surface")]
	[Description("Starts the real clio MCP server and verifies get-theme is discoverable via the get-tool-contract compact index on the lazy tool surface.")]
	public async Task GetTheme_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(GetThemeTool.ToolName,
			because: "the get-theme MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list");
	}

	[Test]
	[AllureTag(GetThemeTool.ToolName)]
	[AllureName("get-theme binds the args wrapper and returns a structured validation failure")]
	[Description("Calls get-theme through the real clio MCP server with an empty args object and verifies the structured kebab-case validation error names environment-name — proving the args wrapper binds without a live Creatio environment.")]
	public async Task GetTheme_Should_Return_Structured_Validation_Failure_When_Args_Are_Empty() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			GetThemeTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?>()
			},
			context.CancellationTokenSource.Token);
		GetThemeResponse result = EntitySchemaStructuredResultParser.Extract<GetThemeResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(
			because: "a read request without an environment name is invalid");
		result.Error.Should().Contain("environment-name",
			because: "the failure must name the exact kebab-case field the caller has to add");
	}

	[Test]
	[AllureTag(GetThemeTool.ToolName)]
	[AllureName("get-theme validation names the id field when only the environment is given")]
	[Description("Calls get-theme with only environment-name set and verifies the structured validation error names the missing id field — the second required argument of the read contract.")]
	public async Task GetTheme_Should_Return_Structured_Validation_Failure_When_Id_Is_Missing() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			GetThemeTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = "definitely-not-a-registered-environment"
				}
			},
			context.CancellationTokenSource.Token);
		GetThemeResponse result = EntitySchemaStructuredResultParser.Extract<GetThemeResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(
			because: "a read request without a theme id is invalid");
		result.Error.Should().Contain("id is required and cannot be empty",
			because: "the failure must name the missing id field before any environment resolution is attempted");
	}
}

/// <summary>
/// Hermetic happy-path coverage for <c>get-theme</c> through the real clio MCP server. The theme catalog and
/// the <c>theme.css</c> route are served by a local stub, so a SUCCESSFUL read is verified on every CI run
/// without a live branded environment.
/// </summary>
/// <remarks>
/// A separate fixture rather than a case in <see cref="GetThemeToolE2ETests"/> because it writes an isolated
/// clio settings file at startup and therefore cannot inherit <see cref="McpContractFixtureBase"/>, whose
/// shared server is started once for the whole fixture — before any stub port is known.
/// </remarks>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("get-theme")]
[NonParallelizable]
public sealed class GetThemeHappyPathE2ETests {

	private const string ThemeId = "e2e-hermetic-theme";
	private const string ThemeCaption = "Clio MCP E2E hermetic theme";
	private const string ThemeCssClassName = "e2e-hermetic-theme";
	private const string ThemeCssContent = ".e2e-hermetic-theme{color:#003366}";

	// The catalog-published cssFilePath, including the cache-busting query the real service appends. The tool
	// builds the CSS URL from this value, so the stub must answer the identical path AND query.
	private const string ThemeCssFilePath =
		"Terrasoft.Configuration/Pkg/Custom/Files/themes/" + ThemeId + "/theme.css?hash=e2ehash";

	[Test]
	[AllureTag(GetThemeTool.ToolName)]
	[AllureName("get-theme returns theme metadata and CSS content through the real MCP server")]
	[AllureDescription("Serves the theme catalog and theme.css from a local stub, then calls get-theme through the real clio MCP server and verifies the successful envelope.")]
	[Description("Verifies the get-theme happy path end to end against a stubbed Creatio: the envelope carries success, id, caption, cssClassName, cssContent and cssContentLength.")]
	public async Task GetTheme_Should_Return_MetadataAndContent_When_CatalogAndCssAreServed() {
		// Arrange
		await using StubbedThemeEnvironment environment = await StubbedThemeEnvironment.StartAsync();

		// Act
		CallToolResult callResult = await environment.Session.CallToolAsync(
			GetThemeTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = environment.EnvironmentName,
				["id"] = ThemeId
			},
			environment.CancellationTokenSource.Token);
		GetThemeResponse response = EntitySchemaStructuredResultParser.Extract<GetThemeResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "get-theme reports its outcome as a structured payload, not an MCP protocol error");
		response.Success.Should().BeTrue(
			because: $"the stubbed catalog publishes the requested theme and serves its CSS (error: {response.Error})");
		response.Id.Should().Be(ThemeId,
			because: "the envelope must carry the id resolved from the catalog");
		response.Caption.Should().Be(ThemeCaption,
			because: "the caption must survive the whole MCP transport unchanged");
		response.CssClassName.Should().Be(ThemeCssClassName,
			because: "the cssClassName must survive the whole MCP transport unchanged");
		response.CssContent.Should().Be(ThemeCssContent,
			because: "the CSS served at the catalog-reported cssFilePath must be returned byte-for-byte");
		response.CssContentLength.Should().Be(ThemeCssContent.Length,
			because: "the reported length must match the returned content");
	}

	/// <summary>
	/// Owns the whole hermetic arrangement: the stub Creatio, an isolated clio settings file carrying a
	/// pre-registered environment that points at the stub, and the real clio MCP server process started
	/// against that settings file.
	/// </summary>
	private sealed record StubbedThemeEnvironment(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		RuntimeDetectionStubServer StubServer,
		TemporaryClioSettingsOverride SettingsOverride,
		string EnvironmentName,
		string TempHome) : IAsyncDisposable {

		private static string BuildCatalogJson() =>
			$$"""
			{"success":true,"values":[{"id":"{{ThemeId}}","caption":"{{ThemeCaption}}","cssClassName":"{{ThemeCssClassName}}","cssFilePath":"{{ThemeCssFilePath}}"}]}
			""";

		private static string BuildSettingsJson(string environmentName, string baseUrl) =>
			$$"""
			{
			  "ActiveEnvironmentKey": "{{environmentName}}",
			  "Environments": {
			    "{{environmentName}}": {
			      "Uri": "{{baseUrl}}",
			      "Login": "Supervisor",
			      "Password": "Supervisor",
			      "IsNetCore": true
			    }
			  }
			}
			""";

		public static async Task<StubbedThemeEnvironment> StartAsync() {
			string tempHome = Path.Combine(Path.GetTempPath(), $"clio-get-theme-e2e-{Guid.NewGuid():N}");
			Directory.CreateDirectory(tempHome);
			McpE2ESettings settings = TestConfiguration.Load();
			settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
			// The settings file the child clio resolves is derived from this variable, so the test never
			// touches the developer's real environment catalog.
			settings.ProcessEnvironmentVariables[OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME"] = tempHome;

			RuntimeDetectionStubServer stubServer = RuntimeDetectionStubServer.Start(
				new RuntimeDetectionStubServerConfiguration(
					NetCoreHealthEnabled: true,
					NetFrameworkHealthEnabled: false,
					NetCoreServiceEnabled: true,
					NetFrameworkServiceEnabled: false,
					NetCoreUiMarkerEnabled: true,
					// Satisfies the get-theme [RequiresCreatioVersion] floor.
					CoreVersion: "10.0.0.1",
					ThemeCatalogJson: BuildCatalogJson(),
					ThemeCssPath: "/" + ThemeCssFilePath,
					ThemeCssContent: ThemeCssContent));

			CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
			try {
				string environmentName = $"get-theme-stub-{Guid.NewGuid():N}";
				TemporaryClioSettingsOverride settingsOverride = TemporaryClioSettingsOverride.ReplaceContent(
					BuildSettingsJson(environmentName, stubServer.BaseUrl),
					settings.ClioProcessPath,
					settings.ProcessEnvironmentVariables);
				McpServerSession session = await McpServerSession.StartAsync(
					settings, cancellationTokenSource.Token);
				return new StubbedThemeEnvironment(
					session, cancellationTokenSource, stubServer, settingsOverride, environmentName, tempHome);
			}
			catch {
				cancellationTokenSource.Dispose();
				await stubServer.DisposeAsync();
				throw;
			}
		}

		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			await StubServer.DisposeAsync();
			SettingsOverride.Dispose();
			CancellationTokenSource.Dispose();
			try {
				if (Directory.Exists(TempHome)) {
					Directory.Delete(TempHome, recursive: true);
				}
			}
			catch (IOException) {
				// Best-effort cleanup: the just-stopped child clio can still hold a handle on the isolated
				// settings file, and a leaked temp directory must not fail an otherwise-green fixture.
			}
			catch (UnauthorizedAccessException) {
				// Same best-effort rationale as the IOException case above.
			}
		}
	}
}
