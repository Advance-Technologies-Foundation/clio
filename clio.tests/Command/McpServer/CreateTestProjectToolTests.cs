using System.IO;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Workspace;
using Clio.Workspaces;
using FluentAssertions;
using FluentValidation;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public class CreateTestProjectToolTests {

	[Test]
	[Description("Resolves new-test-project for the requested environment, executes it from the requested path, and restores the original working directory afterwards.")]
	[Category("Unit")]
	public async Task CreateTestProject_Should_Run_In_Child_Process_For_Workspace_Path() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateTestProjectCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		IProcessExecutor processExecutor = Substitute.For<IProcessExecutor>();
		processExecutor.ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult {
				Started = true,
				ExitCode = 0,
				StandardOutput = "Done"
			}));
		CreateTestProjectTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, processExecutor);
		string workingDirectory = TestContext.CurrentContext.WorkDirectory;
		string workspaceSettingsDirectory = Path.Combine(workingDirectory, ".clio");
		string workspaceSettingsPath = Path.Combine(workspaceSettingsDirectory, "workspaceSettings.json");
		Directory.CreateDirectory(workspaceSettingsDirectory);
		File.WriteAllText(workspaceSettingsPath, "{}");

		// Act
		CommandExecutionResult result = await tool.CreateTestProject(new CreateTestProjectArgs(
			"MyPackage",
			workingDirectory,
			"docker_fix2"));

		// Assert
		result.ExitCode.Should().Be(0, "because the child process returned success");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<CreateTestProjectCommand>(default);
		defaultCommand.CapturedOptions.Should().BeNull("because workspace-path execution should run in a child process");
		await processExecutor.Received(1).ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(options =>
			options.Program == "dotnet"
			&& options.WorkingDirectory == workingDirectory
			&& options.Arguments.Contains("new-test-project")
			&& options.Arguments.Contains("\"MyPackage\"")
			&& options.Arguments.Contains("\"docker_fix2\"")));
		File.Delete(workspaceSettingsPath);
		Directory.Delete(workspaceSettingsDirectory);
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Runs new-test-project with the default command when no environment name is supplied.")]
	[Category("Unit")]
	public async Task CreateTestProject_Should_Use_Default_Command_When_Environment_Is_Not_Provided() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateTestProjectCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		IProcessExecutor processExecutor = Substitute.For<IProcessExecutor>();
		CreateTestProjectTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, processExecutor);

		// Act
		CommandExecutionResult result = await tool.CreateTestProject(new CreateTestProjectArgs("MyPackage"));

		// Assert
		result.ExitCode.Should().Be(0, "because local unit-test scaffolding does not require an environment");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<CreateTestProjectCommand>(default);
		await processExecutor.DidNotReceiveWithAnyArgs().ExecuteAndCaptureAsync(default);
		defaultCommand.CapturedOptions.Should().NotBeNull("because the default command should handle local execution");
		defaultCommand.CapturedOptions.Environment.Should().BeNull("because no environment was requested");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Rejects workspace-path values that are not clio workspaces.")]
	[Category("Unit")]
	public async Task CreateTestProject_Should_Reject_NonWorkspace_Path() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateTestProjectCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		IProcessExecutor processExecutor = Substitute.For<IProcessExecutor>();
		CreateTestProjectTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, processExecutor);
		string invalidWorkspacePath = TestContext.CurrentContext.WorkDirectory;

		// Act
		CommandExecutionResult result = await tool.CreateTestProject(new CreateTestProjectArgs("MyPackage", invalidWorkspacePath));

		// Assert
		result.ExitCode.Should().Be(1, "because explicit workspace-path must point to a clio workspace root");
		result.Output.Should().ContainSingle(message => message.Value.ToString().Contains(".clio\\workspaceSettings.json"),
			"because the tool should explain the workspace-root requirement");
		await processExecutor.DidNotReceiveWithAnyArgs().ExecuteAndCaptureAsync(default);
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeCreateTestProjectCommand : CreateTestProjectCommand {
		public CreateTestProjectOptions CapturedOptions { get; private set; }

		public FakeCreateTestProjectCommand()
			: base(
				Substitute.For<IValidator<CreateTestProjectOptions>>(),
				Substitute.For<IWorkspace>(),
				Substitute.For<IWorkspacePathBuilder>(),
				Substitute.For<IWorkingDirectoriesProvider>(),
				Substitute.For<ITemplateProvider>(),
				Substitute.For<Clio.Common.IFileSystem>(),
				Substitute.For<ILogger>(),
				Substitute.For<ISolutionCreator>()) {
		}

		public override int Execute(CreateTestProjectOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
