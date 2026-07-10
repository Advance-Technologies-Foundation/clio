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
/// End-to-end coverage for the list-themes MCP tool. Actually listing themes requires a live Creatio
/// environment with branding licensing, so the hermetic CI-safe assertions are that the real clio MCP
/// server advertises list-themes and binds its args wrapper to a structured validation error; the live
/// behavior is covered by <see cref="ThemingSandboxE2ETests"/>.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("list-themes")]
[NonParallelizable]
public sealed class ListThemesToolE2ETests : McpContractFixtureBase {
	[Test]
	[AllureTag(ListThemesTool.ToolName)]
	[AllureName("list-themes tool is discoverable on the lazy surface")]
	[Description("Starts the real clio MCP server and verifies list-themes is discoverable via the get-tool-contract compact index on the lazy tool surface.")]
	public async Task ListThemes_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ListThemesTool.ToolName,
			because: "the list-themes MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list");
	}

	[Test]
	[AllureTag(ListThemesTool.ToolName)]
	[AllureName("list-themes binds the args wrapper and returns a structured validation failure")]
	[Description("Calls list-themes through the real clio MCP server with an empty args object and verifies the structured kebab-case validation error names environment-name — proving the args wrapper binds without a live Creatio environment.")]
	public async Task ListThemes_Should_Return_Structured_Validation_Failure_When_Args_Are_Empty() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ListThemesTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?>()
			},
			context.CancellationTokenSource.Token);
		ListThemesResult result = EntitySchemaStructuredResultParser.Extract<ListThemesResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(
			because: "a list request without an environment name is invalid");
		result.Error.Should().Contain("environment-name",
			because: "the failure must name the exact kebab-case field the caller has to add");
	}
}
