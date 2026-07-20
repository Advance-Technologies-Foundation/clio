using System.Text.RegularExpressions;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the restart-by-environment-name MCP tool (ENG-91315).
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("restart-web-app")]
[Parallelizable(ParallelScope.Self)]
public sealed class RestartToolE2ETests : McpContractFixtureBase
{
	private const string ToolName = RestartTool.RestartByEnvironmentNameToolName;

	[Test]
	[AllureTag(ToolName)]
	[AllureDescription("Starts the real clio MCP server, invokes restart-by-environment-name with an invalid environment name, and verifies that the tool fails fast with readable diagnostics instead of engaging the readiness wait.")]
	[AllureName("Restart by environment name reports invalid environment failures")]
	[Description("Reports invalid environment failures for restart-by-environment-name through the real MCP server.")]
	public async Task RestartInstanceByName_Should_Report_Invalid_Environment_Failure()
	{
		// Arrange
		await using var arrangeContext = Arrange();
		string invalidEnvironmentName = $"missing-restart-env-{Guid.NewGuid():N}";

		// Act
		RestartActResult actResult = await ActAsync(arrangeContext, invalidEnvironmentName);

		// Assert
		AssertToolCallFailed(actResult);
		AssertFailureIncludesErrorMessage(actResult);
		AssertFailureMentionsEnvironment(actResult, invalidEnvironmentName);
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureDescription("Starts the real clio MCP server and verifies get-tool-contract advertises the waitReady/waitTimeoutSeconds arguments for restart-by-environment-name (ENG-91315).")]
	[AllureName("Restart by environment name contract exposes waitReady and waitTimeoutSeconds")]
	[Description("get-tool-contract advertises waitReady and waitTimeoutSeconds for restart-by-environment-name.")]
	public async Task RestartInstanceByName_Contract_Should_Expose_WaitReady_And_WaitTimeoutSeconds()
	{
		// Arrange
		await using var arrangeContext = Arrange();

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["tool-names"] = new[] { ToolName }
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		ToolContractGetResponse response = EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(callResult);

		// Assert
		response.Success.Should().BeTrue(because: "the contract lookup for a known tool must succeed");
		response.Tools.Should().NotBeNull();
		ToolContractDefinition contract = response.Tools!.Single(tool => tool.Name == ToolName);
		contract.InputSchema.Properties.Should().Contain(field => field.Name == "waitReady",
			because: "agents must be able to discover the readiness-wait toggle without reading source code");
		contract.InputSchema.Properties.Should().Contain(field => field.Name == "waitTimeoutSeconds",
			because: "agents must be able to discover the readiness-wait timeout budget without reading source code");
	}

	private static async Task<RestartActResult> ActAsync(
		ArrangeContext arrangeContext,
		string environmentName)
	{
		return await AllureApi.Step("Act by invoking restart-by-environment-name through MCP", async () =>
		{
			IReadOnlyCollection<string> toolNames =
				await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);
			toolNames.Should().Contain(ToolName,
				because: "the restart-by-environment-name MCP tool must be discoverable via the get-tool-contract compact index before the end-to-end call can be executed");

			CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
				ToolName,
				new Dictionary<string, object?> { ["environmentName"] = environmentName },
				arrangeContext.CancellationTokenSource.Token);
			CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
			return new RestartActResult(callResult, execution);
		});
	}

	[AllureStep("Assert restart-by-environment-name tool call failed")]
	private static void AssertToolCallFailed(RestartActResult actResult)
	{
		(actResult.CallResult.IsError == true || actResult.Execution.ExitCode != 0).Should().BeTrue(
			because: "an invalid restart-by-environment-name request should fail instead of succeeding silently");
	}

	[AllureStep("Assert restart-by-environment-name failure output contains Error message")]
	private static void AssertFailureIncludesErrorMessage(RestartActResult actResult)
	{
		actResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "failed restart-by-environment-name execution should emit human-readable diagnostics");
		actResult.Execution.Output!.Should().Contain(message => message.MessageType == LogDecoratorType.Error,
			because: "failed restart-by-environment-name execution should report error-level diagnostics");
	}

	[AllureStep("Assert restart-by-environment-name failure mentions the invalid environment")]
	private static void AssertFailureMentionsEnvironment(RestartActResult actResult, string environmentName)
	{
		string combinedOutput = string.Join(
			Environment.NewLine,
			(actResult.Execution.Output ?? []).Select(message => $"{message.MessageType}: {message.Value}"));

		combinedOutput.Should().MatchRegex(
			$"(?is)({Regex.Escape(environmentName)}|environment.*not.*found|not found|error occurred invoking)",
			because: "the failure should help a human understand that the requested environment is not registered");
	}

	private sealed record RestartActResult(
		CallToolResult CallResult,
		CommandExecutionEnvelope Execution);
}
