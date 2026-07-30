using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.Theming;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Discovery and argument-validation coverage for the get-theme MCP tool: the real clio MCP server
/// advertises get-theme and binds its args wrapper to a structured validation error.
/// </summary>
/// <remarks>
/// A SUCCESSFUL read is covered separately: <see cref="GetThemeHappyPathE2ETests"/> serves the theme catalog
/// and theme.css from a local stub and runs on every CI run, and
/// <see cref="ThemingSandboxE2ETests"/> exercises the full create → read → edit → update → delete round-trip
/// against a live branded environment.
/// </remarks>
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
