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
/// End-to-end coverage for the update-theme MCP tool. Actually overwriting a theme requires a live Creatio
/// environment with branding licensing and the CanManageThemes operation, so the hermetic CI-safe assertions
/// are that the real clio MCP server advertises update-theme and binds its args wrapper to a structured
/// validation error; the live behavior is covered by <see cref="ThemingSandboxE2ETests"/>.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("update-theme")]
[NonParallelizable]
public sealed class UpdateThemeToolE2ETests : McpContractFixtureBase {
	[Test]
	[AllureTag(UpdateThemeTool.ToolName)]
	[AllureName("update-theme is discoverable on the lazy surface with a destructive hint")]
	[Description("Starts the real clio MCP server and verifies update-theme is discoverable via the get-tool-contract compact index carrying the destructive flag, so confirmation-seeking MCP clients prompt before the full overwrite.")]
	public async Task UpdateTheme_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		// update-theme is hidden from tools/list on the lazy surface, so its destructive hint comes from
		// the get-tool-contract compact index instead of a tools/list annotation.
		IReadOnlyList<ToolContractIndexEntry> index =
			await context.Session.GetToolContractIndexAsync(context.CancellationTokenSource.Token);

		// Assert
		ToolContractIndexEntry indexEntry = index.Should().ContainSingle(entry => entry.Name == UpdateThemeTool.ToolName,
			because: "the update-theme MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list")
			.Which;
		indexEntry.Destructive.Should().BeTrue(
			because: "update-theme is a full overwrite by id that destroys the theme's previous content");
	}

	[Test]
	[AllureTag(UpdateThemeTool.ToolName)]
	[AllureName("update-theme binds the args wrapper and returns a structured validation failure")]
	[Description("Calls update-theme through the real clio MCP server with an empty args object and verifies the structured exit-code-1 error names environment-name — proving the args wrapper binds without a live Creatio environment.")]
	public async Task UpdateTheme_Should_Return_Structured_Validation_Failure_When_Args_Are_Empty() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			UpdateThemeTool.ToolName,
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
