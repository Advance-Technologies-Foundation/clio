using Clio.Command;
using Clio.Command.McpServer.Prompts;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.UserEnvironment;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using System.Linq;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public class InitWorkspaceToolTests {

	[Test]
	[Description("Maps the init-workspace MCP call into init-workspace command options without requiring structured arguments.")]
	[Category("Unit")]
	public void InitWorkspace_Should_Map_To_Empty_Command_Options() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeInitWorkspaceCommand command = new();
		InitWorkspaceTool tool = new(command, ConsoleLogger.Instance);

		// Act
		CommandExecutionResult result = tool.InitWorkspace();

		// Assert
		result.ExitCode.Should().Be(0, because: "the MCP tool should forward the init-workspace invocation");
		command.CapturedOptions.Should().NotBeNull(because: "the command should receive mapped options");
		command.CapturedOptions!.Environment.Should().BeNull(because: "the local-only MCP slice does not expose environment parameters");
		command.CapturedOptions.AppCode.Should().BeNull(because: "the local-only MCP slice should keep the contract minimal");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Advertises the stable MCP tool name for init-workspace so prompts, tests, and clients share one identifier.")]
	[Category("Unit")]
	public void InitWorkspace_Should_Expose_Stable_Tool_Name() {
		// Arrange
		string toolName = InitWorkspaceTool.InitWorkspaceToolName;

		// Act
		string[] methodNames = typeof(InitWorkspaceTool)
			.GetMethods()
			.Where(method => method.Name == nameof(InitWorkspaceTool.InitWorkspace))
			.Select(_ => toolName)
			.ToArray();

		// Assert
		methodNames.Should().ContainSingle(
			because: "the init-workspace MCP surface should expose exactly one stable local initialization entry point");
	}

	[Test]
	[Description("Prompt guidance for init-workspace references the exact tool name and explains that it preserves existing files.")]
	[Category("Unit")]
	public void InitWorkspacePrompt_Should_Reference_Tool_Name_And_Preservation_Behavior() {
		// Arrange
		string prompt = InitWorkspacePrompt.InitWorkspace();

		// Act
		string normalizedPrompt = prompt.Replace('\r', '\n');

		// Assert
		normalizedPrompt.Should().Contain(InitWorkspaceTool.InitWorkspaceToolName,
			because: "the prompt should direct callers to the concrete MCP tool name");
		normalizedPrompt.Should().Contain("must not be overwritten",
			because: "the prompt should explain when init-workspace is the correct safe choice");
	}

	private sealed class FakeInitWorkspaceCommand : InitWorkspaceCommand {
		public InitWorkspaceCommandOptions CapturedOptions { get; private set; }

		public FakeInitWorkspaceCommand()
			: base(
				Substitute.For<IWorkspace>(),
				ConsoleLogger.Instance,
				Substitute.For<IInstalledApplication>(),
				Substitute.For<IWorkspacePathBuilder>(),
				Substitute.For<IWorkingDirectoriesProvider>()) {
		}

		public override int Execute(InitWorkspaceCommandOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
