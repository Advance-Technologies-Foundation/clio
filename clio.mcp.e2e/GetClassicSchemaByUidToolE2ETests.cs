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
/// End-to-end tests for the get-classic-schema-by-uid MCP tool (read a Classic schema schema by UId).
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(GetClassicSchemaByUidTool.ToolName)]
[NonParallelizable]
public sealed class GetClassicSchemaByUidToolE2ETests : McpContractFixtureBase {

	[Test]
	[Description("Exposes get-classic-schema-by-uid as a discoverable, non-destructive tool via the get-tool-contract compact index on the lazy MCP surface.")]
	[AllureTag(GetClassicSchemaByUidTool.ToolName)]
	[AllureName("get-classic-schema-by-uid MCP tool is discoverable on the lazy surface")]
	public async Task GetClassicSchemaByUid_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames = await arrangeContext.Session.ListReachableToolNamesAsync(
			arrangeContext.CancellationTokenSource.Token);
		IReadOnlyList<ToolContractIndexEntry> index = await arrangeContext.Session.GetToolContractIndexAsync(
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(GetClassicSchemaByUidTool.ToolName,
			because: $"the {GetClassicSchemaByUidTool.ToolName} MCP tool must be discoverable on the lazy surface (get-tool-contract compact index)");
		ToolContractIndexEntry entry = index.Should()
			.ContainSingle(entry => entry.Name == GetClassicSchemaByUidTool.ToolName,
				because: "the compact discovery index must carry exactly one entry for get-classic-schema-by-uid")
			.Which;
		entry.Destructive.Should().NotBe(true,
			because: "get-classic-schema-by-uid is a read-only tool and must not be flagged destructive in the discovery index");
	}

	[Test]
	[Description("Binds get-classic-schema-by-uid arguments through the real MCP server and returns a structured failure for an unknown environment.")]
	[AllureTag(GetClassicSchemaByUidTool.ToolName)]
	[AllureName("get-classic-schema-by-uid MCP tool binds arguments")]
	public async Task GetClassicSchemaByUid_Should_Bind_Arguments_And_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-classic-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			GetClassicSchemaByUidTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-uid"] = Guid.NewGuid().ToString(),
					["environment-name"] = invalidEnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		GetClassicSchemaByUidResponse response = EntitySchemaStructuredResultParser.Extract<GetClassicSchemaByUidResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "valid get-classic-schema-by-uid payloads should bind and return a structured tool response");
		response.Success.Should().BeFalse(
			because: "an unknown registered environment should fail inside tool execution");
		response.Error.Should().Contain(invalidEnvironmentName,
			because: "the structured failure should identify the missing environment name");
	}
}
