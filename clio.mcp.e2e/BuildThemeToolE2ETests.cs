using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Linq;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Theming;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the build-theme MCP tool. It builds over the bundled template without a live
/// Creatio environment; a custom font additionally triggers a Google Fonts availability check, which fails
/// soft to an advisory warning when the catalogue is unreachable.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("build-theme")]
[NonParallelizable]
public sealed class BuildThemeToolE2ETests : McpContractFixtureBase {
	private const string ToolName = BuildThemeTool.ToolName;

	[Test]
	[AllureTag(ToolName)]
	[AllureName("build-theme is discoverable on the lazy surface and builds CSS from the bundled template")]
	[Description("Starts the real clio MCP server, verifies build-theme is discoverable via the get-tool-contract compact index as non-destructive with the guidance pointer in its contract, and invokes it in compute mode to build a theme.css from the bundled template.")]
	public async Task BuildTheme_Should_Be_Discoverable_And_Build() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		// build-theme is hidden from tools/list on the lazy surface, so its discovery metadata comes from the
		// get-tool-contract compact index (destructive flag) and full contract (description) instead of
		// tools/list annotations.
		IReadOnlyList<ToolContractIndexEntry> index =
			await context.Session.GetToolContractIndexAsync(context.CancellationTokenSource.Token);
		CallToolResult contractResult = await context.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["tool-names"] = new[] { ToolName }
				}
			},
			context.CancellationTokenSource.Token);
		ToolContractGetResponse contracts =
			EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(contractResult);
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["primary"] = "#004fd6",
					["css-class-name"] = "MyTheme"
				}
			},
			context.CancellationTokenSource.Token);
		BuildThemeResult result = EntitySchemaStructuredResultParser.Extract<BuildThemeResult>(callResult);

		// Assert
		ToolContractIndexEntry indexEntry = index.Should().ContainSingle(entry => entry.Name == ToolName,
			because: "the build-theme MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list")
			.Which;
		indexEntry.Destructive.Should().BeFalse(
			because: "build-theme writes generated build artifacts into a caller-supplied directory, never destructive updates");
		ToolContractDefinition contract = contracts.Tools!.Single(definition => definition.Name == ToolName);
		contract.Description.Should().Contain("get-guidance theming",
			because: "the contract routes agents to the theme workflow guidance");
		contract.Description.Should().Contain("checked against Google Fonts",
			because: "the branding skill gates itself on this phrase in the get-tool-contract PROJECTION — the unit assertion on the [Description] attribute would stay green if build-theme ever gained a curated ToolContractCatalog entry that dropped it, silently telling users on a correct clio that their clio predates the feature");
		callResult.IsError.Should().NotBeTrue(
			because: "build-theme returns a structured result instead of a top-level MCP failure");
		result.Success.Should().BeTrue(
			because: "a valid primary and css-class-name build a theme from the bundled template");
		result.Css.Should().Contain(".MyTheme",
			because: "the built CSS scopes the theme to the supplied css-class-name");
		result.Css.Should().Contain("--crt-palette-primary-500",
			because: "the built CSS carries the generated primary palette");
		result.Descriptor.Should().Contain("MyTheme",
			because: "build-theme also returns the theme.json descriptor scoped to the css class name");
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureName("build-theme workspace-write mode writes theme.css + theme.json into the package and returns the path without the CSS payload")]
	[Description("Starts the real clio MCP server and invokes build-theme with workspace-directory + package-name; verifies it writes theme.css + theme.json into <ws>/packages/<pkg>/Files/themes/<css-class-name>/ and returns the path with no CSS payload.")]
	public async Task BuildTheme_Should_WriteIntoPackage_WhenWorkspaceAndPackageProvided() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));
		string workspaceDir = CreateFixtureDirectory("build-theme-ws");
		const string packageName = "UsrTheme";
		const string cssClassName = "MyTheme";
		string packagePath = Path.Combine(workspaceDir, "packages", packageName);
		string themeDir = Path.Combine(packagePath, "Files", "themes", cssClassName);
		Directory.CreateDirectory(Path.Combine(workspaceDir, ".clio"));
		File.WriteAllText(Path.Combine(workspaceDir, ".clio", "workspaceSettings.json"), "{}");
		Directory.CreateDirectory(packagePath);

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["primary"] = "#004fd6",
					["css-class-name"] = cssClassName,
					["workspace-directory"] = workspaceDir,
					["package-name"] = packageName
				}
			},
			context.CancellationTokenSource.Token);
		BuildThemeResult result = EntitySchemaStructuredResultParser.Extract<BuildThemeResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "build-theme returns a structured result instead of a top-level MCP failure");
		result.Success.Should().BeTrue(
			because: "a valid workspace + existing package is a valid workspace-write request");
		result.Path.Should().Be(themeDir,
			because: "workspace-write mode returns the resolved <ws>/packages/<pkg>/Files/themes/<cssClassName> directory");
		result.Css.Should().BeNull(
			because: "the CSS payload is omitted in workspace-write mode to keep the large string out of the agent context");
		File.Exists(Path.Combine(themeDir, "theme.css")).Should().BeTrue(
			because: "workspace-write mode writes theme.css into the package theme directory");
		File.Exists(Path.Combine(themeDir, "theme.json")).Should().BeTrue(
			because: "workspace-write mode writes theme.json alongside theme.css");
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureName("build-theme rejects the removed local-font-families argument with a migration hint")]
	[Description("Starts the real clio MCP server and invokes build-theme with the removed local-font-families argument; verifies the real JSON binding routes it into the overflow bag and the tool returns a structured failure explaining the argument was removed, instead of silently ignoring it.")]
	public async Task BuildTheme_Should_RejectRemovedLocalFontFamiliesArgument() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["primary"] = "#004fd6",
					["css-class-name"] = "MyTheme",
					["heading-font"] = "Verdana",
					["local-font-families"] = new[] { "Verdana" }
				}
			},
			context.CancellationTokenSource.Token);
		BuildThemeResult result = EntitySchemaStructuredResultParser.Extract<BuildThemeResult>(callResult);

		// Assert
		result.Success.Should().BeFalse(
			because: "the removed argument must fail loudly for one release instead of vanishing into the overflow bag");
		result.Error.Should().Contain("local-font-families was removed",
			because: "the failure names the removed argument so an agent built against the old contract can self-correct");
		result.Error.Should().Contain("probed automatically",
			because: "the failure explains that the availability probe now makes the suppression decision");
	}
}

/// <summary>
/// The one build-theme e2e case that needs outbound access to fonts.google.com. It lives in its own
/// fixture, and deliberately carries NEITHER <c>McpE2E.NoEnvironment</c> nor <c>McpE2E.Sandbox</c>:
/// per-method categories are additive on top of the fixture tag, so a live-network test left in the
/// environment-free tier would still be selected by the blocking pre-merge sweep, whose acceptance gate is
/// <c>Total == Passed</c> AND <c>Skipped == 0</c> — an egress-blocked runner would fail that gate on a skip.
/// Exclude it with <c>dotnet test --filter "TestCategory!=McpE2E.LiveGoogleFonts"</c>.
/// The deterministic InCatalog / NotInCatalog / Unverified matrix lives in the unit suite
/// (<c>GoogleFontsCatalogTests</c>, <c>BuildThemeCommandTests</c>); the MCP server runs out of process here,
/// so its <c>IGoogleFontsCatalog</c> cannot be substituted from the test.
/// </summary>
[TestFixture]
[Category(LiveGoogleFontsE2ETests.LiveGoogleFontsCategory)]
[AllureNUnit]
[AllureSuite("MCP e2e")]
[AllureFeature("build-theme")]
public sealed class LiveGoogleFontsE2ETests : McpContractFixtureBase {

	internal const string LiveGoogleFontsCategory = "McpE2E.LiveGoogleFonts";

	private const string ToolName = "build-theme";

	[Test]
	[AllureTag(ToolName)]
	[AllureName("build-theme suppresses the Google Fonts import for a family the live catalogue does not publish")]
	[Description("Live smoke test of the whole probe path: starts the real clio MCP server and invokes build-theme with a family Google Fonts does not host (Verdana); the server probes the live catalogue, applies the family through the --crt-font-family-heading token WITHOUT an @import, and reports the suppression in a warning. The server runs out of process, so its IGoogleFontsCatalog cannot be substituted from here — the deterministic InCatalog/NotInCatalog/Unverified matrix lives in the unit suite (GoogleFontsCatalogTests, BuildThemeCommandTests). Carries its own category so a runner without egress can exclude it, and skips rather than fails when the endpoint is unreachable: the production design fails open, so a blocked runner would otherwise report an infrastructure problem as a suppression regression.")]
	public async Task BuildTheme_Should_OmitImportAndWarn_ForFamilyNotInGoogleFonts() {
		// Arrange
		await SkipUnlessGoogleFontsIsReachableAsync();
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["primary"] = "#004fd6",
					["css-class-name"] = "MyTheme",
					["heading-font"] = "Verdana",
					["body-font"] = "Verdana"
				}
			},
			context.CancellationTokenSource.Token);
		BuildThemeResult result = EntitySchemaStructuredResultParser.Extract<BuildThemeResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a family outside Google Fonts is advisory, not an error");
		result.Success.Should().BeTrue(
			because: "build-theme builds normally and decides the import from its own availability probe");
		result.Css.Should().NotContain("@import",
			because: "the live catalogue answers 404 for Verdana, so the import is suppressed — css2 would serve a look-alike substitute that shadows the locally installed font");
		result.Css.Should().Contain("--crt-font-family-heading: 'Verdana', sans-serif;",
			because: "the family is still applied through the token so the theme actually restyles");
		result.Warnings.Should().Contain(w => w.Contains("was not found in Google Fonts"),
			because: "the suppression is disclosed post factum through the warnings channel");
	}

	/// <summary>
	/// Marks the test inconclusive unless the live endpoint answers the way the production probe requires:
	/// the same handler posture (no cookies, no redirect following), the same user agent and budget, and a
	/// JSON success. A looser guard would pass on a captive portal or a slow link while the server's own
	/// probe degrades to Unverified, keeps the import, and reds the suppression assertion — the very
	/// infrastructure-as-regression failure this guard exists to prevent.
	/// </summary>
	private static async Task SkipUnlessGoogleFontsIsReachableAsync() {
		using HttpClientHandler handler = new() { UseCookies = false, AllowAutoRedirect = false };
		using HttpClient probeClient = new(handler) { Timeout = GoogleFontsCatalog.ProbeTimeout };
		probeClient.DefaultRequestHeaders.UserAgent.TryParseAdd("clio");
		try {
			using HttpResponseMessage response = await probeClient.GetAsync(
				"https://fonts.google.com/metadata/fonts/Roboto", HttpCompletionOption.ResponseHeadersRead);
			string mediaType = response.Content?.Headers?.ContentType?.MediaType;
			if (!response.IsSuccessStatusCode
				|| mediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true) {
				Assert.Ignore(
					$"fonts.google.com answered {(int)response.StatusCode} ({mediaType ?? "no content type"}); "
					+ "the server's probe would degrade to Unverified, so the suppression path cannot be exercised.");
			}
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException) {
			Assert.Ignore($"fonts.google.com is unreachable ({exception.GetType().Name}); the live probe path cannot be exercised.");
		}
	}
}
