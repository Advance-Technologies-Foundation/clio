using System.Text.RegularExpressions;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for FSM mode MCP tools.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("fsm")]
public sealed class FsmModeToolE2ETests
{
	private const string GetToolName = FsmModeTool.GetFsmModeToolName;
	private const string SetToolName = FsmModeTool.SetFsmModeToolName;

	[Test]
	[AllureTag(GetToolName)]
	[AllureDescription("Starts the real clio MCP server, invokes get-fsm-mode for the configured sandbox environment, and verifies that the structured FSM payload is returned from the live Creatio instance.")]
	[AllureName("Get FSM mode returns live sandbox status")]
	[Description("Returns the current FSM mode for the configured sandbox environment through the real MCP server and live Creatio response.")]
	public async Task GetFsmMode_Should_Return_Live_Sandbox_Status()
	{
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		await using FsmModeArrangeContext arrangeContext = await ArrangeAsync(settings);

		// Act
		CallToolResult callResult = await ActGetAsync(arrangeContext, settings.Sandbox.EnvironmentName!);
		FsmModeStatusEnvelope status = FsmModeStatusResultParser.Extract(callResult);

		// Assert
		AssertStatusToolSucceeded(callResult);
		AssertStructuredStatusReturned(status, settings.Sandbox.EnvironmentName!);
		AssertStatusShapeMatchesMode(status);
	}

	[Test]
	[AllureTag(GetToolName)]
	[AllureDescription("Starts the real clio MCP server, invokes get-fsm-mode with an invalid environment name, and verifies that the tool fails with readable diagnostics.")]
	[AllureName("Get FSM mode reports invalid environment failures")]
	[Description("Reports invalid environment failures for get-fsm-mode through the real MCP server.")]
	public async Task GetFsmMode_Should_Report_Invalid_Environment_Failure()
	{
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		await using FsmModeArrangeContext arrangeContext = await ArrangeAsync(settings);
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
		McpE2ESettings settings = TestConfiguration.Load();
		await using FsmModeArrangeContext arrangeContext = await ArrangeAsync(settings);
		string invalidEnvironmentName = $"missing-fsm-env-{Guid.NewGuid():N}";

		// Act
		CommandExecutionActResult actResult = await ActSetAsync(arrangeContext, invalidEnvironmentName, "on");

		// Assert
		AssertCommandToolFailed(actResult);
		AssertFailureIncludesErrorMessage(actResult, "failed set-fsm-mode execution should emit error diagnostics");
		AssertFailureMentionsEnvironment(actResult, invalidEnvironmentName);
	}

	[AllureStep("Arrange FSM MCP session")]
	private static async Task<FsmModeArrangeContext> ArrangeAsync(McpE2ESettings settings)
	{
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new FsmModeArrangeContext(session, cancellationTokenSource);
	}

	[AllureStep("Act by invoking get-fsm-mode through MCP")]
	private static async Task<CallToolResult> ActGetAsync(FsmModeArrangeContext arrangeContext, string environmentName)
	{
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(GetToolName,
			because: "the get-fsm-mode MCP tool must be advertised before the end-to-end call can be executed");

		return await arrangeContext.Session.CallToolAsync(
			GetToolName,
			new Dictionary<string, object?> { ["environmentName"] = environmentName },
			arrangeContext.CancellationTokenSource.Token);
	}

	[AllureStep("Act by invoking set-fsm-mode through MCP")]
	private static async Task<CommandExecutionActResult> ActSetAsync(
		FsmModeArrangeContext arrangeContext,
		string environmentName,
		string mode)
	{
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(SetToolName,
			because: "the set-fsm-mode MCP tool must be advertised before the end-to-end call can be executed");

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
	}

	[AllureStep("Assert get-fsm-mode call failed for invalid environment")]
	private static void AssertStatusToolFailed(CallToolResult callResult)
	{
		callResult.IsError.Should().BeTrue(
			because: "get-fsm-mode should fail when the requested environment is not registered");
	}

	[AllureStep("Assert get-fsm-mode call succeeded")]
	private static void AssertStatusToolSucceeded(CallToolResult callResult)
	{
		callResult.IsError.Should().NotBeTrue(
			because: "get-fsm-mode should return a normal MCP result for a registered sandbox environment");
	}

	[AllureStep("Assert structured sandbox FSM status is returned")]
	private static void AssertStructuredStatusReturned(FsmModeStatusEnvelope status, string environmentName)
	{
		status.EnvironmentName.Should().Be(environmentName,
			because: "the live MCP result should preserve the requested sandbox environment name");
		status.Mode.Should().MatchRegex("^(on|off)$",
			because: "the live MCP result should report FSM mode as either on or off");
	}

	[AllureStep("Assert live FSM status shape matches the reported mode")]
	private static void AssertStatusShapeMatchesMode(FsmModeStatusEnvelope status)
	{
		if (string.Equals(status.Mode, "on", StringComparison.OrdinalIgnoreCase))
		{
			status.UseStaticFileContent.Should().BeFalse(
				because: "FSM on should correspond to useStaticFileContent=false in the live payload");
			status.StaticFileContent.Should().BeNull(
				because: "FSM on should correspond to staticFileContent=null in the live payload");
			return;
		}

		status.UseStaticFileContent.Should().BeTrue(
			because: "FSM off should correspond to useStaticFileContent=true in the live payload");
		status.StaticFileContent.Should().NotBeNull(
			because: "FSM off should correspond to populated staticFileContent in the live payload");
	}

	[AllureStep("Assert status-tool failure text mentions the invalid environment")]
	private static void AssertFailureTextMentionsEnvironment(CallToolResult callResult, string environmentName)
	{
		string combinedOutput = string.Join(
			Environment.NewLine,
			(callResult.Content ?? []).Select(content => content.ToString()));

		combinedOutput.Should().MatchRegex(
			$"(?is)({Regex.Escape(environmentName)}|environment.*not.*found|not found|error occurred invoking)",
			because: "the failure should help a human understand that the requested environment is not registered");
	}

	[AllureStep("Assert command-oriented MCP tool failed")]
	private static void AssertCommandToolFailed(CommandExecutionActResult actResult)
	{
		(actResult.CallResult.IsError == true || actResult.Execution.ExitCode != 0).Should().BeTrue(
			because: "invalid set-fsm-mode requests should fail instead of succeeding silently");
	}

	[AllureStep("Assert failure output contains Error message")]
	private static void AssertFailureIncludesErrorMessage(CommandExecutionActResult actResult, string because)
	{
		actResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "failed MCP command execution should emit human-readable diagnostics");
		actResult.Execution.Output!.Should().Contain(message => message.MessageType == Clio.Common.LogDecoratorType.Error,
			because: because);
	}

	[AllureStep("Assert failure diagnostics mention the invalid environment")]
	private static void AssertFailureMentionsEnvironment(CommandExecutionActResult actResult, string environmentName)
	{
		string combinedOutput = string.Join(
			Environment.NewLine,
			(actResult.Execution.Output ?? []).Select(message => $"{message.MessageType}: {message.Value}"));

		combinedOutput.Should().MatchRegex(
			$"(?is)({Regex.Escape(environmentName)}|environment.*not.*found|not found|error occurred invoking)",
			because: "the failure should help a human understand that the requested environment is not registered");
	}

	private sealed record FsmModeArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable
	{
		public async ValueTask DisposeAsync()
		{
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}

	private sealed record CommandExecutionActResult(
		CallToolResult CallResult,
		CommandExecutionEnvelope Execution);
}
