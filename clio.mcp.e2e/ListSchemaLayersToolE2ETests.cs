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
/// End-to-end tests for the list-schema-layers MCP tool (enumerate a schema's package layers).
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ListSchemaLayersTool.ToolName)]
[NonParallelizable]
public sealed class ListSchemaLayersToolE2ETests : McpContractFixtureBase {

	[Test]
	[Description("Exposes list-schema-layers as a discoverable, non-destructive tool via the get-tool-contract compact index on the lazy MCP surface.")]
	[AllureTag(ListSchemaLayersTool.ToolName)]
	[AllureName("list-schema-layers MCP tool is discoverable on the lazy surface")]
	public async Task ListSchemaLayers_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames = await arrangeContext.Session.ListReachableToolNamesAsync(
			arrangeContext.CancellationTokenSource.Token);
		IReadOnlyList<ToolContractIndexEntry> index = await arrangeContext.Session.GetToolContractIndexAsync(
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ListSchemaLayersTool.ToolName,
			because: $"the {ListSchemaLayersTool.ToolName} MCP tool must be discoverable on the lazy surface (get-tool-contract compact index)");
		ToolContractIndexEntry entry = index.Should()
			.ContainSingle(entry => entry.Name == ListSchemaLayersTool.ToolName,
				because: "the compact discovery index must carry exactly one entry for list-schema-layers")
			.Which;
		entry.Destructive.Should().NotBe(true,
			because: "list-schema-layers is a read-only tool and must not be flagged destructive in the discovery index");
	}

	[Test]
	[Description("Binds list-schema-layers arguments through the real MCP server and returns a structured failure for an unknown environment.")]
	[AllureTag(ListSchemaLayersTool.ToolName)]
	[AllureName("list-schema-layers MCP tool binds arguments")]
	public async Task ListSchemaLayers_Should_Bind_Arguments_And_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-layers-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ListSchemaLayersTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "ContractPageV2",
					["environment-name"] = invalidEnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		ListSchemaLayersResponse response = EntitySchemaStructuredResultParser.Extract<ListSchemaLayersResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "valid list-schema-layers payloads should bind and return a structured tool response");
		response.Success.Should().BeFalse(
			because: "an unknown registered environment should fail inside tool execution");
		response.Error.Should().Contain(invalidEnvironmentName,
			because: "the structured failure should identify the missing environment name");
	}
}
