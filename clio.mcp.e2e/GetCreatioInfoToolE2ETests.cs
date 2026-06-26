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
	[Description("Advertises describe-environment as a read-only, non-destructive MCP tool through the real MCP server.")]
	[AllureTag(GetCreatioInfoTool.ToolName)]
	[AllureName("describe-environment MCP tool is advertised")]
	public async Task DescribeEnvironment_Should_Be_Advertised() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		McpClientTool tool = tools.Single(tool => tool.Name == GetCreatioInfoTool.ToolName);
		tool.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeTrue(
			because: "describe-environment only reads instance metadata and must be advertised as read-only");
		tool.ProtocolTool.Annotations.DestructiveHint.Should().BeFalse(
			because: "describe-environment must not mutate Creatio state");
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
