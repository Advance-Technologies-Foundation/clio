using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Common;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.RegularExpressions;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the create-workspace MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
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
	[AllureDescription("Starts the real clio MCP server with a stale ActiveEnvironmentKey, invokes create-workspace with an explicit absolute directory, and verifies that the bootstrap repair still allows the local workspace flow to succeed.")]
	[AllureName("Create Workspace Tool succeeds after bootstrap repairs an invalid active environment key")]
	public async Task CreateWorkspace_Should_Create_Empty_Workspace_When_Active_Environment_Key_Is_Invalid() {
		// Arrange
		await using CreateWorkspaceArrangeContext arrangeContext = await ArrangeAsync(
			createMissingDirectory: false,
			settingsOverrideFactory: () => TemporaryClioSettingsOverride.SetWrongActiveEnvironmentKey(
				TestConfiguration.ResolveFreshClioProcessPath(),
				new Dictionary<string, string?> {
					["HOME"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
				}));

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
		Func<TemporaryClioSettingsOverride>? settingsOverrideFactory = null) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		settings.ProcessEnvironmentVariables["HOME"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		string rootDirectory = Path.Combine(Path.GetTempPath(), $"clio-create-workspace-e2e-{Guid.NewGuid():N}");
		if (!createMissingDirectory) {
			Directory.CreateDirectory(rootDirectory);
		}

		string workspaceName = $"workspace-{Guid.NewGuid():N}";
		string workspacePath = Path.Combine(rootDirectory, workspaceName);
		TemporaryClioSettingsOverride? settingsOverride = configureWorkspacesRoot
			? TemporaryClioSettingsOverride.SetWorkspacesRoot(
				rootDirectory,
				settings.ClioProcessPath,
				settings.ProcessEnvironmentVariables)
			: settingsOverrideFactory?.Invoke();
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new CreateWorkspaceArrangeContext(rootDirectory, workspaceName, workspacePath, session, cancellationTokenSource, settingsOverride);
	}

	[AllureStep("Act by invoking create-workspace through MCP")]
	[AllureDescription("Act by discovering the create-workspace MCP tool and invoking it with the arranged workspace name and directory")]
	private static async Task<CreateWorkspaceActResult> ActAsync(CreateWorkspaceArrangeContext arrangeContext) {
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the create-workspace MCP tool must be advertised by the server before the end-to-end call can be executed");

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
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the create-workspace MCP tool must be advertised by the server before the end-to-end call can be executed");

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
		TemporaryClioSettingsOverride? SettingsOverride) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
			SettingsOverride?.Dispose();

			if (Directory.Exists(RootDirectory)) {
				Directory.Delete(RootDirectory, recursive: true);
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
