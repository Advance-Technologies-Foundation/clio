using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the get-record-rights MCP tool. Reading real record rights requires a live Creatio
/// environment, so the hermetic CI-safe assertions are that the real clio MCP server advertises
/// get-record-rights as a non-destructive tool and binds its args wrapper to a structured failure against a
/// missing environment.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(GetRecordRightsTool.ToolName)]
[NonParallelizable]
public sealed class GetRecordRightsToolE2ETests : McpContractFixtureBase {
	[Test]
	[Description("Exposes get-record-rights as a discoverable, non-destructive tool via the get-tool-contract compact index on the lazy MCP surface.")]
	[AllureTag(GetRecordRightsTool.ToolName)]
	[AllureName("get-record-rights MCP tool is discoverable on the lazy surface")]
	public async Task GetRecordRights_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames = await arrangeContext.Session.ListReachableToolNamesAsync(
			arrangeContext.CancellationTokenSource.Token);
		IReadOnlyList<ToolContractIndexEntry> index = await arrangeContext.Session.GetToolContractIndexAsync(
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(GetRecordRightsTool.ToolName,
			because: $"the {GetRecordRightsTool.ToolName} MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list");
		ToolContractIndexEntry entry = index.Should()
			.ContainSingle(entry => entry.Name == GetRecordRightsTool.ToolName,
				because: "the compact discovery index must carry exactly one entry for get-record-rights")
			.Which;
		entry.Destructive.Should().NotBe(true,
			because: "get-record-rights is a read-only tool and must not be flagged destructive in the discovery index");
	}

	[Test]
	[Description("Binds get-record-rights arguments through the real MCP server and returns a structured failure for an unknown environment.")]
	[AllureTag(GetRecordRightsTool.ToolName)]
	[AllureName("get-record-rights MCP tool binds arguments")]
	public async Task GetRecordRights_Should_Bind_Arguments_And_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-getrr-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			GetRecordRightsTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["entity"] = "Contact",
					["record-id"] = Guid.NewGuid().ToString()
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		GetRecordRightsResponse response = EntitySchemaStructuredResultParser.Extract<GetRecordRightsResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "valid get-record-rights payloads should bind and return a structured tool response");
		response.Success.Should().BeFalse(
			because: "an unknown registered environment should fail inside tool execution");
		response.Error.Should().Contain(invalidEnvironmentName,
			because: "the structured failure should identify the missing environment name");
	}

}
