using System;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Prompts;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Common.Skills;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class SkillManagementToolTests {
	[Test]
	[Category("Unit")]
	[Description("Advertises stable MCP tool names for install-toolkit, update-toolkit, and delete-toolkit so callers and tests share the same identifiers.")]
	public void SkillManagementTools_ShouldAdvertiseStableToolNames() {
		// Arrange & Act
		string installToolName = InstallSkillsTool.ToolName;
		string updateToolName = UpdateSkillTool.ToolName;
		string deleteToolName = DeleteSkillTool.ToolName;

		// Assert
		installToolName.Should().Be("install-toolkit", because: "the install MCP tool name must remain stable");
		updateToolName.Should().Be("update-toolkit", because: "the update MCP tool name must remain stable");
		deleteToolName.Should().Be("delete-toolkit", because: "the delete MCP tool name must remain stable");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps install-toolkit MCP arguments (target, repo) into install-toolkit command options.")]
	public void InstallSkills_ShouldMapTargetAndRepoArguments() {
		// Arrange
		FakeInstallSkillsCommand command = new();
		InstallSkillsTool tool = new(command, ConsoleLogger.Instance);

		// Act
		CommandExecutionResult result = tool.InstallSkills(new InstallSkillsArgs("codex", "url"));

		// Assert
		result.ExitCode.Should().Be(0, because: "the tool should forward a valid command payload");
		command.CapturedOptions.Should().NotBeNull(because: "the command should receive mapped options");
		command.CapturedOptions!.Target.Should().Be("codex", because: "the requested target must be preserved");
		command.CapturedOptions.Repo.Should().Be("url", because: "the requested repo must be preserved");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps update-toolkit MCP arguments (target, repo) into update-toolkit command options.")]
	public void UpdateSkill_ShouldMapTargetAndRepoArguments() {
		// Arrange
		FakeUpdateSkillCommand command = new();
		UpdateSkillTool tool = new(command, ConsoleLogger.Instance);

		// Act
		CommandExecutionResult result = tool.UpdateSkill(new UpdateSkillArgs("cursor", "url"));

		// Assert
		result.ExitCode.Should().Be(0, because: "the tool should forward a valid command payload");
		command.CapturedOptions!.Target.Should().Be("cursor", because: "the requested target must be preserved");
		command.CapturedOptions.Repo.Should().Be("url", because: "the requested repo must be preserved");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps delete-toolkit MCP argument (target) into delete-toolkit command options.")]
	public void DeleteSkill_ShouldMapTargetArgument() {
		// Arrange
		FakeDeleteSkillCommand command = new();
		DeleteSkillTool tool = new(command, ConsoleLogger.Instance);

		// Act
		CommandExecutionResult result = tool.DeleteSkill(new DeleteSkillArgs("claude"));

		// Assert
		result.ExitCode.Should().Be(0, because: "the tool should forward a valid command payload");
		command.CapturedOptions!.Target.Should().Be("claude", because: "the requested target must be preserved");
	}

	[Test]
	[Category("Unit")]
	[Description("Install and update are non-destructive and idempotent; delete is destructive and idempotent.")]
	public void SkillManagementTools_ShouldAdvertiseExpectedSafetyFlags() {
		// Arrange
		McpServerToolAttribute install = GetToolAttribute(typeof(InstallSkillsTool), nameof(InstallSkillsTool.InstallSkills));
		McpServerToolAttribute update = GetToolAttribute(typeof(UpdateSkillTool), nameof(UpdateSkillTool.UpdateSkill));
		McpServerToolAttribute delete = GetToolAttribute(typeof(DeleteSkillTool), nameof(DeleteSkillTool.DeleteSkill));

		// Assert
		install.Destructive.Should().BeFalse(because: "install adds/refreshes plugins but does not destroy user content");
		install.Idempotent.Should().BeTrue(because: "re-running install converges to the same installed state");
		update.Destructive.Should().BeFalse(because: "update refreshes the plugin in place and is no longer marked destructive");
		update.Idempotent.Should().BeTrue(because: "re-running update converges to the same updated state");
		delete.Destructive.Should().BeTrue(because: "delete removes plugin/marketplace/rule artifacts");
		delete.Idempotent.Should().BeTrue(because: "deleting an already-clean agent is a safe no-op");
	}

	[Test]
	[Category("Unit")]
	[Description("Prompt guidance references the exact tool names and the new target option.")]
	public void SkillManagementPrompt_ShouldMentionTarget_AndToolNames() {
		// Act
		string installPrompt = SkillManagementPrompt.InstallSkills("codex", "repo");
		string updatePrompt = SkillManagementPrompt.UpdateSkill(null, null);
		string deletePrompt = SkillManagementPrompt.DeleteSkill(null);

		// Assert
		installPrompt.Should().Contain(InstallSkillsTool.ToolName, because: "the prompt should reference the exact tool name");
		installPrompt.Should().Contain("target", because: "the prompt should teach callers about the target option");
		updatePrompt.Should().Contain(UpdateSkillTool.ToolName, because: "the prompt should reference the exact tool name");
		updatePrompt.Should().Contain("all detected agents", because: "omitting target should mean all detected agents");
		deletePrompt.Should().Contain(DeleteSkillTool.ToolName, because: "the prompt should reference the exact tool name");
	}

	private static McpServerToolAttribute GetToolAttribute(Type toolType, string methodName) {
		return toolType
			.GetMethod(methodName)!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Cast<McpServerToolAttribute>()
			.Single();
	}

	private sealed class FakeInstallSkillsCommand()
		: InstallSkillsCommand(Substitute.For<ISkillInstallService>(), Substitute.For<ILogger>()) {
		public InstallSkillsOptions? CapturedOptions { get; private set; }

		public override int Execute(InstallSkillsOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}

	private sealed class FakeUpdateSkillCommand()
		: UpdateSkillCommand(Substitute.For<ISkillInstallService>(), Substitute.For<ILogger>()) {
		public UpdateSkillOptions? CapturedOptions { get; private set; }

		public override int Execute(UpdateSkillOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}

	private sealed class FakeDeleteSkillCommand()
		: DeleteSkillCommand(Substitute.For<ISkillInstallService>(), Substitute.For<ILogger>()) {
		public DeleteSkillOptions? CapturedOptions { get; private set; }

		public override int Execute(DeleteSkillOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
