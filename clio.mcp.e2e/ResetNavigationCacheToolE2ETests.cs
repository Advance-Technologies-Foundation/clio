using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the reset-navigation-cache MCP tool that need no Creatio environment: the tool is
/// reachable on the lazy surface and its args wrapper binds to a structured validation error.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ResetNavigationCacheTool.ToolName)]
[Parallelizable(ParallelScope.Self)]
public sealed class ResetNavigationCacheToolE2ETests : McpContractFixtureBase {

	private const string ToolName = ResetNavigationCacheTool.ToolName;

	[Test]
	[AllureTag(ToolName)]
	[AllureName("reset-navigation-cache is discoverable on the lazy surface")]
	[Description("Starts the real clio MCP server and verifies reset-navigation-cache is discoverable via the get-tool-contract compact index.")]
	public async Task ResetNavigationCache_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		await using ArrangeContext context = Arrange();

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: "reset-navigation-cache must be reachable on the lazy surface even though it is not resident in tools/list");
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureName("reset-navigation-cache binds the args wrapper and returns a structured validation failure")]
	[Description("Calls reset-navigation-cache through the real MCP server without environment-name and verifies the structured failure names the missing argument.")]
	public async Task ResetNavigationCache_Should_Return_Structured_Validation_Failure_When_Environment_Is_Missing() {
		// Arrange
		await using ArrangeContext context = Arrange();

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(ToolName,
			new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?>() },
			context.CancellationTokenSource.Token);
		ResetNavigationCacheResponse result =
			EntitySchemaStructuredResultParser.Extract<ResetNavigationCacheResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(because: "on stdio the target environment can only come from the argument");
		result.Error.Should().Contain("environment-name", because: "the failure names the field the caller has to add");
	}
}
