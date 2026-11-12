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

	[Test]
	[AllureTag(GetToolName)]
	[AllureDescription("Starts the real clio MCP server and calls get-fsm-mode directly with environment-name, the spelling its args-record siblings use, instead of its flat environmentName. The call is refused before any environment is read: the refusal names environmentName and points environment-name at it.")]
	[AllureName("Get FSM mode names environmentName when called with environment-name")]
	[Description("A direct get-fsm-mode call that sends environment-name is refused over the real MCP server with environmentName named as missing and a did-you-mean for the key that was sent, not with the SDK's binder text.")]
	public async Task GetFsmMode_Should_Name_The_Missing_Argument_When_Called_With_Environment_Name()
	{
		// Arrange
		await using var arrangeContext = Arrange();

		// Act
		CallToolResult callResult = await AllureApi.Step("Act by calling get-fsm-mode directly with environment-name", async () =>
			await arrangeContext.Session.CallToolAsync(
				GetToolName,
				new Dictionary<string, object?> { ["environment-name"] = "any-environment" },
				arrangeContext.CancellationTokenSource.Token));

		// Assert
		AssertMissingEnvironmentNameRefusal(callResult);
	}

	[Test]
	[AllureTag(GetToolName)]
	[AllureTag(ClioRunTool.ToolName)]
	[AllureDescription("Starts the real clio MCP server and dispatches get-fsm-mode through clio-run with args {\"environment-name\": ...}. The nested dispatch is refused before any environment is read, with the same refusal as the direct call.")]
	[AllureName("clio-run get-fsm-mode names environmentName when args use environment-name")]
	[Description("get-fsm-mode dispatched through clio-run with environment-name is refused over the real MCP server with environmentName named as missing and a did-you-mean, not with 'tool failed' and the SDK's binder text.")]
	public async Task ClioRun_GetFsmMode_Should_Name_The_Missing_Argument_When_Args_Use_Environment_Name()
	{
		// Arrange
		await using var arrangeContext = Arrange();

		// Act
		CallToolResult callResult = await AllureApi.Step("Act by dispatching get-fsm-mode through clio-run with environment-name", async () =>
			await arrangeContext.Session.CallToolAsync(
				ClioRunTool.ToolName,
				new Dictionary<string, object?> {
					["command"] = GetToolName,
					["args"] = new Dictionary<string, object?> { ["environment-name"] = "any-environment" }
				},
				arrangeContext.CancellationTokenSource.Token));

		// Assert
		AssertMissingEnvironmentNameRefusal(callResult);
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

	[AllureStep("Assert the call is refused with environmentName named and environment-name pointed at it")]
	private static void AssertMissingEnvironmentNameRefusal(CallToolResult callResult)
	{
		string text = string.Join(" ", callResult.Content?.OfType<TextContentBlock>().Select(block => block.Text) ?? []);
		callResult.IsError.Should().BeTrue(
			because: "a call that leaves out get-fsm-mode's required argument must not run");
		text.Should().Contain(
			"invalid-parameter-type: argument 'environmentName' for MCP tool 'get-fsm-mode' is required and was not sent.",
			because: "the lead sentence names the argument the tool needs, in the stable invalid-parameter-type form");
		text.Should().Contain("Unknown argument 'environment-name' (did you mean 'environmentName'?).",
			because: "the key the caller sent is the one to rename, and the refusal says to what");
		text.Should().NotContain("arguments dictionary",
			because: "the SDK's binder text is what this refusal replaces on every path");
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
