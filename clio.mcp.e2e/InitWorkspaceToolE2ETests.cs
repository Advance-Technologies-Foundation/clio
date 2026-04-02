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

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the init-workspace MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("init-workspace")]
[NonParallelizable]
public sealed class InitWorkspaceToolE2ETests {
	private const string ToolName = InitWorkspaceTool.InitWorkspaceToolName;

	[Test]
	[AllureTag(ToolName)]
	[AllureDescription("Starts the real clio MCP server inside a directory that already contains a user file, invokes init-workspace, and verifies that the workspace is initialized without overwriting that file.")]
	[AllureName("Init Workspace Tool initializes existing directory without overwriting files")]
	public async Task InitWorkspace_Should_Initialize_Current_Directory_Without_Overwriting_Existing_Files() {
		// Arrange
		await using InitWorkspaceArrangeContext arrangeContext = await ArrangeAsync();

		// Act
		InitWorkspaceActResult actResult = await ActAsync(arrangeContext);

		// Assert
		AssertToolCallSucceeded(actResult);
		AssertCommandExitCode(actResult);
		AssertSuccessIncludesInfoMessage(actResult);
		AssertSuccessReportsInitializedWorkspacePath(actResult, arrangeContext.WorkspacePath);
		AssertWorkspaceMetadataFolderWasCreated(arrangeContext);
		AssertExistingFileWasPreserved(arrangeContext);
	}

	[AllureStep("Arrange init-workspace MCP session")]
	private static async Task<InitWorkspaceArrangeContext> ArrangeAsync() {
		string workspacePath = Path.Combine(Path.GetTempPath(), $"clio-init-workspace-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(workspacePath);
		string preservedFilePath = Path.Combine(workspacePath, "README.md");
		File.WriteAllText(preservedFilePath, "existing-content");

		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		McpServerSession session = await McpServerSession.StartAsync(
			settings,
			cancellationTokenSource.Token,
			workingDirectory: workspacePath);
		return new InitWorkspaceArrangeContext(workspacePath, preservedFilePath, session, cancellationTokenSource);
	}

	[AllureStep("Act by invoking init-workspace through MCP")]
	private static async Task<InitWorkspaceActResult> ActAsync(InitWorkspaceArrangeContext arrangeContext) {
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the init-workspace MCP tool must be advertised by the server before the end-to-end call can be executed");

		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?>(),
			arrangeContext.CancellationTokenSource.Token);

		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
		return new InitWorkspaceActResult(callResult, execution);
	}

	[AllureStep("Assert MCP tool result is successful")]
	private static void AssertToolCallSucceeded(InitWorkspaceActResult actResult) {
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: "a successful init-workspace invocation should return a normal MCP tool result");
	}

	[AllureStep("Assert init-workspace command exit code")]
	private static void AssertCommandExitCode(InitWorkspaceActResult actResult) {
		actResult.Execution.ExitCode.Should().Be(0,
			because: "the underlying init-workspace command should complete successfully for an existing directory");
	}

	[AllureStep("Assert success output contains info message")]
	private static void AssertSuccessIncludesInfoMessage(InitWorkspaceActResult actResult) {
		actResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "successful MCP command execution should emit human-readable log messages");
		actResult.Execution.Output!.Should().Contain(
			message => message.MessageType == LogDecoratorType.Info,
			because: "successful init-workspace execution should report progress or completion using info-level log output");
	}

	[AllureStep("Assert success output reports initialized workspace path")]
	private static void AssertSuccessReportsInitializedWorkspacePath(
		InitWorkspaceActResult actResult,
		string expectedWorkspacePath) {
		string expectedWorkspaceName = Path.GetFileName(expectedWorkspacePath);
		actResult.Execution.Output.Should().Contain(
			message => message.MessageType == LogDecoratorType.Info
				&& message.Value.StartsWith("Workspace initialized at: ", StringComparison.Ordinal)
				&& message.Value.EndsWith(expectedWorkspaceName, StringComparison.Ordinal),
			because: "successful init-workspace execution should tell the user where the workspace was initialized");
	}

	[AllureStep("Assert workspace metadata folder was created")]
	private static void AssertWorkspaceMetadataFolderWasCreated(InitWorkspaceArrangeContext arrangeContext) {
		Directory.Exists(Path.Combine(arrangeContext.WorkspacePath, ".clio")).Should().BeTrue(
			because: "an initialized clio workspace should include the .clio metadata folder");
	}

	[AllureStep("Assert existing file was preserved")]
	private static void AssertExistingFileWasPreserved(InitWorkspaceArrangeContext arrangeContext) {
		File.ReadAllText(arrangeContext.PreservedFilePath).Should().Be("existing-content",
			because: "init-workspace should not overwrite files that already exist in the target directory");
	}

	private sealed record InitWorkspaceArrangeContext(
		string WorkspacePath,
		string PreservedFilePath,
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
			if (Directory.Exists(WorkspacePath)) {
				Directory.Delete(WorkspacePath, recursive: true);
			}
		}
	}

	private sealed record InitWorkspaceActResult(
		CallToolResult CallResult,
		CommandExecutionEnvelope Execution);
}
