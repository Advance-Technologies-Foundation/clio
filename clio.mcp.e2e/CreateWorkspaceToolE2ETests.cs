using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Common;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using System.Text.RegularExpressions;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the create-workspace MCP tool.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
// [AllureNUnit] is intentionally omitted.
// NUnit runs each async test on a single thread, and [AllureNUnit] adds
// per-test bookkeeping that runs on that same thread. In tests with many
// sequential awaits, that bookkeeping appears to block async continuations
// from resuming — the test deadlocks with no timeout or error. Removing the
// attribute restores normal execution.
[AllureFeature("create-workspace")]
[NonParallelizable]
public sealed class CreateWorkspaceToolE2ETests {
	private const string ToolName = CreateWorkspaceTool.CreateWorkspaceToolName;

	[Test]
	[AllureTag(ToolName)]
	[AllureDescription("Starts the real clio MCP server, invokes create-workspace with an explicit absolute directory, and verifies that the new workspace folder is created there.")]
	[AllureName("Create Workspace Tool creates an empty workspace in the requested directory")]
	public async Task CreateWorkspace_Should_Create_Empty_Workspace_When_Directory_Is_Provided() {
		// Arrange
		await using CreateWorkspaceArrangeContext arrangeContext = await ArrangeAsync(createMissingDirectory: false);

		// Act
		CreateWorkspaceActResult actResult = await ActAsync(arrangeContext);

		// Assert
		AssertToolCallSucceeded(actResult);
		AssertCommandExitCode(actResult);
		AssertSuccessIncludesInfoMessage(actResult);
		AssertSuccessReportsCreatedWorkspacePath(actResult, arrangeContext.WorkspacePath);
		AssertWorkspaceFolderWasCreated(arrangeContext);
		AssertWorkspaceMetadataFolderWasCreated(arrangeContext);
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureDescription("Starts the real clio MCP server, configures a temporary workspaces-root value, invokes create-workspace without directory, and verifies that the new workspace folder is created under that configured root.")]
	[AllureName("Create Workspace Tool creates an empty workspace in configured workspaces-root")]
	public async Task CreateWorkspace_Should_Create_Empty_Workspace_When_Directory_Is_Omitted() {
		// Arrange
		await using CreateWorkspaceArrangeContext arrangeContext = await ArrangeAsync(
			createMissingDirectory: false,
			configureWorkspacesRoot: true);

		// Act
		CreateWorkspaceActResult actResult = await ActWithoutDirectoryAsync(arrangeContext);

		// Assert
		AssertToolCallSucceeded(actResult);
		AssertCommandExitCode(actResult);
		AssertSuccessIncludesInfoMessage(actResult);
		AssertSuccessReportsCreatedWorkspacePath(actResult, arrangeContext.WorkspacePath);
		AssertWorkspaceFolderWasCreated(arrangeContext);
		AssertWorkspaceMetadataFolderWasCreated(arrangeContext);
	}

	[Test]
	[AllureTag(ToolName)]
	// The name used to say "bootstrap repairs an invalid active environment key". It does not:
	// SettingsBootstrapService records an `invalid-active-environment` issue and leaves
	// CanExecuteEnvTools = false, and no repair rewrites the key. What this test really proves is that a
	// workspace-local command still works with that catalog, because CreateWorkspaceCommandOptions
	// declares RequiredEnvironment => false. The old wording was unfalsifiable until this fixture got a
	// private clio home — the override previously landed in the runner's own settings file and the server
	// under test never read it.
	[AllureDescription("Starts the real clio MCP server on a catalog whose ActiveEnvironmentKey does not resolve, invokes create-workspace with an explicit absolute directory, and verifies that the workspace-local flow still succeeds because the command requires no environment.")]
	[AllureName("Create Workspace Tool succeeds despite an unresolvable active environment key")]
	public async Task CreateWorkspace_Should_Create_Empty_Workspace_When_Active_Environment_Key_Is_Invalid() {
		// Arrange
		// The factory receives the fixture's OWN settings, so the deliberately broken catalog is written
		// into this fixture's private clio home and is actually read by the server started below. Building
		// a fresh environment dictionary here instead would drop CLIO_HOME, and the override would land in
		// the runner's real per-user settings file while the server read a different one entirely.
		await using CreateWorkspaceArrangeContext arrangeContext = await ArrangeAsync(
			createMissingDirectory: false,
			settingsOverrideFactory: fixtureSettings => TemporaryClioSettingsOverride.SetWrongActiveEnvironmentKey(
				fixtureSettings.ClioProcessPath,
				fixtureSettings.ProcessEnvironmentVariables));

		// Act
		CreateWorkspaceActResult actResult = await ActAsync(arrangeContext);

		// Assert
		AssertToolCallSucceeded(actResult);
		AssertCommandExitCode(actResult);
		AssertSuccessIncludesInfoMessage(actResult);
		AssertSuccessReportsCreatedWorkspacePath(actResult, arrangeContext.WorkspacePath);
		AssertWorkspaceFolderWasCreated(arrangeContext);
		AssertWorkspaceMetadataFolderWasCreated(arrangeContext);
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureDescription("Starts the real clio MCP server, invokes create-workspace with a non-existent absolute directory, and verifies that the MCP result reports a failure without creating local files.")]
	[AllureName("Create Workspace Tool reports invalid directory failures")]
	public async Task CreateWorkspace_Should_Report_Failure_When_Directory_Does_Not_Exist() {
		// Arrange
		await using CreateWorkspaceArrangeContext arrangeContext = await ArrangeAsync(createMissingDirectory: true);

		// Act
		CreateWorkspaceActResult actResult = await ActAsync(arrangeContext);

		// Assert
		AssertToolCallFailed(actResult);
		AssertFailureIncludesErrorMessage(actResult);
		AssertFailureMentionsMissingDirectory(actResult, arrangeContext.RootDirectory);
		AssertWorkspaceFolderWasNotCreated(arrangeContext);
	}

	[AllureStep("Arrange create-workspace MCP session")]
	[AllureDescription("Arrange by creating an isolated temporary directory, choosing the requested workspace path, and starting a real clio MCP server session")]
	private static async Task<CreateWorkspaceArrangeContext> ArrangeAsync(
		bool createMissingDirectory,
		bool configureWorkspacesRoot = false,
		Func<McpE2ESettings, TemporaryClioSettingsOverride>? settingsOverrideFactory = null) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		// A PRIVATE clio home, never the suite-shared one. This fixture rewrites appsettings.json in two of
		// its four tests (a workspaces-root value, and a deliberately unresolvable ActiveEnvironmentKey)
		// through TemporaryClioSettingsOverride's plain File.WriteAllText, which takes none of the
		// cross-process locks a real clio writer takes. Pointed at the shared catalog that write races every
		// other clio process in the run and loses on Windows with "the process cannot access the file …
		// because it is being used by another process" — the observed failure of the workspaces-root test.
		// The previous `ProcessEnvironmentVariables["HOME"] = <user profile>` line bought no isolation from
		// that: CLIO_HOME outranks HOME/LOCALAPPDATA outright and TestConfiguration.Load puts the
		// suite-owned CLIO_HOME into every spawned process, so HOME was inert. IsolatedClioHome sets the
		// variable that actually decides, plus HOME/USERPROFILE, so the whole fixture is self-contained.
		string clioHome = IsolatedClioHome.CreateAndRedirect(settings, "clio-create-workspace-home");
		string rootDirectory = Path.Combine(Path.GetTempPath(), $"clio-create-workspace-e2e-{Guid.NewGuid():N}");
		if (!createMissingDirectory) {
			Directory.CreateDirectory(rootDirectory);
		}

		string workspaceName = $"workspace-{Guid.NewGuid():N}";
		string workspacePath = Path.Combine(rootDirectory, workspaceName);
		TemporaryClioSettingsOverride? settingsOverride = configureWorkspacesRoot
			? SetWorkspacesRootInPrivateHome(settings, rootDirectory)
			: settingsOverrideFactory?.Invoke(settings);
		settingsOverride?.AppSettingsPath.Should().StartWith(clioHome,
			because: "the settings file this fixture rewrites must be its own, so the write cannot collide with another clio process on the suite-shared catalog and cannot leak a broken catalog into the rest of the run");
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new CreateWorkspaceArrangeContext(rootDirectory, workspaceName, workspacePath, session, cancellationTokenSource, settingsOverride, clioHome);
	}

	// SetWorkspacesRoot reads the existing settings file to preserve the rest of the catalog, so it needs
	// one to exist. A freshly created private home has none until some clio process writes it, and relying
	// on the path probe's side effect would make this arrangement depend on a detail of another command.
	private static TemporaryClioSettingsOverride SetWorkspacesRootInPrivateHome(
		McpE2ESettings settings,
		string workspacesRoot) {
		string appSettingsPath = TemporaryClioSettingsOverride.GetClioAppSettingsPath(
			settings.ClioProcessPath,
			settings.ProcessEnvironmentVariables);
		if (!File.Exists(appSettingsPath)) {
			Directory.CreateDirectory(Path.GetDirectoryName(appSettingsPath)!);
			File.WriteAllText(appSettingsPath, "{}");
		}
		return TemporaryClioSettingsOverride.SetWorkspacesRoot(
			workspacesRoot,
			settings.ClioProcessPath,
			settings.ProcessEnvironmentVariables);
	}

	[AllureStep("Act by invoking create-workspace through MCP")]
	[AllureDescription("Act by discovering the create-workspace MCP tool and invoking it with the arranged workspace name and directory")]
	private static async Task<CreateWorkspaceActResult> ActAsync(CreateWorkspaceArrangeContext arrangeContext) {
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);
		toolNames.Should().Contain(ToolName,
			because: "the create-workspace MCP tool must be discoverable via the get-tool-contract compact index before the end-to-end call can be executed");

		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["workspaceName"] = arrangeContext.WorkspaceName,
					["directory"] = arrangeContext.RootDirectory
				}
			},
			arrangeContext.CancellationTokenSource.Token);

		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
		return new CreateWorkspaceActResult(callResult, execution);
	}

	[AllureStep("Act by invoking create-workspace without directory through MCP")]
	[AllureDescription("Act by discovering the create-workspace MCP tool and invoking it with only the arranged workspace name so clio uses the configured workspaces-root setting")]
	private static async Task<CreateWorkspaceActResult> ActWithoutDirectoryAsync(CreateWorkspaceArrangeContext arrangeContext) {
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);
		toolNames.Should().Contain(ToolName,
			because: "the create-workspace MCP tool must be discoverable via the get-tool-contract compact index before the end-to-end call can be executed");

		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["workspaceName"] = arrangeContext.WorkspaceName
				}
			},
			arrangeContext.CancellationTokenSource.Token);

		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
		return new CreateWorkspaceActResult(callResult, execution);
	}

	[AllureStep("Assert MCP tool result is successful")]
	[AllureDescription("Assert that the create-workspace MCP tool completed without an MCP error result")]
	private static void AssertToolCallSucceeded(CreateWorkspaceActResult actResult) {
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: $"a successful create-workspace invocation should return a normal MCP tool result. Actual MCP content: {DescribeCallResult(actResult.CallResult)}");
	}

	[AllureStep("Assert create-workspace command exit code")]
	[AllureDescription("Assert that the underlying create-workspace command completed with exit code 0")]
	private static void AssertCommandExitCode(CreateWorkspaceActResult actResult) {
		actResult.Execution.ExitCode.Should().Be(0,
			because: "the underlying create-workspace command should complete successfully for an existing target directory");
	}

	[AllureStep("Assert success output contains info message")]
	[AllureDescription("Assert that successful create-workspace execution includes at least one Info log message in the MCP command output")]
	private static void AssertSuccessIncludesInfoMessage(CreateWorkspaceActResult actResult) {
		actResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "successful MCP command execution should emit human-readable log messages");
		actResult.Execution.Output!.Should().Contain(
			message => message.MessageType == LogDecoratorType.Info,
			because: "successful create-workspace execution should report progress or completion using info-level log output");
	}

	[AllureStep("Assert success output reports created workspace path")]
	[AllureDescription("Assert that successful create-workspace execution includes the full path where the workspace was created")]
	private static void AssertSuccessReportsCreatedWorkspacePath(
		CreateWorkspaceActResult actResult,
		string expectedWorkspacePath) {
		actResult.Execution.Output.Should().Contain(
			message => message.MessageType == LogDecoratorType.Info
				&& string.Equals(message.Value, $"Workspace created at: {expectedWorkspacePath}", StringComparison.Ordinal),
			because: "successful create-workspace execution should tell the user where the workspace was created");
	}

	[AllureStep("Assert workspace folder was created")]
	[AllureDescription("Assert that the requested workspace folder now exists under the target directory")]
	private static void AssertWorkspaceFolderWasCreated(CreateWorkspaceArrangeContext arrangeContext) {
		Directory.Exists(arrangeContext.WorkspacePath).Should().BeTrue(
			because: "create-workspace should create the requested workspace folder");
	}

	[AllureStep("Assert workspace metadata folder was created")]
	[AllureDescription("Assert that the generated workspace contains the .clio metadata folder")]
	private static void AssertWorkspaceMetadataFolderWasCreated(CreateWorkspaceArrangeContext arrangeContext) {
		Directory.Exists(Path.Combine(arrangeContext.WorkspacePath, ".clio")).Should().BeTrue(
			because: "a created clio workspace should include the .clio metadata folder");
	}

	[AllureStep("Assert failed create-workspace request reported failure")]
	[AllureDescription("Assert that create-workspace reports failure instead of succeeding silently when the target directory is invalid")]
	private static void AssertToolCallFailed(CreateWorkspaceActResult actResult) {
		bool failed = actResult.CallResult.IsError == true || actResult.Execution.ExitCode != 0;
		failed.Should().BeTrue(
			because: "create-workspace should fail when the requested directory does not exist");
	}

	[AllureStep("Assert failure output contains error message type")]
	[AllureDescription("Assert that failed create-workspace execution emits at least one Error log message when execution output is available")]
	private static void AssertFailureIncludesErrorMessage(CreateWorkspaceActResult actResult) {
		actResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "failed MCP command execution should emit human-readable diagnostics");
		actResult.Execution.Output!.Should().Contain(
			message => message.MessageType == LogDecoratorType.Error,
			because: "failed create-workspace execution should report its diagnostics as error-level log output");
	}

	[AllureStep("Assert failure diagnostics mention missing directory")]
	[AllureDescription("Assert that the failure output identifies the missing target directory or at minimum states that the create-workspace MCP invocation failed because the directory could not be used")]
	private static void AssertFailureMentionsMissingDirectory(CreateWorkspaceActResult actResult, string rootDirectory) {
		string combinedOutput = string.Join(
			Environment.NewLine,
			(actResult.Execution.Output ?? [])
			.Select(message => $"{message.MessageType}: {message.Value}"));

		combinedOutput.Should().NotBeNullOrWhiteSpace(
			because: "failed create-workspace execution should explain why the directory could not be used");
		combinedOutput.Should().MatchRegex(
			$"(?is)({Regex.Escape(rootDirectory)}.*does not exist|workspace root directory does not exist|an error occurred invoking 'create-workspace')",
			because: "the failure log should either identify the missing directory directly or at minimum show the MCP invocation failure wrapper for the create-workspace tool");
	}

	[AllureStep("Assert workspace folder was not created")]
	[AllureDescription("Assert that no workspace folder is created when the requested target directory is invalid")]
	private static void AssertWorkspaceFolderWasNotCreated(CreateWorkspaceArrangeContext arrangeContext) {
		Directory.Exists(arrangeContext.WorkspacePath).Should().BeFalse(
			because: "create-workspace should not create local files when the requested base directory does not exist");
	}

	private sealed record CreateWorkspaceArrangeContext(
		string RootDirectory,
		string WorkspaceName,
		string WorkspacePath,
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		TemporaryClioSettingsOverride? SettingsOverride,
		string ClioHome) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
			SettingsOverride?.Dispose();

			if (Directory.Exists(RootDirectory)) {
				Directory.Delete(RootDirectory, recursive: true);
			}

			// The private home is this fixture's own scratch, so it goes with the test. A leftover home is
			// not worth failing a passing assertion over — on Windows the server child can still be
			// releasing handles when the directory delete runs.
			if (Directory.Exists(ClioHome)) {
				try {
					Directory.Delete(ClioHome, recursive: true);
				} catch (IOException) {
				} catch (UnauthorizedAccessException) {
				}
			}
		}
	}

	private sealed record CreateWorkspaceActResult(
		CallToolResult CallResult,
		CommandExecutionEnvelope Execution);

	private static string DescribeCallResult(CallToolResult callResult) {
		if (callResult.Content is null || callResult.Content.Count == 0) {
			return "<no content>";
		}

		return string.Join(
			" | ",
			callResult.Content.Select(content => content?.ToString() ?? "<null>"));
	}
}
