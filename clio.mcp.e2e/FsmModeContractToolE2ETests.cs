using System.Text.RegularExpressions;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Stand-free end-to-end contract tests for FSM mode MCP tools.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("fsm")]
[Category("McpE2E.NoEnvironment")]
[Parallelizable(ParallelScope.Self)]
public sealed class FsmModeContractToolE2ETests : McpContractFixtureBase
{
	private const string GetToolName = FsmModeTool.GetFsmModeToolName;
	private const string SetToolName = FsmModeTool.SetFsmModeToolName;

	[Test]
	[AllureTag(GetToolName)]
	[AllureDescription("Starts the real clio MCP server, invokes get-fsm-mode with an invalid environment name, and verifies that the tool fails with readable diagnostics.")]
	[AllureName("Get FSM mode reports invalid environment failures")]
	[Description("Reports invalid environment failures for get-fsm-mode through the real MCP server.")]
	public async Task GetFsmMode_Should_Report_Invalid_Environment_Failure()
	{
		// Arrange
		await using var arrangeContext = Arrange();
		string invalidEnvironmentName = $"missing-fsm-status-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await ActGetAsync(arrangeContext, invalidEnvironmentName);

		// Assert
		AssertStatusToolFailed(callResult);
		AssertFailureTextMentionsEnvironment(callResult, invalidEnvironmentName);
	}

	[Test]
	[AllureTag(SetToolName)]
	[AllureDescription("Starts the real clio MCP server, invokes set-fsm-mode with an invalid environment name, and verifies that the command fails with readable diagnostics.")]
	[AllureName("Set FSM mode reports invalid environment failures")]
	[Description("Reports invalid environment failures for set-fsm-mode through the real MCP server.")]
	public async Task SetFsmMode_Should_Report_Invalid_Environment_Failure()
	{
		// Arrange
		await using var arrangeContext = Arrange();
		string invalidEnvironmentName = $"missing-fsm-env-{Guid.NewGuid():N}";

		// Act
		CommandExecutionActResult actResult = await ActSetAsync(arrangeContext, invalidEnvironmentName, "on");

		// Assert
		AssertCommandToolFailed(actResult);
		AssertFailureIncludesErrorMessage(actResult, "failed set-fsm-mode execution should emit error diagnostics");
		AssertFailureMentionsEnvironment(actResult, invalidEnvironmentName);
	}

	private static async Task<CallToolResult> ActGetAsync(ArrangeContext arrangeContext, string environmentName)
	{
		return await AllureApi.Step("Act by invoking get-fsm-mode through MCP", async () =>
		{
			IReadOnlyCollection<string> toolNames =
				await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);
			toolNames.Should().Contain(GetToolName,
				because: "the get-fsm-mode MCP tool must be discoverable via the get-tool-contract compact index before the end-to-end call can be executed");

			return await arrangeContext.Session.CallToolAsync(
				GetToolName,
				new Dictionary<string, object?> { ["environmentName"] = environmentName },
				arrangeContext.CancellationTokenSource.Token);
		});
	}

	private static async Task<CommandExecutionActResult> ActSetAsync(
		ArrangeContext arrangeContext,
		string environmentName,
		string mode)
	{
		return await AllureApi.Step("Act by invoking set-fsm-mode through MCP", async () =>
		{
			IReadOnlyCollection<string> toolNames =
				await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);
			toolNames.Should().Contain(SetToolName,
				because: "the set-fsm-mode MCP tool must be discoverable via the get-tool-contract compact index before the end-to-end call can be executed");

			CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
				SetToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["mode"] = mode
					}
				},
				arrangeContext.CancellationTokenSource.Token);
			CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
			return new CommandExecutionActResult(callResult, execution);
		});
	}

	[AllureStep("Assert get-fsm-mode call failed for invalid environment")]
	private static void AssertStatusToolFailed(CallToolResult callResult)
	{
		callResult.IsError.Should().BeTrue(
			because: "get-fsm-mode should fail when the requested environment is not registered");
	}

	[AllureStep("Assert get-fsm-mode error mentions the requested environment")]
	private static void AssertFailureTextMentionsEnvironment(CallToolResult callResult, string invalidEnvironmentName)
	{
		string text = string.Join(
			Environment.NewLine,
			callResult.Content?.Select(content => content?.ToString() ?? string.Empty) ?? []);
		// get-fsm-mode is a hidden long-tail tool routed through the clio-run executor, so an
		// invocation-layer failure may surface either as the native SDK diagnostic or as the
		// executor-wrapped "Error: tool '<name>' failed:" text.
		text.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found|not registered|error occurred invoking.*{Regex.Escape(GetToolName)}|tool '{Regex.Escape(GetToolName)}' failed)",
			because: "the failure should either identify the requested environment or clearly identify the failed MCP tool invocation");
	}

	[AllureStep("Assert set-fsm-mode command failed")]
	private static void AssertCommandToolFailed(CommandExecutionActResult actResult)
	{
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: "set-fsm-mode command failures should be returned as a normal command execution envelope");
		actResult.Execution.ExitCode.Should().NotBe(0,
			because: "set-fsm-mode should fail when the requested environment is not registered");
	}

	[AllureStep("Assert failed set-fsm-mode output contains errors")]
	private static void AssertFailureIncludesErrorMessage(CommandExecutionActResult actResult, string because)
	{
		actResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "failed set-fsm-mode execution should emit human-readable diagnostics");
		actResult.Execution.Output!.Should().Contain(message => message.MessageType == Clio.Common.LogDecoratorType.Error,
			because: because);
	}

	[AllureStep("Assert set-fsm-mode failure mentions the requested environment")]
	private static void AssertFailureMentionsEnvironment(CommandExecutionActResult actResult, string invalidEnvironmentName)
	{
		string combinedOutput = string.Join(
			Environment.NewLine,
			actResult.Execution.Output?.Select(message => $"{message.MessageType}: {message.Value}") ?? []);
		combinedOutput.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found|not registered)",
			because: "the failure should help a human understand that the requested environment is not registered");
	}

	private sealed record CommandExecutionActResult(CallToolResult CallResult, CommandExecutionEnvelope Execution);
}
