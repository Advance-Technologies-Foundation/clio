using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the place-section-in-workplace MCP tool that need no Creatio environment: the tool is
/// reachable on the lazy surface, it has a curated contract, and its args wrapper binds to a structured failure.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("place-section-in-workplace")]
[Parallelizable(ParallelScope.Self)]
public sealed class PlaceSectionInWorkplaceToolE2ETests : McpContractFixtureBase {

	private const string ToolName = PlaceSectionInWorkplaceTool.ToolName;

	[Test]
	[AllureTag(ToolName)]
	[AllureName("place-section-in-workplace is discoverable on the lazy surface")]
	[Description("Starts the real clio MCP server and verifies place-section-in-workplace is discoverable via the get-tool-contract compact index.")]
	public async Task PlaceSectionInWorkplace_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		await using ArrangeContext context = Arrange();

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: "the tool must be reachable on the lazy surface even though it is not resident in tools/list");
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureName("place-section-in-workplace binds the args wrapper and returns a structured validation failure")]
	[Description("Calls the tool through the real MCP server without section-code and verifies the structured failure names the missing field.")]
	public async Task PlaceSectionInWorkplace_Should_Return_Structured_Failure_When_Section_Code_Is_Missing() {
		// Arrange
		await using ArrangeContext context = Arrange();

		// Act
		CallToolResult callResult = await context.Session.CallDestructiveAsync(ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = "any-environment",
				["workplace-name"] = "Orders"
			},
			context.CancellationTokenSource.Token);
		PlaceSectionInWorkplaceResponse result =
			EntitySchemaStructuredResultParser.Extract<PlaceSectionInWorkplaceResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(because: "a section cannot be placed without its code");
		result.Error.Should().Contain("section-code", because: "the failure names the field the caller has to add");
		result.WritesStarted.Should().BeNull(because: "an argument failure comes before the first write, so nothing changed");
	}
}
