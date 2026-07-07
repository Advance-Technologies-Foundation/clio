using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the get-user-culture MCP tool. NOT part of CI — run manually against a
/// real clio mcp-server process.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(GetUserCultureTool.ToolName)]
[NonParallelizable]
public sealed class GetUserCultureToolE2ETests : McpContractFixtureBase {
	[Test]
	[Description("Exposes get-user-culture via the get-tool-contract compact index with a non-destructive safety flag on the lazy tool surface.")]
	[AllureTag(GetUserCultureTool.ToolName)]
	[AllureName("get-user-culture MCP tool is discoverable on the lazy surface")]
	public async Task GetUserCulture_Should_Be_Advertised() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyList<ToolContractIndexEntry> index = await arrangeContext.Session.GetToolContractIndexAsync(
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		// The lazy surface exposes hidden tools only through the compact discovery index, which carries the
		// destructive flag; the read-only hint is no longer observable for non-resident tools.
		ToolContractIndexEntry indexEntry = index.Should().ContainSingle(entry => entry.Name == GetUserCultureTool.ToolName,
			because: "get-user-culture must be discoverable via the get-tool-contract compact index on the lazy surface")
			.Which;
		indexEntry.Destructive.Should().NotBe(true,
			because: "get-user-culture only reads the profile culture and must not be flagged destructive");
	}

	[Test]
	[Description("Binds get-user-culture arguments through the real MCP server and returns a structured failure signal for an unknown environment.")]
	[AllureTag(GetUserCultureTool.ToolName)]
	[AllureName("get-user-culture MCP tool binds arguments")]
	public async Task GetUserCulture_Should_Bind_Arguments_And_Report_Failure_For_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-culture-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			GetUserCultureTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		GetUserCultureResponse response = EntitySchemaStructuredResultParser.Extract<GetUserCultureResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "valid get-user-culture payloads should bind and return a structured tool response, not a protocol error");
		response.Success.Should().BeFalse(
			because: "an unknown registered environment cannot yield a profile culture");
		response.Reason.Should().NotBeNullOrWhiteSpace(
			because: "the failure signal must carry a machine-readable reason so the agent can ask the user");
		response.Culture.Should().BeNull(
			because: "a failure signal must never surface a fallback culture as if it were resolved");
	}

}
