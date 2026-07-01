using System;
using System.Collections.Generic;
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
/// so the real clio MCP server can build a theme without a live Creatio environment. Its tools are behind the
/// <c>theming</c> feature toggle, so the fixture skip-gates when the feature is disabled (the default) —
/// exactly like the other theming and process-designer E2E fixtures.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("build-theme")]
[NonParallelizable]
public sealed class BuildThemeToolE2ETests {
	private const string ToolName = BuildThemeTool.ToolName;

	[Test]
	[AllureTag(ToolName)]
	[AllureName("build-theme advertises read-only metadata and builds CSS from the bundled template")]
	[Description("Starts the real clio MCP server, verifies build-theme is advertised as read-only with the guidance pointer, and invokes it to build a theme.css from the bundled template. Skips when the theming feature is disabled.")]
	public async Task BuildTheme_Should_Advertise_And_Build() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		FeatureE2EGate.SkipIfFeatureDisabled(settings, "theming");
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationTokenSource.Token);
		McpClientTool tool = tools.Single(t => t.Name == ToolName);
		CallToolResult callResult = await session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> { ["primary"] = "#004fd6", ["cssClassName"] = "MyTheme" },
			cancellationTokenSource.Token);
		BuildThemeResult result = EntitySchemaStructuredResultParser.Extract<BuildThemeResult>(callResult);

		// Assert
		tool.ProtocolTool.Annotations.Should().NotBeNull(
			because: "the MCP server should expose tool annotations for client-side safety policies");
		tool.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeTrue(
			because: "build-theme is a pure compute tool that never mutates an environment");
		tool.ProtocolTool.Annotations.DestructiveHint.Should().BeFalse(
			because: "build-theme only reads the bundled template and returns CSS");
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
}
