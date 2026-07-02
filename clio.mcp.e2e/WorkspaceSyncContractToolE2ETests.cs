using System.Text.Json;
using System.Text.RegularExpressions;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Stand-free end-to-end contract tests for the workspace sync MCP tools.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("workspace-sync")]
[Category("McpE2E.NoEnvironment")]
[Parallelizable(ParallelScope.Self)]
public sealed class WorkspaceSyncContractToolE2ETests : McpContractFixtureBase {
	private const string PushToolName = PushWorkspaceTool.PushWorkspaceToolName;
	private const string RestoreToolName = RestoreWorkspaceTool.RestoreWorkspaceToolName;

	[Test]
	[Description("Starts the real clio MCP server, invokes push-workspace with an unknown environment name, and verifies that the tool reports a readable failure without mutating the workspace directory.")]
	[AllureTag(PushToolName)]
	[AllureName("Push workspace reports invalid environment failures")]
	[AllureDescription("Uses the real clio MCP server to call push-workspace with a guaranteed-missing environment name and verifies the error diagnostics plus the absence of workspace mutations.")]
	public async Task PushWorkspace_Should_Report_Invalid_Environment() {
		// Arrange
		await using WorkspaceSyncArrangeContext arrangeContext = ArrangeInvalidEnvironment("pushw");

		// Act
		WorkspaceCommandActResult actResult = await ActWorkspaceCommandAsync(arrangeContext, PushToolName, arrangeContext.WorkspacePath);

		// Assert
		AssertToolCallFailed(actResult);
		AssertCommandExitCode(actResult, 1, "unknown environment names should fail before push-workspace starts operating on the workspace");
		AssertIncludesErrorMessage(actResult, "failed push-workspace execution should emit error diagnostics");
		AssertFailureMentionsMissingEnvironment(actResult, arrangeContext.EnvironmentName, PushToolName);
		AssertWorkspaceWasNotMutated(arrangeContext.WorkspacePath);
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes restore-workspace with an unknown environment name, and verifies that the tool reports a readable failure without mutating the workspace directory.")]
	[AllureTag(RestoreToolName)]
	[AllureName("Restore workspace reports invalid environment failures")]
	[AllureDescription("Uses the real clio MCP server to call restore-workspace with a guaranteed-missing environment name and verifies the error diagnostics plus the absence of workspace mutations.")]
	public async Task RestoreWorkspace_Should_Report_Invalid_Environment() {
		// Arrange
		await using WorkspaceSyncArrangeContext arrangeContext = ArrangeInvalidEnvironment("restorew");

		// Act
		WorkspaceCommandActResult actResult = await ActWorkspaceCommandAsync(arrangeContext, RestoreToolName, arrangeContext.WorkspacePath);

		// Assert
		AssertToolCallFailed(actResult);
		AssertCommandExitCode(actResult, 1, "unknown environment names should fail before restore-workspace starts downloading or creating workspace content");
		AssertIncludesErrorMessage(actResult, "failed restore-workspace execution should emit error diagnostics");
		AssertFailureMentionsMissingEnvironment(actResult, arrangeContext.EnvironmentName, RestoreToolName);
		AssertWorkspaceWasNotMutated(arrangeContext.WorkspacePath);
	}

	[Test]
	[Description("Starts the real clio MCP server, lists tools, and verifies that both workspace-sync MCP endpoints are advertised as destructive.")]
	[AllureTag(PushToolName)]
	[AllureTag(RestoreToolName)]
	[AllureName("Workspace sync tools advertise destructive metadata")]
	[AllureDescription("Uses the real clio MCP server tool discovery response to verify that push-workspace and restore-workspace expose the destructive hint required for client-side safety policies.")]
	public async Task WorkspaceSync_Tools_Should_Be_Advertised_As_Destructive() {
		// Arrange
		await using ArrangeContext arrangeContext = Arrange();

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		AssertToolIsAdvertisedAsDestructive(tools, PushToolName);
		AssertToolIsAdvertisedAsDestructive(tools, RestoreToolName);
	}

	[Test]
	[Description("Starts the real clio MCP server, inspects push-workspace tool discovery metadata, and verifies that skip-backup is exposed as an optional input argument.")]
	[AllureTag(PushToolName)]
	[AllureName("Push workspace advertises optional skip-backup argument")]
	[AllureDescription("Uses the real clio MCP server tool discovery response to verify that push-workspace exposes the optional skip-backup argument for callers that explicitly want to skip the pre-install backup.")]
	public async Task PushWorkspace_Tool_Should_Advertise_Optional_SkipBackup_Argument() {
		// Arrange
		await using ArrangeContext arrangeContext = Arrange();

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		AssertPushWorkspaceToolAdvertisesSkipBackup(tools);
	}

	private WorkspaceSyncArrangeContext ArrangeInvalidEnvironment(string toolPrefix) {
		string rootDirectory = CreateFixtureDirectory($"workspace-sync-{toolPrefix}");
		string workspacePath = Path.Combine(rootDirectory, "workspace");
		string environmentName = $"missing-{toolPrefix}-env-{Guid.NewGuid():N}";
		Directory.CreateDirectory(workspacePath);
		ArrangeContext arrangeContext = Arrange();
		return new WorkspaceSyncArrangeContext(
			rootDirectory,
			workspacePath,
			environmentName,
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource);
	}

	private static async Task<WorkspaceCommandActResult> ActWorkspaceCommandAsync(
		WorkspaceSyncArrangeContext arrangeContext,
		string toolName,
		string workspacePath) {
		return await AllureApi.Step("Act by invoking workspace-sync tool through MCP", async () => {
			IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
			tools.Select(tool => tool.Name).Should().Contain(toolName,
				because: "the requested workspace-sync MCP tool must be advertised before the end-to-end call can be executed");

			CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
				toolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = arrangeContext.EnvironmentName,
						["workspace-path"] = workspacePath
					}
				},
				arrangeContext.CancellationTokenSource.Token);

			CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
			return new WorkspaceCommandActResult(callResult, execution);
		});
	}

	[AllureStep("Assert MCP tool call failed")]
	private static void AssertToolCallFailed(WorkspaceCommandActResult actResult) {
		(actResult.CallResult.IsError == true || actResult.Execution.ExitCode != 0).Should().BeTrue(
			because: "invalid environment requests should fail instead of succeeding silently");
	}

	[AllureStep("Assert command exit code")]
	private static void AssertCommandExitCode(WorkspaceCommandActResult actResult, int expectedExitCode, string because) {
		actResult.Execution.ExitCode.Should().Be(expectedExitCode, because: because);
	}

	[AllureStep("Assert execution includes Error message")]
	private static void AssertIncludesErrorMessage(WorkspaceCommandActResult actResult, string because) {
		actResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "failed command execution should emit human-readable diagnostics");
		actResult.Execution.Output!.Should().Contain(message => message.MessageType == LogDecoratorType.Error,
			because: because);
	}

	[AllureStep("Assert invalid environment diagnostics mention the missing environment name")]
	private static void AssertFailureMentionsMissingEnvironment(
		WorkspaceCommandActResult actResult,
		string environmentName,
		string toolName) {
		string combinedOutput = string.Join(
			Environment.NewLine,
			(actResult.Execution.Output ?? []).Select(message => $"{message.MessageType}: {message.Value}"));

		combinedOutput.Should().NotBeNullOrWhiteSpace(
			because: "failed workspace-sync execution should explain why the call was rejected");
		combinedOutput.Should().MatchRegex(
			$"(?is)({Regex.Escape(environmentName)}.*not found|environment.*not.*found|{Regex.Escape(toolName)}|error occurred invoking)",
			because: "the failure should either identify the missing environment directly or include the MCP invocation wrapper");
	}

	[AllureStep("Assert workspace was not mutated")]
	private static void AssertWorkspaceWasNotMutated(string workspacePath) {
		Directory.EnumerateFileSystemEntries(workspacePath).Should().BeEmpty(
			because: "invalid environment requests must not create or modify files in the target workspace directory");
	}

	[AllureStep("Assert discovered MCP tool is marked as destructive")]
	private static void AssertToolIsAdvertisedAsDestructive(IList<McpClientTool> tools, string toolName) {
		McpClientTool tool = tools.Single(tool => tool.Name == toolName);

		tool.ProtocolTool.Annotations.Should().NotBeNull(
			because: "the MCP server should expose tool annotations for clients that apply safety policies");
		tool.ProtocolTool.Annotations!.DestructiveHint.Should().BeTrue(
			because: "workspace-sync tools mutate local workspace state and/or the target environment");
	}

	[AllureStep("Assert push-workspace MCP schema advertises optional skip-backup argument")]
	private static void AssertPushWorkspaceToolAdvertisesSkipBackup(IList<McpClientTool> tools) {
		McpClientTool tool = tools.Single(tool => tool.Name == PushToolName);
		JsonElement inputSchema = JsonSerializer.SerializeToElement(tool.ProtocolTool.InputSchema);
		JsonElement argsSchema = inputSchema.GetProperty("properties").GetProperty("args");
		JsonElement argsProperties = argsSchema.GetProperty("properties");
		string[] requiredArgs = argsSchema.GetProperty("required").EnumerateArray()
			.Select(item => item.GetString()!)
			.ToArray();

		argsProperties.TryGetProperty("skip-backup", out JsonElement skipBackupProperty).Should().BeTrue(
			because: "push-workspace callers need an explicit way to disable backup without changing the default behavior");
		JsonElement skipBackupType = skipBackupProperty.GetProperty("type");
		IEnumerable<string> declaredTypes = skipBackupType.ValueKind == JsonValueKind.Array
			? skipBackupType.EnumerateArray().Select(item => item.GetString()!)
			: [skipBackupType.GetString()!];
		declaredTypes.Should().Contain("boolean",
			because: "skip-backup should be modeled as a boolean MCP input (an optional nullable boolean may also advertise 'null')");
		requiredArgs.Should().NotContain("skip-backup",
			because: "backup skipping must remain opt-in and the default behavior should still create backups when the argument is omitted");
	}

	private sealed record WorkspaceSyncArrangeContext(
		string RootDirectory,
		string WorkspacePath,
		string EnvironmentName,
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public ValueTask DisposeAsync() {
			CancellationTokenSource.Dispose();
			return ValueTask.CompletedTask;
		}
	}

	private sealed record WorkspaceCommandActResult(
		CallToolResult CallResult,
		CommandExecutionEnvelope Execution);
}
