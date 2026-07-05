using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// End-to-end coverage for the build-theme MCP tool. build-theme is pure compute over the bundled template,
/// so the real clio MCP server can build a theme without a live Creatio environment.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("build-theme")]
[NonParallelizable]
public sealed class BuildThemeToolE2ETests {
	private const string ToolName = BuildThemeTool.ToolName;

	[Test]
	[AllureTag(ToolName)]
	[AllureName("build-theme advertises write-capable metadata and builds CSS from the bundled template")]
	[Description("Starts the real clio MCP server, verifies build-theme is advertised as write-capable (ReadOnly=false, the output mode writes local files) with the guidance pointer, and invokes it in compute mode to build a theme.css from the bundled template.")]
	public async Task BuildTheme_Should_Advertise_And_Build() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationTokenSource.Token);
		McpClientTool tool = tools.Single(t => t.Name == ToolName);
		CallToolResult callResult = await session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["primary"] = "#004fd6",
					["css-class-name"] = "MyTheme"
				}
			},
			cancellationTokenSource.Token);
		BuildThemeResult result = EntitySchemaStructuredResultParser.Extract<BuildThemeResult>(callResult);

		// Assert
		tool.ProtocolTool.Annotations.Should().NotBeNull(
			because: "the MCP server should expose tool annotations for client-side safety policies");
		tool.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeFalse(
			because: "build-theme can write theme.css + theme.json to a local directory in its output mode");
		tool.ProtocolTool.Annotations.DestructiveHint.Should().BeFalse(
			because: "build-theme writes generated build artifacts into a caller-supplied directory, never destructive updates");
		tool.Description.Should().Contain("get-guidance theming",
			because: "the description routes agents to the theme workflow guidance");
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
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		string workspaceDir = Path.Combine(Path.GetTempPath(), "clio-build-theme-e2e", Guid.NewGuid().ToString("N"));
		const string packageName = "UsrTheme";
		const string cssClassName = "MyTheme";
		string packagePath = Path.Combine(workspaceDir, "packages", packageName);
		string themeDir = Path.Combine(packagePath, "Files", "themes", cssClassName);

		try {
			Directory.CreateDirectory(Path.Combine(workspaceDir, ".clio"));
			File.WriteAllText(Path.Combine(workspaceDir, ".clio", "workspaceSettings.json"), "{}");
			Directory.CreateDirectory(packagePath);
			await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

			// Act
			CallToolResult callResult = await session.CallToolAsync(
				ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["primary"] = "#004fd6",
						["css-class-name"] = cssClassName,
						["workspace-directory"] = workspaceDir,
						["package-name"] = packageName
					}
				},
				cancellationTokenSource.Token);
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
		} finally {
			if (Directory.Exists(workspaceDir)) {
				Directory.Delete(workspaceDir, recursive: true);
			}
		}
	}
}
