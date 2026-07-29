using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
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
	[AllureName("build-theme applies a confirmed local font family through the token without a Google Fonts import")]
	[Description("Starts the real clio MCP server and invokes build-theme with a heading font also listed in local-font-families; verifies the returned CSS carries the family in its --crt-font-family-heading token and contains no @import, so a font the user confirmed is installed locally is never fetched from Google Fonts.")]
	public async Task BuildTheme_Should_OmitImport_ForConfirmedLocalFontFamily() {
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
					["body-font"] = "Verdana",
					["local-font-families"] = new[] { "Verdana" }
				}
			},
			context.CancellationTokenSource.Token);
		BuildThemeResult result = EntitySchemaStructuredResultParser.Extract<BuildThemeResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a confirmed local font family is valid input, not an error");
		result.Success.Should().BeTrue(
			because: "build-theme builds normally when the requested family needs no download");
		result.Css.Should().NotContain("@import",
			because: "a family the user confirmed is installed locally must not be fetched, and Google answers 200 with a look-alike substitute for many such names");
		result.Css.Should().Contain("--crt-font-family-heading: 'Verdana', sans-serif;",
			because: "the family is still applied through the token so the theme actually restyles");
	}
}
