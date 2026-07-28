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
/// End-to-end coverage for the check-theming-access MCP tool. Actually probing rights and licenses requires a
/// live Creatio environment, so the hermetic CI-safe assertions are that the real clio MCP server advertises
/// check-theming-access and binds its args wrapper to a structured validation error; the live behavior is
/// covered by <see cref="ThemingSandboxE2ETests"/>.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("check-theming-access")]
[NonParallelizable]
public sealed class CheckThemingAccessToolE2ETests : McpContractFixtureBase {
	[Test]
	[AllureTag(CheckThemingAccessTool.ToolName)]
	[AllureName("check-theming-access tool is discoverable on the lazy surface")]
	[Description("Starts the real clio MCP server and verifies check-theming-access is discoverable via the get-tool-contract compact index on the lazy tool surface.")]
	public async Task CheckThemingAccess_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(CheckThemingAccessTool.ToolName,
			because: "the check-theming-access MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list");
	}

	[Test]
	[AllureTag(CheckThemingAccessTool.ToolName)]
	[AllureName("check-theming-access binds the args wrapper and returns a structured validation failure")]
	[Description("Calls check-theming-access through the real clio MCP server with an empty args object and verifies the structured kebab-case validation error names environment-name — proving the args wrapper binds without a live Creatio environment.")]
	public async Task CheckThemingAccess_Should_Return_Structured_Validation_Failure_When_Args_Are_Empty() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			CheckThemingAccessTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?>()
			},
			context.CancellationTokenSource.Token);
		ThemingAccessResult result = EntitySchemaStructuredResultParser.Extract<ThemingAccessResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(
			because: "an access check without an environment name is invalid");
		result.Error.Should().Contain("environment-name",
			because: "the failure must name the exact kebab-case field the caller has to add");
	}
}
