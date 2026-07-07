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
/// End-to-end coverage for the create-theme MCP tool. Actually creating a theme requires a live Creatio
/// environment with branding licensing and the CanManageThemes operation, so the hermetic CI-safe assertions
/// are that the real clio MCP server advertises create-theme and rejects a camelCase alias with a structured
/// rename hint; the live behavior is covered by <see cref="ThemingSandboxE2ETests"/>.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("create-theme")]
[NonParallelizable]
public sealed class CreateThemeToolE2ETests : McpContractFixtureBase {
	[Test]
	[AllureTag(CreateThemeTool.ToolName)]
	[AllureName("create-theme tool is discoverable on the lazy surface")]
	[Description("Starts the real clio MCP server and verifies create-theme is discoverable via the get-tool-contract compact index on the lazy tool surface.")]
	public async Task CreateTheme_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(CreateThemeTool.ToolName,
			because: "the create-theme MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list");
	}

	[Test]
	[AllureTag(CreateThemeTool.ToolName)]
	[AllureName("create-theme rejects a camelCase alias with a structured rename hint over the wire")]
	[Description("Calls create-theme through the real clio MCP server with a camelCase environmentName field and verifies the structured rename hint — proving the args wrapper binds and unknown keys reach the ExtensionData bag through the real MCP serializer, without a live Creatio environment.")]
	public async Task CreateTheme_Should_Return_RenameHint_When_CamelCase_Alias_Is_Passed_Over_The_Wire() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			CreateThemeTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environmentName"] = "docker_fix2"
				}
			},
			context.CancellationTokenSource.Token);
		CreateThemeResult result = EntitySchemaStructuredResultParser.Extract<CreateThemeResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(
			because: "a camelCase alias must be rejected, not silently dropped");
		result.Error.Should().Contain("'environmentName' -> 'environment-name'",
			because: "the failure must tell the caller the exact rename that fixes the call");
	}

	[Test]
	[AllureTag(CreateThemeTool.ToolName)]
	[AllureName("create-theme binds the args wrapper and returns a structured validation failure")]
	[Description("Calls create-theme through the real clio MCP server with an empty args object and verifies the structured { success=false, error } result names environment-name — proving the args wrapper binds without a live Creatio environment.")]
	public async Task CreateTheme_Should_Return_Structured_Validation_Failure_When_Args_Are_Empty() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			CreateThemeTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?>()
			},
			context.CancellationTokenSource.Token);
		CreateThemeResult result = EntitySchemaStructuredResultParser.Extract<CreateThemeResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(
			because: "a missing environment name is an expected, caller-actionable validation error");
		result.Error.Should().Contain("environment-name is required",
			because: "the failure must name the exact kebab-case field the caller has to add");
	}
}
