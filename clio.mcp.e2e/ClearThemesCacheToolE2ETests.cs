using System;
using System.Collections.Generic;
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
/// End-to-end coverage for the clear-themes-cache MCP tool. Actually clearing the theme cache requires a
/// live Creatio environment with branding licensing, so the hermetic CI-safe assertions are that the real
/// clio MCP server advertises clear-themes-cache and binds its args wrapper to a structured validation error;
/// the live behavior is covered by <see cref="ThemingSandboxE2ETests"/>.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("clear-themes-cache")]
[NonParallelizable]
public sealed class ClearThemesCacheToolE2ETests : McpContractFixtureBase {
	[Test]
	[AllureTag(ClearThemesCacheTool.ToolName)]
	[AllureName("clear-themes-cache tool is discoverable on the lazy surface")]
	[Description("Starts the real clio MCP server and verifies clear-themes-cache is discoverable via the get-tool-contract compact index on the lazy tool surface.")]
	public async Task ClearThemesCache_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ClearThemesCacheTool.ToolName,
			because: "the clear-themes-cache MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list");
	}

	[Test]
	[AllureTag(ClearThemesCacheTool.ToolName)]
	[AllureName("clear-themes-cache binds the args wrapper and returns a structured validation failure")]
	[Description("Calls clear-themes-cache through the real clio MCP server with an empty args object and verifies the structured exit-code-1 error names environment-name — proving the args wrapper binds without a live Creatio environment.")]
	public async Task ClearThemesCache_Should_Return_Structured_Validation_Failure_When_Args_Are_Empty() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ClearThemesCacheTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?>()
			},
			context.CancellationTokenSource.Token);
		CommandExecutionEnvelope response = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		response.ExitCode.Should().Be(1,
			because: "a missing environment name is an expected, caller-actionable validation error");
		response.Output.Should().Contain(message =>
			message.Value != null && message.Value.Contains("environment-name is required"),
			because: "the failure must name the exact kebab-case field the caller has to add");
	}
}
