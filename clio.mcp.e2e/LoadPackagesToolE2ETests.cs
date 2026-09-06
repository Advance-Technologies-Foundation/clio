using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the <c>pkg-to-db</c> MCP tool on an environment that has file system
/// development mode (FSM) disabled.
/// </summary>
/// <remarks>
/// GitHub issue #952: the loader used to report every refusal through a plain log line and return
/// <c>void</c>, so the tool answered <c>exit-code: 0</c> with <c>message-type: "None"</c> while nothing
/// had been loaded — both published failure signals of the <c>command-execution-result</c> contract were
/// negative. This fixture pins the two signals on the only refusal reachable without changing the
/// environment: FSM disabled. Nothing is mutated, because the command stops at the FSM check before it
/// posts the import request; the test skips itself when the sandbox has FSM enabled, where the same call
/// WOULD import packages.
/// No AllowDestructiveMcpTests gate and no DestructiveStandAuthorization check: the arrange step only
/// reads the FSM state, and the refused call changes nothing on the stand.
/// </remarks>
[TestFixture]
[AllureNUnit]
[AllureFeature("pkg-to-db")]
[NonParallelizable]
public sealed class LoadPackagesToolE2ETests : McpContractFixtureBase {

	private const string LoadPackagesToDbToolName = "pkg-to-db";
	private const string GetFsmModeToolName = FsmModeTool.GetFsmModeToolName;

	[Test]
	[Category("McpE2E.Sandbox")]
	[AllureTag(LoadPackagesToDbToolName)]
	[AllureDescription("Invokes pkg-to-db against a sandbox environment with file system development mode disabled and verifies that the refusal reaches the caller as a non-zero exit code and an Error log message.")]
	[AllureName("Load packages to database reports failure when file system development mode is disabled")]
	[Description("pkg-to-db reports a non-zero exit code and an Error message when the environment has file system development mode disabled, instead of an exit code 0 with no error.")]
	public async Task LoadPackagesToDb_Should_Report_Failure_When_FileDesignMode_Is_Disabled() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		await using ArrangeContext arrangeContext = Arrange();
		string environmentName = settings.Sandbox.EnvironmentName!;
		await AssertFileDesignModeIsDisabledAsync(arrangeContext, environmentName);

		// Act
		CommandExecutionEnvelope execution = await ActLoadPackagesToDbAsync(arrangeContext, environmentName);

		// Assert
		AssertExitCodeReportsFailure(execution);
		AssertErrorMessageIsReported(execution);
	}

	// The FSM-enabled sandbox would really import the packages, which is a mutation this read-only
	// fixture must not perform, so the state is a precondition rather than an assertion.
	private static async Task AssertFileDesignModeIsDisabledAsync(ArrangeContext arrangeContext, string environmentName) {
		await AllureApi.Step("Arrange by confirming the sandbox has file system development mode disabled", async () => {
			CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
				GetFsmModeToolName,
				new Dictionary<string, object?> { ["environmentName"] = environmentName },
				arrangeContext.CancellationTokenSource.Token);
			FsmModeStatusEnvelope status = FsmModeStatusResultParser.Extract(callResult);
			if (!string.Equals(status.Mode, "off", StringComparison.OrdinalIgnoreCase)) {
				Assert.Ignore(
					"The sandbox environment has file system development mode enabled, where pkg-to-db would " +
					"import packages; the disabled-mode refusal cannot be observed without mutating it.");
			}
		});
	}

	private static async Task<CommandExecutionEnvelope> ActLoadPackagesToDbAsync(
		ArrangeContext arrangeContext,
		string environmentName) {
		return await AllureApi.Step("Act by dispatching pkg-to-db through clio-run", async () => {
			IReadOnlyCollection<string> toolNames =
				await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);
			toolNames.Should().Contain(LoadPackagesToDbToolName,
				because: "pkg-to-db must be discoverable through the get-tool-contract compact index before it can be dispatched");

			// pkg-to-db is a destructive long-tail tool: called by name it answers confirmation-required,
			// so the reachable path is the advertised clio-run executor.
			CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
				ClioRunTool.ToolName,
				new Dictionary<string, object?> {
					["command"] = LoadPackagesToDbToolName,
					["args"] = new Dictionary<string, object?> {
						["environmentName"] = environmentName
					}
				},
				arrangeContext.CancellationTokenSource.Token);
			return McpCommandExecutionParser.Extract(callResult);
		});
	}

	[AllureStep("Assert the exit code reports the refused load")]
	private static void AssertExitCodeReportsFailure(CommandExecutionEnvelope execution) {
		execution.ExitCode.Should().NotBe(0,
			because: "a load that never happened must not be reported to the caller as exit code 0 (GitHub issue #952)");
	}

	[AllureStep("Assert the refusal is reported as an error log message")]
	private static void AssertErrorMessageIsReported(CommandExecutionEnvelope execution) {
		execution.Output.Should().NotBeNullOrEmpty(
			because: "a refused load must explain itself in the execution log");
		execution.Output!.Should().Contain(
			message => message.MessageType == LogDecoratorType.Error,
			because: "the second published failure signal of the command-execution-result contract is an Error " +
			"message type, and the refusal used to be reported as message-type None");
		string combinedOutput = string.Join(
			Environment.NewLine,
			execution.Output!.Select(message => $"{message.MessageType}: {message.Value}"));
		combinedOutput.Should().Contain("file design mode",
			because: "the diagnostics must name the disabled file system development mode as the reason");
	}
}
