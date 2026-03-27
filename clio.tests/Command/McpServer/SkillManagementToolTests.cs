using System;
using System.IO;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Prompts;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Workspaces;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;
using System.IO.Abstractions.TestingHelpers;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public sealed class SkillManagementToolTests {
	[Test]
	[Category("Unit")]
	[Description("Advertises stable MCP tool names for install-skills, update-skill, and delete-skill so callers and tests share the same identifiers.")]
	public void SkillManagementTools_ShouldAdvertiseStableToolNames() {
		// Arrange

		// Act
		string installToolName = InstallSkillsTool.ToolName;
		string updateToolName = UpdateSkillTool.ToolName;
		string deleteToolName = DeleteSkillTool.ToolName;

		// Assert
		installToolName.Should().Be("install-skills",
			because: "the install-skills MCP tool name should remain stable for callers and tests");
		updateToolName.Should().Be("update-skill",
			because: "the update-skill MCP tool name should remain stable for callers and tests");
		deleteToolName.Should().Be("delete-skill",
			because: "the delete-skill MCP tool name should remain stable for callers and tests");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps install-skills MCP arguments into install-skills command options and executes from the requested workspace path.")]
	public void InstallSkills_ShouldMapArguments_AndUseWorkspace() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		string originalDirectory = Directory.GetCurrentDirectory();
		MockFileSystem fileSystem = CreateWorkspaceFileSystem();
		FakeInstallSkillsCommand command = new(fileSystem);
		InstallSkillsTool tool = new(command, ConsoleLogger.Instance, fileSystem);

		try {
			// Act
			CommandExecutionResult result = tool.InstallSkills(new InstallSkillsArgs(@"C:\workspace", "alpha", "repo"));

			// Assert
			result.ExitCode.Should().Be(0, because: "the install-skills MCP tool should forward a valid command payload");
			command.CapturedOptions.Should().NotBeNull(because: "the install-skills command should receive mapped options");
			command.CapturedOptions!.Skill.Should().Be("alpha",
				because: "the requested skill name must be preserved for install-skills");
			command.CapturedOptions.Repo.Should().Be("repo",
				because: "the requested repository locator must be preserved for install-skills");
			command.CapturedWorkingDirectory.Should().Be(@"C:\workspace",
				because: "install-skills must execute relative to the requested workspace path");
			Directory.GetCurrentDirectory().Should().Be(originalDirectory,
				because: "the MCP wrapper should restore the original working directory after install-skills execution");
		}
		finally {
			ConsoleLogger.Instance.ClearMessages();
			Directory.SetCurrentDirectory(originalDirectory);
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Maps update-skill MCP arguments into update-skill command options and executes from the requested workspace path.")]
	public void UpdateSkill_ShouldMapArguments_AndUseWorkspace() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		string originalDirectory = Directory.GetCurrentDirectory();
		MockFileSystem fileSystem = CreateWorkspaceFileSystem();
		FakeUpdateSkillCommand command = new(fileSystem);
		UpdateSkillTool tool = new(command, ConsoleLogger.Instance, fileSystem);

		try {
			// Act
			CommandExecutionResult result = tool.UpdateSkill(new UpdateSkillArgs(@"C:\workspace", "alpha", "repo"));

			// Assert
			result.ExitCode.Should().Be(0, because: "the update-skill MCP tool should forward a valid command payload");
			command.CapturedOptions.Should().NotBeNull(because: "the update-skill command should receive mapped options");
			command.CapturedOptions!.Skill.Should().Be("alpha",
				because: "the requested skill name must be preserved for update-skill");
			command.CapturedOptions.Repo.Should().Be("repo",
				because: "the requested repository locator must be preserved for update-skill");
			command.CapturedWorkingDirectory.Should().Be(@"C:\workspace",
				because: "update-skill must execute relative to the requested workspace path");
			Directory.GetCurrentDirectory().Should().Be(originalDirectory,
				because: "the MCP wrapper should restore the original working directory after update-skill execution");
		}
		finally {
			ConsoleLogger.Instance.ClearMessages();
			Directory.SetCurrentDirectory(originalDirectory);
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Maps delete-skill MCP arguments into delete-skill command options and executes from the requested workspace path.")]
	public void DeleteSkill_ShouldMapArguments_AndUseWorkspace() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		string originalDirectory = Directory.GetCurrentDirectory();
		MockFileSystem fileSystem = CreateWorkspaceFileSystem();
		FakeDeleteSkillCommand command = new(fileSystem);
		DeleteSkillTool tool = new(command, ConsoleLogger.Instance, fileSystem);

		try {
			// Act
			CommandExecutionResult result = tool.DeleteSkill(new DeleteSkillArgs(@"C:\workspace", "alpha"));

			// Assert
			result.ExitCode.Should().Be(0, because: "the delete-skill MCP tool should forward a valid command payload");
			command.CapturedOptions.Should().NotBeNull(because: "the delete-skill command should receive mapped options");
			command.CapturedOptions!.Skill.Should().Be("alpha",
				because: "the requested skill name must be preserved for delete-skill");
			command.CapturedWorkingDirectory.Should().Be(@"C:\workspace",
				because: "delete-skill must execute relative to the requested workspace path");
			Directory.GetCurrentDirectory().Should().Be(originalDirectory,
				because: "the MCP wrapper should restore the original working directory after delete-skill execution");
		}
		finally {
			ConsoleLogger.Instance.ClearMessages();
			Directory.SetCurrentDirectory(originalDirectory);
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects non-workspace paths before install-skills command execution so MCP callers must provide a real local clio workspace.")]
	public void InstallSkills_ShouldRejectInvalidWorkspacePath() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		MockFileSystem fileSystem = new(new System.Collections.Generic.Dictionary<string, System.IO.Abstractions.TestingHelpers.MockFileData>(), @"C:\");
		FakeInstallSkillsCommand command = new(fileSystem);
		InstallSkillsTool tool = new(command, ConsoleLogger.Instance, fileSystem);

		// Act
		CommandExecutionResult result = tool.InstallSkills(new InstallSkillsArgs(@"C:\workspace"));

		// Assert
		result.ExitCode.Should().Be(1, because: "install-skills should fail fast when the requested workspace directory is not a clio workspace");
		result.Output.Should().Contain(message =>
			message.GetType() == typeof(ErrorMessage) &&
			Equals(message.Value, @"Workspace path not found: C:\workspace"),
			because: "the failure should explain why the workspace path was rejected");
		command.CapturedOptions.Should().BeNull(
			because: "the install-skills command should not run when workspace validation fails");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects UNC workspace paths for update-skill so MCP callers cannot force execution against remote network shares.")]
	public void UpdateSkill_ShouldRejectNetworkWorkspacePath() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		MockFileSystem fileSystem = CreateWorkspaceFileSystem();
		FakeUpdateSkillCommand command = new(fileSystem);
		UpdateSkillTool tool = new(command, ConsoleLogger.Instance, fileSystem);

		// Act
		CommandExecutionResult result = tool.UpdateSkill(new UpdateSkillArgs(@"\\server\share\workspace"));

		// Assert
		result.ExitCode.Should().Be(1, because: "update-skill should reject network shares to keep execution local and portable");
		result.Output.Should().Contain(message =>
			message.GetType() == typeof(ErrorMessage) &&
			Equals(message.Value, @"Workspace path must be a local absolute path: \\server\share\workspace"),
			because: "the failure should explain that only local absolute workspace paths are allowed");
		command.CapturedOptions.Should().BeNull(
			because: "the update-skill command should not run when the workspace path targets a network share");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks update-skill and delete-skill as destructive while install-skills remains non-destructive for MCP safety policies.")]
	public void SkillManagementTools_ShouldAdvertiseExpectedDestructiveFlags() {
		// Arrange
		McpServerToolAttribute installAttribute = GetToolAttribute(typeof(InstallSkillsTool), nameof(InstallSkillsTool.InstallSkills));
		McpServerToolAttribute updateAttribute = GetToolAttribute(typeof(UpdateSkillTool), nameof(UpdateSkillTool.UpdateSkill));
		McpServerToolAttribute deleteAttribute = GetToolAttribute(typeof(DeleteSkillTool), nameof(DeleteSkillTool.DeleteSkill));

		// Act

		// Assert
		installAttribute.Destructive.Should().BeFalse(
			because: "install-skills adds new workspace files but does not remove or replace existing unmanaged content");
		updateAttribute.Destructive.Should().BeTrue(
			because: "update-skill replaces managed skill files when the source commit changed");
		deleteAttribute.Destructive.Should().BeTrue(
			because: "delete-skill removes managed skill folders from the workspace");
	}

	[Test]
	[Category("Unit")]
	[Description("Prompt guidance for skill-management tools keeps the workspacePath requirement visible and references the exact production tool names.")]
	public void SkillManagementPrompt_ShouldMentionWorkspacePath_AndToolNames() {
		// Arrange

		// Act
		string installPrompt = SkillManagementPrompt.InstallSkills(@"C:\workspace", "alpha", "repo");
		string updatePrompt = SkillManagementPrompt.UpdateSkill(@"C:\workspace", "alpha", "repo");
		string deletePrompt = SkillManagementPrompt.DeleteSkill(@"C:\workspace", "alpha");

		// Assert
		installPrompt.Should().Contain("workspacePath",
			because: "the install-skills prompt should tell agents how to target the correct local workspace");
		installPrompt.Should().Contain(InstallSkillsTool.ToolName,
			because: "the install-skills prompt should reference the exact MCP tool name");
		updatePrompt.Should().Contain("workspacePath",
			because: "the update-skill prompt should tell agents how to target the correct local workspace");
		updatePrompt.Should().Contain(UpdateSkillTool.ToolName,
			because: "the update-skill prompt should reference the exact MCP tool name");
		deletePrompt.Should().Contain("workspacePath",
			because: "the delete-skill prompt should tell agents how to target the correct local workspace");
		deletePrompt.Should().Contain(DeleteSkillTool.ToolName,
			because: "the delete-skill prompt should reference the exact MCP tool name");
	}

	private static MockFileSystem CreateWorkspaceFileSystem() {
		return new MockFileSystem(new System.Collections.Generic.Dictionary<string, System.IO.Abstractions.TestingHelpers.MockFileData> {
			[@"C:\workspace\.clio\workspaceSettings.json"] = new("{}")
		}, @"C:\");
	}

	private static McpServerToolAttribute GetToolAttribute(Type toolType, string methodName) {
		return toolType
			.GetMethod(methodName)!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Cast<McpServerToolAttribute>()
			.Single();
	}

	private sealed class FakeInstallSkillsCommand : InstallSkillsCommand {
		private readonly MockFileSystem _fileSystem;

		public InstallSkillsOptions? CapturedOptions { get; private set; }
		public string? CapturedWorkingDirectory { get; private set; }

		public FakeInstallSkillsCommand(MockFileSystem fileSystem)
			: base(Substitute.For<ISkillManagementService>(), Substitute.For<IWorkspacePathBuilder>(), Substitute.For<ILogger>()) {
			_fileSystem = fileSystem;
		}

		public override int Execute(InstallSkillsOptions options) {
			CapturedOptions = options;
			CapturedWorkingDirectory = _fileSystem.Directory.GetCurrentDirectory();
			return 0;
		}
	}

	private sealed class FakeUpdateSkillCommand : UpdateSkillCommand {
		private readonly MockFileSystem _fileSystem;

		public UpdateSkillOptions? CapturedOptions { get; private set; }
		public string? CapturedWorkingDirectory { get; private set; }

		public FakeUpdateSkillCommand(MockFileSystem fileSystem)
			: base(Substitute.For<ISkillManagementService>(), Substitute.For<IWorkspacePathBuilder>(), Substitute.For<ILogger>()) {
			_fileSystem = fileSystem;
		}

		public override int Execute(UpdateSkillOptions options) {
			CapturedOptions = options;
			CapturedWorkingDirectory = _fileSystem.Directory.GetCurrentDirectory();
			return 0;
		}
	}

	private sealed class FakeDeleteSkillCommand : DeleteSkillCommand {
		private readonly MockFileSystem _fileSystem;

		public DeleteSkillOptions? CapturedOptions { get; private set; }
		public string? CapturedWorkingDirectory { get; private set; }

		public FakeDeleteSkillCommand(MockFileSystem fileSystem)
			: base(Substitute.For<ISkillManagementService>(), Substitute.For<IWorkspacePathBuilder>(), Substitute.For<ILogger>()) {
			_fileSystem = fileSystem;
		}

		public override int Execute(DeleteSkillOptions options) {
			CapturedOptions = options;
			CapturedWorkingDirectory = _fileSystem.Directory.GetCurrentDirectory();
			return 0;
		}
	}
}
