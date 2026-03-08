using System.IO;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.ChainItems;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public class AddPackageToolTests {

	[Test]
	[Description("Resolves add-package for the requested environment, executes it from the requested path, and restores the original working directory afterwards.")]
	[Category("Unit")]
	public async Task AddPackage_Should_Run_In_Child_Process_For_Workspace_Path() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeAddPackageCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		IProcessExecutor processExecutor = Substitute.For<IProcessExecutor>();
		processExecutor.ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult {
				Started = true,
				ExitCode = 0,
				StandardOutput = "Done"
			}));
		AddPackageTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, processExecutor);
		string workingDirectory = TestContext.CurrentContext.WorkDirectory;
		string workspaceSettingsDirectory = Path.Combine(workingDirectory, ".clio");
		string workspaceSettingsPath = Path.Combine(workspaceSettingsDirectory, "workspaceSettings.json");
		Directory.CreateDirectory(workspaceSettingsDirectory);
		File.WriteAllText(workspaceSettingsPath, "{}");

		// Act
		CommandExecutionResult result = await tool.AddPackage(new AddPackageArgs(
			"MyPackage",
			true,
			workingDirectory,
			"docker_fix2",
			@"C:\Builds\8.3.0.1234"));

		// Assert
		result.ExitCode.Should().Be(0, "because the child process returned success");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<AddPackageCommand>(default);
		defaultCommand.CapturedOptions.Should().BeNull("because workspace-path execution should run in a child process");
		await processExecutor.Received(1).ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(options =>
			options.Program == "dotnet"
			&& options.WorkingDirectory == workingDirectory
			&& options.Arguments.Contains("add-package")
			&& options.Arguments.Contains("\"MyPackage\"")
			&& options.Arguments.Contains("--asApp")
			&& options.Arguments.Contains("\"docker_fix2\"")
			&& options.Arguments.Contains("\"C:\\Builds\\8.3.0.1234\"")));
		File.Delete(workspaceSettingsPath);
		Directory.Delete(workspaceSettingsDirectory);
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Runs add-package with the default command when no environment name is supplied.")]
	[Category("Unit")]
	public async Task AddPackage_Should_Use_Default_Command_When_Environment_Is_Not_Provided() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeAddPackageCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		IProcessExecutor processExecutor = Substitute.For<IProcessExecutor>();
		AddPackageTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, processExecutor);

		// Act
		CommandExecutionResult result = await tool.AddPackage(new AddPackageArgs("MyPackage", false));

		// Assert
		result.ExitCode.Should().Be(0, "because local add-package execution does not require an environment");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<AddPackageCommand>(default);
		await processExecutor.DidNotReceiveWithAnyArgs().ExecuteAndCaptureAsync(default);
		defaultCommand.CapturedOptions.Should().NotBeNull("because the default command should handle local execution");
		defaultCommand.CapturedOptions.Environment.Should().BeNull("because no environment was requested");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Rejects workspace-path values that are not clio workspaces.")]
	[Category("Unit")]
	public async Task AddPackage_Should_Reject_NonWorkspace_Path() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeAddPackageCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		IProcessExecutor processExecutor = Substitute.For<IProcessExecutor>();
		AddPackageTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, processExecutor);
		string invalidWorkspacePath = TestContext.CurrentContext.WorkDirectory;

		// Act
		CommandExecutionResult result = await tool.AddPackage(new AddPackageArgs("MyPackage", false, invalidWorkspacePath));

		// Assert
		result.ExitCode.Should().Be(1, "because explicit workspace-path must point to a clio workspace root");
		result.Output.Should().ContainSingle(message => message.Value.ToString().Contains(".clio\\workspaceSettings.json"),
			"because the tool should explain the workspace-root requirement");
		await processExecutor.DidNotReceiveWithAnyArgs().ExecuteAndCaptureAsync(default);
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeAddPackageCommand : AddPackageCommand {
		public AddPackageOptions CapturedOptions { get; private set; }

		public FakeAddPackageCommand()
			: base(
				Substitute.For<IPackageCreator>(),
				Substitute.For<ILogger>(),
				Substitute.For<IFollowUpChain>(),
				Substitute.For<IFollowupUpChainItem>()) {
		}

		public override int Execute(AddPackageOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
