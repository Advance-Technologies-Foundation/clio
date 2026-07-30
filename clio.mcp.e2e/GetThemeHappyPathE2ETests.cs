using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.Theming;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Hermetic happy-path coverage for <c>get-theme</c> through the real clio MCP server. The theme catalog and
/// the <c>theme.css</c> route are served by a local stub, so a SUCCESSFUL read is verified on every CI run
/// without a live branded environment; the live create → read → edit → update → delete round-trip stays in
/// <see cref="ThemingSandboxE2ETests"/>. This fixture writes an isolated clio settings file at startup, so it
/// deliberately does NOT inherit <c>McpContractFixtureBase</c> (whose shared server is started once for the
/// whole fixture, before any stub port is known).
/// </summary>
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
