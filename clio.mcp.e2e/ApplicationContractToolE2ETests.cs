using System.Text.Json;
using System.Text.RegularExpressions;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Stand-free end-to-end contract tests for application MCP tools.
/// </summary>
[TestFixture]
[AllureNUnit]
[Category("McpE2E.NoEnvironment")]
[Parallelizable(ParallelScope.Self)]
public sealed class ApplicationContractToolE2ETests : McpContractFixtureBase {
	private const string DeleteToolName = ApplicationDeleteTool.ToolName;

	[Test]
	[Description("Exposes delete-app via the get-tool-contract compact index so callers can discover the uninstall tool on the lazy surface.")]
	[AllureFeature(DeleteToolName)]
	[AllureTag(DeleteToolName)]
	[AllureName("Application delete tool is discoverable on the lazy surface")]
	[AllureDescription("Starts the real clio MCP server and verifies that delete-app is discoverable via the get-tool-contract compact index.")]
	public async Task ApplicationDelete_Should_Be_Listed_By_Mcp_Server() {
		// Arrange
		await using ArrangeContext arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(DeleteToolName,
			because: $"the {DeleteToolName} MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list, so MCP callers can discover the uninstall tool");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes delete-app with an unknown environment, and verifies that the failure remains human-readable.")]
	[AllureFeature(DeleteToolName)]
	[AllureTag(DeleteToolName)]
	[AllureName("Application delete reports invalid environment failures")]
	[AllureDescription("Uses the real clio MCP server to call delete-app with an unknown environment and verifies that the tool returns a structured readable error envelope.")]
	public async Task ApplicationDelete_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		await using ArrangeContext arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-delete-app-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			DeleteToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["app-name"] = "11111111-1111-1111-1111-111111111111"
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		ApplicationDeleteResponseEnvelope response = ApplicationResultParser.ExtractDelete(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: $"structured delete-app failures should be returned in the payload instead of as MCP invocation errors. Actual result: {DescribeCallResult(callResult)}");
		response.Success.Should().BeFalse(
			because: "delete-app should fail when the requested environment does not exist");
		response.Error.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found)",
			because: "the failure should explain that the requested environment is missing");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes delete-app without environment-name or explicit connection args, and verifies that the failure explains the missing target.")]
	[AllureFeature(DeleteToolName)]
	[AllureTag(DeleteToolName)]
	[AllureName("Application delete rejects missing execution target")]
	[AllureDescription("Uses the real clio MCP server to call delete-app without environment-name or URI credentials and verifies that the tool returns readable resolver diagnostics.")]
	public async Task ApplicationDelete_Should_Reject_Missing_Execution_Target() {
		// Arrange
		await using ArrangeContext arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			DeleteToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["app-name"] = "11111111-1111-1111-1111-111111111111"
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		ApplicationDeleteResponseEnvelope response = ApplicationResultParser.ExtractDelete(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: $"structured delete-app failures should be returned in the payload instead of as MCP invocation errors. Actual result: {DescribeCallResult(callResult)}");
		response.Success.Should().BeFalse(
			because: "delete-app should fail when the call does not identify any execution target");
		response.Error.Should().Contain("Either a configured environment name or an explicit URI is required",
			because: "the failure should explain that the MCP request needs an environment-name or explicit URI");
	}

	private static string DescribeCallResult(CallToolResult callResult) =>
		JsonSerializer.Serialize(new { callResult.IsError, callResult.StructuredContent, callResult.Content });
}
