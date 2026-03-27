using System.Diagnostics;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for workspace-local skill management MCP tools.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("workspace-skill-management")]
[NonParallelizable]
public sealed class SkillManagementToolE2ETests {
[Test]
[AllureTag(InstallSkillsTool.ToolName)]
[Description("Installs all discovered skills into a temporary workspace through the MCP install-skills tool.")]
[AllureDescription("Starts the real clio MCP server, installs all skills from a local git repository into a temporary workspace, and verifies the copied skill files and manifest.")]
[AllureName("Install Skills Tool installs all skills into the workspace")]
public async Task InstallSkills_ShouldInstallAllSkills() {
		// Arrange
		await using SkillManagementArrangeContext arrangeContext = await ArrangeAsync();
		CreateSkillRepository(arrangeContext.RepositoryPath, new Dictionary<string, string> {
			["alpha"] = "alpha v1",
			["beta"] = "beta v1"
		});

		// Act
		SkillManagementActResult actResult = await CallToolAsync(
			arrangeContext,
			InstallSkillsTool.ToolName,
			new Dictionary<string, object?> {
				["workspacePath"] = arrangeContext.WorkspacePath,
				["repo"] = arrangeContext.RepositoryPath
			});

		// Assert
		AssertToolCallSucceeded(actResult);
		AssertCommandExitCode(actResult, 0);
		AssertSuccessIncludesInfoMessage(actResult);
		File.Exists(Path.Combine(arrangeContext.WorkspacePath, ".agents", "skills", "alpha", "SKILL.md")).Should().BeTrue(
			because: "install-skills should copy the alpha skill into the workspace");
		File.Exists(Path.Combine(arrangeContext.WorkspacePath, ".agents", "skills", "beta", "SKILL.md")).Should().BeTrue(
			because: "install-skills should copy the beta skill into the workspace");
		File.Exists(Path.Combine(arrangeContext.WorkspacePath, ".agents", "skills", ".clio-managed.json")).Should().BeTrue(
			because: "install-skills should create the managed manifest after a successful install");
	}

[Test]
[AllureTag(InstallSkillsTool.ToolName)]
[Description("Installs only the selected skill into a temporary workspace through the MCP install-skills tool.")]
[AllureDescription("Starts the real clio MCP server, installs a single named skill from a local git repository, and verifies only the selected skill is copied.")]
[AllureName("Install Skills Tool installs one selected skill")]
public async Task InstallSkills_ShouldInstallSelectedSkillOnly() {
		// Arrange
		await using SkillManagementArrangeContext arrangeContext = await ArrangeAsync();
		CreateSkillRepository(arrangeContext.RepositoryPath, new Dictionary<string, string> {
			["alpha"] = "alpha v1",
			["beta"] = "beta v1"
		});

		// Act
		SkillManagementActResult actResult = await CallToolAsync(
			arrangeContext,
			InstallSkillsTool.ToolName,
			new Dictionary<string, object?> {
				["workspacePath"] = arrangeContext.WorkspacePath,
				["repo"] = arrangeContext.RepositoryPath,
				["skillName"] = "beta"
			});

		// Assert
		AssertToolCallSucceeded(actResult);
		AssertCommandExitCode(actResult, 0);
		AssertSuccessIncludesInfoMessage(actResult);
		File.Exists(Path.Combine(arrangeContext.WorkspacePath, ".agents", "skills", "beta", "SKILL.md")).Should().BeTrue(
			because: "install-skills should copy the requested skill into the workspace");
		File.Exists(Path.Combine(arrangeContext.WorkspacePath, ".agents", "skills", "alpha", "SKILL.md")).Should().BeFalse(
			because: "install-skills should not copy unselected skills when skillName is provided");
	}

[Test]
[AllureTag(UpdateSkillTool.ToolName)]
[Description("Updates a managed skill after the source repository advances to a new HEAD commit.")]
[AllureDescription("Starts the real clio MCP server, installs a skill from a local git repository, advances repository HEAD with a new commit, updates the managed skill, and verifies the refreshed content.")]
[AllureName("Update Skill Tool refreshes managed skills when repository HEAD changed")]
public async Task UpdateSkill_ShouldRefreshManagedSkill_WhenHeadChanges() {
		// Arrange
		await using SkillManagementArrangeContext arrangeContext = await ArrangeAsync();
		CreateSkillRepository(arrangeContext.RepositoryPath, new Dictionary<string, string> {
			["alpha"] = "alpha v1"
		});
		await CallToolAsync(
			arrangeContext,
			InstallSkillsTool.ToolName,
			new Dictionary<string, object?> {
				["workspacePath"] = arrangeContext.WorkspacePath,
				["repo"] = arrangeContext.RepositoryPath
			});
		UpdateSkillRepository(arrangeContext.RepositoryPath, "alpha", "alpha v2");

		// Act
		SkillManagementActResult actResult = await CallToolAsync(
			arrangeContext,
			UpdateSkillTool.ToolName,
			new Dictionary<string, object?> {
				["workspacePath"] = arrangeContext.WorkspacePath,
				["repo"] = arrangeContext.RepositoryPath,
				["skillName"] = "alpha"
			});

		// Assert
		AssertToolCallSucceeded(actResult);
		AssertCommandExitCode(actResult, 0);
		AssertSuccessIncludesInfoMessage(actResult);
		File.ReadAllText(Path.Combine(arrangeContext.WorkspacePath, ".agents", "skills", "alpha", "SKILL.md")).Should().Contain("alpha v2",
			because: "update-skill should replace the installed skill files when repository HEAD changed");
	}

[Test]
[AllureTag(UpdateSkillTool.ToolName)]
[Description("Reports an up-to-date no-op when update-skill is called without any repository HEAD change.")]
[AllureDescription("Starts the real clio MCP server, installs a skill from a local git repository, calls update-skill without changing repository HEAD, and verifies the command reports an up-to-date no-op.")]
[AllureName("Update Skill Tool reports no-op when repository HEAD is unchanged")]
public async Task UpdateSkill_ShouldReportNoOp_WhenHeadIsUnchanged() {
		// Arrange
		await using SkillManagementArrangeContext arrangeContext = await ArrangeAsync();
		CreateSkillRepository(arrangeContext.RepositoryPath, new Dictionary<string, string> {
			["alpha"] = "alpha v1"
		});
		await CallToolAsync(
			arrangeContext,
			InstallSkillsTool.ToolName,
			new Dictionary<string, object?> {
				["workspacePath"] = arrangeContext.WorkspacePath,
				["repo"] = arrangeContext.RepositoryPath
			});

		// Act
		SkillManagementActResult actResult = await CallToolAsync(
			arrangeContext,
			UpdateSkillTool.ToolName,
			new Dictionary<string, object?> {
				["workspacePath"] = arrangeContext.WorkspacePath,
				["repo"] = arrangeContext.RepositoryPath,
				["skillName"] = "alpha"
			});

		// Assert
		AssertToolCallSucceeded(actResult);
		AssertCommandExitCode(actResult, 0);
		AssertSuccessIncludesInfoMessage(actResult);
		actResult.Execution.Output.Should().Contain(message =>
			message.MessageType == LogDecoratorType.Info &&
			(message.Value?.Contains("already up to date", StringComparison.OrdinalIgnoreCase) ?? false),
			because: "update-skill should report an already-up-to-date no-op when repository HEAD is unchanged");
	}

[Test]
[AllureTag(DeleteSkillTool.ToolName)]
[Description("Deletes a managed skill from a temporary workspace through the MCP delete-skill tool.")]
[AllureDescription("Starts the real clio MCP server, installs a skill from a local git repository, deletes the managed skill, and verifies the workspace folder is removed.")]
[AllureName("Delete Skill Tool removes a managed skill")]
public async Task DeleteSkill_ShouldDeleteManagedSkill() {
		// Arrange
		await using SkillManagementArrangeContext arrangeContext = await ArrangeAsync();
		CreateSkillRepository(arrangeContext.RepositoryPath, new Dictionary<string, string> {
			["alpha"] = "alpha v1"
		});
		await CallToolAsync(
			arrangeContext,
			InstallSkillsTool.ToolName,
			new Dictionary<string, object?> {
				["workspacePath"] = arrangeContext.WorkspacePath,
				["repo"] = arrangeContext.RepositoryPath
			});

		// Act
		SkillManagementActResult actResult = await CallToolAsync(
			arrangeContext,
			DeleteSkillTool.ToolName,
			new Dictionary<string, object?> {
				["workspacePath"] = arrangeContext.WorkspacePath,
				["skillName"] = "alpha"
			});

		// Assert
		AssertToolCallSucceeded(actResult);
		AssertCommandExitCode(actResult, 0);
		AssertSuccessIncludesInfoMessage(actResult);
		Directory.Exists(Path.Combine(arrangeContext.WorkspacePath, ".agents", "skills", "alpha")).Should().BeFalse(
			because: "delete-skill should remove the managed skill folder from the workspace");
	}

[Test]
[AllureTag(DeleteSkillTool.ToolName)]
[Description("Rejects deletion of an unmanaged workspace skill through the MCP delete-skill tool.")]
[AllureDescription("Starts the real clio MCP server, creates an unmanaged skill folder in a temporary workspace, invokes delete-skill, and verifies the command fails without removing the folder.")]
[AllureName("Delete Skill Tool rejects unmanaged skill folders")]
public async Task DeleteSkill_ShouldRejectUnmanagedSkill() {
		// Arrange
		await using SkillManagementArrangeContext arrangeContext = await ArrangeAsync();
		Directory.CreateDirectory(Path.Combine(arrangeContext.WorkspacePath, ".agents", "skills", "alpha"));
		File.WriteAllText(Path.Combine(arrangeContext.WorkspacePath, ".agents", "skills", "alpha", "SKILL.md"), "manual");

		// Act
		SkillManagementActResult actResult = await CallToolAsync(
			arrangeContext,
			DeleteSkillTool.ToolName,
			new Dictionary<string, object?> {
				["workspacePath"] = arrangeContext.WorkspacePath,
				["skillName"] = "alpha"
			});

		// Assert
		AssertToolCallFailed(actResult);
		AssertFailureIncludesErrorMessage(actResult);
		Directory.Exists(Path.Combine(arrangeContext.WorkspacePath, ".agents", "skills", "alpha")).Should().BeTrue(
			because: "delete-skill should not remove unmanaged skill folders when it rejects the request");
	}

	[AllureStep("Arrange temporary workspace, repository, and MCP session")]
	private static async Task<SkillManagementArrangeContext> ArrangeAsync() {
		string rootDirectory = Path.Combine(Path.GetTempPath(), $"clio-skill-management-e2e-{Guid.NewGuid():N}");
		string workspacePath = Path.Combine(rootDirectory, "workspace");
		string repositoryPath = Path.Combine(rootDirectory, "repo");
		Directory.CreateDirectory(Path.Combine(workspacePath, ".clio"));
		File.WriteAllText(Path.Combine(workspacePath, ".clio", "workspaceSettings.json"), "{}");
		Directory.CreateDirectory(repositoryPath);
		McpE2ESettings settings = TestConfiguration.Load();
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new SkillManagementArrangeContext(rootDirectory, workspacePath, repositoryPath, session, cancellationTokenSource);
	}

	[AllureStep("Call workspace skill management tool")]
	private static async Task<SkillManagementActResult> CallToolAsync(
		SkillManagementArrangeContext arrangeContext,
		string toolName,
		Dictionary<string, object?> arguments) {
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(toolName,
			because: "the requested workspace skill management MCP tool must be advertised before the end-to-end call can be executed");

		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			toolName,
			arguments,
			arrangeContext.CancellationTokenSource.Token);

		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
		return new SkillManagementActResult(callResult, execution);
	}

	[AllureStep("Assert MCP tool result is successful")]
	private static void AssertToolCallSucceeded(SkillManagementActResult actResult) {
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: $"a successful workspace skill management invocation should return a normal MCP tool result. Actual MCP content: {DescribeCallResult(actResult.CallResult)}");
	}

	[AllureStep("Assert MCP tool result failed")]
	private static void AssertToolCallFailed(SkillManagementActResult actResult) {
		bool failed = actResult.CallResult.IsError == true || actResult.Execution.ExitCode != 0;
		failed.Should().BeTrue(
			because: "the workspace skill management invocation should fail for an invalid destructive request");
	}

	[AllureStep("Assert command exit code")]
	private static void AssertCommandExitCode(SkillManagementActResult actResult, int expectedExitCode) {
		actResult.Execution.ExitCode.Should().Be(expectedExitCode,
			because: "the underlying command exit code should match the scenario outcome");
	}

	[AllureStep("Assert success output contains info message")]
	private static void AssertSuccessIncludesInfoMessage(SkillManagementActResult actResult) {
		actResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "successful MCP command execution should emit human-readable log messages");
		actResult.Execution.Output!.Should().Contain(
			message => message.MessageType == LogDecoratorType.Info,
			because: "successful workspace skill management execution should report progress or completion using info-level log output");
	}

	[AllureStep("Assert failure output contains error message")]
	private static void AssertFailureIncludesErrorMessage(SkillManagementActResult actResult) {
		actResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "failed MCP command execution should emit human-readable diagnostics");
		actResult.Execution.Output!.Should().Contain(
			message => message.MessageType == LogDecoratorType.Error,
			because: "failed workspace skill management execution should report its diagnostics as error-level log output");
	}

	private static void CreateSkillRepository(string repositoryPath, IReadOnlyDictionary<string, string> skills) {
		RunGit(repositoryPath, "init");
		RunGit(repositoryPath, "config user.email codex@example.com");
		RunGit(repositoryPath, "config user.name Codex");
		foreach ((string skillName, string skillContent) in skills) {
			string skillDirectory = Path.Combine(repositoryPath, ".agents", "skills", skillName);
			Directory.CreateDirectory(skillDirectory);
			File.WriteAllText(Path.Combine(skillDirectory, "SKILL.md"), skillContent);
			File.WriteAllText(Path.Combine(skillDirectory, "README.md"), "details");
		}
		RunGit(repositoryPath, "add .");
		RunGit(repositoryPath, "commit -m initial");
	}

	private static void UpdateSkillRepository(string repositoryPath, string skillName, string skillContent) {
		string skillPath = Path.Combine(repositoryPath, ".agents", "skills", skillName, "SKILL.md");
		File.WriteAllText(skillPath, skillContent);
		RunGit(repositoryPath, "add .");
		RunGit(repositoryPath, "commit -m update");
	}

	private static void RunGit(string repositoryPath, string arguments) {
		ProcessStartInfo startInfo = new("git", arguments) {
			WorkingDirectory = repositoryPath,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};
		using Process process = Process.Start(startInfo)
			?? throw new InvalidOperationException("Unable to start git process.");
		process.WaitForExit();
		if (process.ExitCode != 0) {
			string stderr = process.StandardError.ReadToEnd();
			string stdout = process.StandardOutput.ReadToEnd();
			throw new InvalidOperationException($"Git command failed: git {arguments}{Environment.NewLine}{stderr}{stdout}");
		}
	}

	private static string DescribeCallResult(CallToolResult callResult) {
		if (callResult.Content is null || callResult.Content.Count == 0) {
			return "<no content>";
		}

		return string.Join(
			" | ",
			callResult.Content.Select(content => content?.ToString() ?? "<null>"));
	}

	private sealed record SkillManagementArrangeContext(
		string RootDirectory,
		string WorkspacePath,
		string RepositoryPath,
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
			if (Directory.Exists(RootDirectory)) {
				Directory.Delete(RootDirectory, recursive: true);
			}
		}
	}

	private sealed record SkillManagementActResult(
		CallToolResult CallResult,
		CommandExecutionEnvelope Execution);
}
