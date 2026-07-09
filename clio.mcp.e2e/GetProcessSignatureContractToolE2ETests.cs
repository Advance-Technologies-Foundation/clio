using System.Text.RegularExpressions;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Stand-free end-to-end contract tests for the get-process-signature MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("get-process-signature")]
[Category("McpE2E.NoEnvironment")]
[Parallelizable(ParallelScope.Self)]
public sealed class GetProcessSignatureContractToolE2ETests : McpContractFixtureBase {
	private const string ToolName = GetProcessSignatureTool.ToolName;

	[Test]
	[Description("Starts the real clio MCP server, invokes get-process-signature with an invalid environment name, and verifies a readable structured failure.")]
	[AllureTag(ToolName)]
	[AllureName("Get process signature reports invalid environment failures")]
	[AllureDescription("Uses the real clio MCP server to call get-process-signature with an unknown environment name and verifies that the MCP result stays structured and reports a human-readable failure with success=false.")]
	public async Task GetProcessSignature_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		string invalidEnvironmentName = $"missing-gps-env-{Guid.NewGuid():N}";
		await using ArrangeContext arrangeContext = Arrange(TimeSpan.FromMinutes(5));

		// Act
		GetProcessSignatureEnvelope envelope = await ActAsync(arrangeContext, "UsrMissingProcess", invalidEnvironmentName);

		// Assert
		envelope.Success.Should().BeFalse(
			because: "an unknown environment cannot resolve a process signature");
		envelope.Error.Should().NotBeNullOrWhiteSpace(
			because: "failed signature lookups should carry a human-readable error");
		envelope.Error!.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found|not registered)",
			because: "the failure should help a human understand that the requested environment is not registered");
	}

	private static async Task<GetProcessSignatureEnvelope> ActAsync(
		ArrangeContext arrangeContext,
		string processName,
		string environmentName) {
		IReadOnlyCollection<string> toolNames = await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);
		toolNames.Should().Contain(ToolName,
			because: "the get-process-signature MCP tool must be discoverable via the get-tool-contract compact index on the lazy surface before the end-to-end call can be executed");

		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["process-name"] = processName,
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);

		return GetProcessSignatureResultParser.Extract(callResult);
	}
}
