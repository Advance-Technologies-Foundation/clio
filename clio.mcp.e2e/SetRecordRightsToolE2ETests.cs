using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the set-record-rights MCP tool. Mutating real record rights requires a live Creatio
/// environment, so the hermetic CI-safe assertions are that the real clio MCP server advertises
/// set-record-rights as a destructive tool and binds its args wrapper to a structured failure against a
/// missing environment — execution fails before any rights write, so no live mutation occurs.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(SetRecordRightsTool.ToolName)]
[NonParallelizable]
public sealed class SetRecordRightsToolE2ETests : McpContractFixtureBase {
	[Test]
	[Description("Exposes set-record-rights as a discoverable, destructive tool via the get-tool-contract compact index on the lazy MCP surface.")]
	[AllureTag(SetRecordRightsTool.ToolName)]
	[AllureName("set-record-rights MCP tool is discoverable and destructive on the lazy surface")]
	public async Task SetRecordRights_Should_Be_Discoverable_And_Destructive_On_Lazy_Surface() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames = await arrangeContext.Session.ListReachableToolNamesAsync(
			arrangeContext.CancellationTokenSource.Token);
		IReadOnlyList<ToolContractIndexEntry> index = await arrangeContext.Session.GetToolContractIndexAsync(
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(SetRecordRightsTool.ToolName,
			because: $"the {SetRecordRightsTool.ToolName} MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list");
		ToolContractIndexEntry entry = index.Should()
			.ContainSingle(entry => entry.Name == SetRecordRightsTool.ToolName,
				because: "the compact discovery index must carry exactly one entry for set-record-rights")
			.Which;
		entry.Destructive.Should().Be(true,
			because: "set-record-rights grants or revokes a record right and must be flagged destructive in the discovery index");
	}

	[Test]
	[Description("Binds set-record-rights arguments through the real MCP server and returns a structured failure for an unknown environment before any rights write.")]
	[AllureTag(SetRecordRightsTool.ToolName)]
	[AllureName("set-record-rights MCP tool binds arguments")]
	public async Task SetRecordRights_Should_Bind_Arguments_And_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-setrr-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			SetRecordRightsTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["entity"] = "Contact",
					["record-id"] = Guid.NewGuid().ToString(),
					["grantee"] = Guid.NewGuid().ToString(),
					["operation"] = "read"
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		SetRecordRightsResponse response = EntitySchemaStructuredResultParser.Extract<SetRecordRightsResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "valid set-record-rights payloads should bind and return a structured tool response");
		response.Success.Should().BeFalse(
			because: "an unknown registered environment should fail inside tool execution before any rights write");
		response.Error.Should().Contain(invalidEnvironmentName,
			because: "the structured failure should identify the missing environment name");
	}

}
