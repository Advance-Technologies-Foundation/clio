using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the describe-environment MCP tool. NOT part of CI — run manually against a
/// real clio mcp-server process.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(GetCreatioInfoTool.ToolName)]
[NonParallelizable]
public sealed class GetCreatioInfoToolE2ETests : McpContractFixtureBase {
	[Test]
	[Description("Exposes describe-environment via the get-tool-contract compact index with a non-destructive safety flag on the lazy tool surface.")]
	[AllureTag(GetCreatioInfoTool.ToolName)]
	[AllureName("describe-environment MCP tool is discoverable on the lazy surface")]
	public async Task DescribeEnvironment_Should_Be_Advertised() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyList<ToolContractIndexEntry> index = await arrangeContext.Session.GetToolContractIndexAsync(
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		// The lazy surface exposes hidden tools only through the compact discovery index, which carries the
		// destructive flag; the read-only hint is no longer observable for non-resident tools.
		ToolContractIndexEntry entry = index.Should().ContainSingle(entry => entry.Name == GetCreatioInfoTool.ToolName,
			because: "describe-environment must be discoverable via the get-tool-contract compact index on the lazy surface")
			.Which;
		entry.Destructive.Should().NotBe(true,
			because: "describe-environment only reads instance metadata and must not be flagged destructive");
	}

	[Test]
	[Description("Binds describe-environment arguments through the real MCP server and returns a structured exit-code-1 failure for an unknown environment.")]
	[AllureTag(GetCreatioInfoTool.ToolName)]
	[AllureName("describe-environment MCP tool binds arguments")]
	public async Task DescribeEnvironment_Should_Bind_Arguments_And_Report_Failure_For_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-describe-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			GetCreatioInfoTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope response = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "valid describe-environment payloads should bind and return a structured execution result, not a protocol error");
		response.ExitCode.Should().Be(1,
			because: "an unknown registered environment is an expected, caller-actionable failure (exit code 1)");
	}
}
