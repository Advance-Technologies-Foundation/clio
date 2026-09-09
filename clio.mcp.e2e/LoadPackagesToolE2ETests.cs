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
/// negative. This fixture pins the agreement between those two signals.
/// <para>
/// The test asserts BOTH file design mode states instead of skipping on one of them. The MCP end-to-end
/// stand is deployed fresh for every build, so a fixture that self-ignored whenever the stand came up
/// with FSM enabled could disappear from the run statistics without anyone noticing. On an FSM-disabled
/// stand nothing is mutated (the command stops at the FSM check before the import request); on an
/// FSM-enabled stand the import really runs, which is why the standard
/// <c>AllowDestructiveMcpTests</c> opt-in guards the fixture — CI sets it to true.
/// </para>
/// <para>
/// The arrange step reads <c>get-fsm-mode</c>, which derives the mode from GetApplicationInfo, while the
/// command itself gates on WorkspaceExplorerService.svc/GetIsFileDesignMode. Should those two signals
/// ever disagree on an environment, this test fails and names the inconsistency instead of silently
/// skipping — which is the outcome we want from an end-to-end fixture.
/// </para>
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
	[AllureDescription("Invokes pkg-to-db against the sandbox environment and verifies that the reported exit code and Error log message agree with what actually happened: a refusal on a file-system-mode-disabled environment reaches the caller as a non-zero exit code with an Error message, and a load that really ran still reports exit code 0 with no Error message.")]
	[AllureName("Load packages to database reports the load outcome honestly in both file system development mode states")]
	[Description("pkg-to-db reports a non-zero exit code and an Error message when the environment has file system development mode disabled, instead of an exit code 0 with no error, and still reports exit code 0 with no Error message when the load really runs.")]
	public async Task LoadPackagesToDb_Should_Report_The_Load_Outcome_Honestly() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run the pkg-to-db end-to-end test: on an " +
				"environment with file system development mode enabled it really imports the packages.");
		}
		await using ArrangeContext arrangeContext = Arrange();
		string environmentName = settings.Sandbox.EnvironmentName!;
		bool isFileDesignModeEnabled = await ArrangeFileDesignModeStateAsync(arrangeContext, environmentName);

		// Act
		CommandExecutionEnvelope execution = await ActLoadPackagesToDbAsync(arrangeContext, environmentName);

		// Assert
		if (isFileDesignModeEnabled) {
			AssertLoadIsReportedAsCompleted(execution);
		} else {
			AssertExitCodeReportsFailure(execution);
			AssertErrorMessageIsReported(execution);
		}
	}

	// Read rather than assert: the stand is deployed fresh per build and either file design mode state is
	// legitimate, so the observed state selects which half of the contract is pinned.
	private static async Task<bool> ArrangeFileDesignModeStateAsync(ArrangeContext arrangeContext, string environmentName) {
		return await AllureApi.Step("Arrange by reading the sandbox file system development mode state", async () => {
			CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
				GetFsmModeToolName,
				new Dictionary<string, object?> { ["environmentName"] = environmentName },
				arrangeContext.CancellationTokenSource.Token);
			FsmModeStatusEnvelope status = FsmModeStatusResultParser.Extract(callResult);
			status.Mode.Should().BeOneOf(["on", "off"],
				because: "the sandbox must report a known file system development mode before pkg-to-db is dispatched");
			return string.Equals(status.Mode, "on", StringComparison.OrdinalIgnoreCase);
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

	[AllureStep("Assert a load that really ran is reported as a success")]
	private static void AssertLoadIsReportedAsCompleted(CommandExecutionEnvelope execution) {
		execution.ExitCode.Should().Be(0,
			because: "on a file-system-mode environment the import really runs, and the honest exit code of a " +
			"completed load must stay 0 - the fix for GitHub issue #952 must not invert the success case");
		execution.Output.Should().NotContain(
			message => message.MessageType == LogDecoratorType.Error,
			because: "a completed load must not publish the failure signal of the command-execution-result contract");
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
