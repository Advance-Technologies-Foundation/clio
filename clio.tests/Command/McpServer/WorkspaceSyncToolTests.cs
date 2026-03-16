using System;
using System.IO;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Prompts;
using Clio.Command.McpServer.Tools;
using Clio.Command.TIDE;
using Clio.Common;
using Clio.Package;
using Clio.UserEnvironment;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using ModelContextProtocol.Server;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public sealed class WorkspaceSyncToolTests {

	[Test]
	[Category("Unit")]
	[Description("Advertises stable MCP tool names for push-workspace and restore-workspace so tests and callers share the same identifiers.")]
	public void WorkspaceSyncTools_Should_Advertise_Stable_Tool_Names() {
		// Arrange

		// Act
		string pushToolName = PushWorkspaceTool.PushWorkspaceToolName;
		string restoreToolName = RestoreWorkspaceTool.RestoreWorkspaceToolName;

		// Assert
		pushToolName.Should().Be("push-workspace",
			because: "the MCP tool name for push-workspace should remain stable for callers and tests");
		restoreToolName.Should().Be("restore-workspace",
			because: "the MCP tool name for restore-workspace should remain stable for callers and tests");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves a fresh push-workspace command for the requested environment and executes it from the requested workspace path.")]
	public void PushWorkspace_Should_Resolve_Command_And_Use_Requested_Workspace() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		string originalDirectory = Directory.GetCurrentDirectory();
		string workspacePath = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"pushw-tool-{Guid.NewGuid():N}")).FullName;
		FakePushWorkspaceCommand defaultCommand = new();
		FakePushWorkspaceCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PushWorkspaceCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(resolvedCommand);
		PushWorkspaceTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, new System.IO.Abstractions.FileSystem());

		try {
			// Act
			CommandExecutionResult result = tool.PushWorkspace(new PushWorkspaceArgs("dev", workspacePath));

			// Assert
			result.ExitCode.Should().Be(0,
				because: "the push-workspace MCP tool should forward a valid command payload");
			commandResolver.Received(1).Resolve<PushWorkspaceCommand>(Arg.Is<EnvironmentOptions>(options =>
				options.Environment == "dev"));
			defaultCommand.CapturedOptions.Should().BeNull(
				because: "the environment-aware MCP path should execute the resolved push-workspace command instance");
			resolvedCommand.CapturedOptions.Should().NotBeNull(
				because: "the resolved push-workspace command should receive the forwarded options");
			resolvedCommand.CapturedOptions!.Environment.Should().Be("dev",
				because: "the requested environment name must be preserved for push-workspace");
			resolvedCommand.CapturedWorkingDirectory.Should().Be(workspacePath,
				because: "push-workspace must execute from the requested workspace path");
			Directory.GetCurrentDirectory().Should().Be(originalDirectory,
				because: "the MCP tool should restore the original working directory after push-workspace execution");
		}
		finally {
			ConsoleLogger.Instance.ClearMessages();
			Directory.SetCurrentDirectory(originalDirectory);
			Directory.Delete(workspacePath, recursive: true);
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves a fresh restore-workspace command for the requested environment and executes it from the requested workspace path.")]
	public void RestoreWorkspace_Should_Resolve_Command_And_Use_Requested_Workspace() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		string originalDirectory = Directory.GetCurrentDirectory();
		string workspacePath = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"restorew-tool-{Guid.NewGuid():N}")).FullName;
		FakeRestoreWorkspaceCommand defaultCommand = new();
		FakeRestoreWorkspaceCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<RestoreWorkspaceCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(resolvedCommand);
		RestoreWorkspaceTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, new System.IO.Abstractions.FileSystem());

		try {
			// Act
			CommandExecutionResult result = tool.RestoreWorkspace(new RestoreWorkspaceArgs("dev", workspacePath));

			// Assert
			result.ExitCode.Should().Be(0,
				because: "the restore-workspace MCP tool should forward a valid command payload");
			commandResolver.Received(1).Resolve<RestoreWorkspaceCommand>(Arg.Is<EnvironmentOptions>(options =>
				options.Environment == "dev"));
			defaultCommand.CapturedOptions.Should().BeNull(
				because: "the environment-aware MCP path should execute the resolved restore-workspace command instance");
			resolvedCommand.CapturedOptions.Should().NotBeNull(
				because: "the resolved restore-workspace command should receive the forwarded options");
			resolvedCommand.CapturedOptions!.Environment.Should().Be("dev",
				because: "the requested environment name must be preserved for restore-workspace");
			resolvedCommand.CapturedWorkingDirectory.Should().Be(workspacePath,
				because: "restore-workspace must execute from the requested workspace path");
			Directory.GetCurrentDirectory().Should().Be(originalDirectory,
				because: "the MCP tool should restore the original working directory after restore-workspace execution");
		}
		finally {
			ConsoleLogger.Instance.ClearMessages();
			Directory.SetCurrentDirectory(originalDirectory);
			Directory.Delete(workspacePath, recursive: true);
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects relative workspace paths for push-workspace before command execution so the MCP contract stays explicit and portable.")]
	public void PushWorkspace_Should_Reject_Relative_Workspace_Path() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakePushWorkspaceCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		PushWorkspaceTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, new System.IO.Abstractions.FileSystem());

		// Act
		CommandExecutionResult result = tool.PushWorkspace(new PushWorkspaceArgs("dev", @"relative\workspace"));

		// Assert
		result.ExitCode.Should().Be(1,
			because: "push-workspace should fail fast when the caller does not provide an absolute workspace path");
		result.Output.Should().Contain(message =>
			message.GetType() == typeof(ErrorMessage) &&
			Equals(message.Value, @"Workspace path must be absolute: relative\workspace"),
			because: "the failure should explain why the workspace path was rejected");
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the push-workspace command should not run when the workspace path is invalid");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects missing workspace directories for restore-workspace before command execution so the MCP wrapper does not guess or create directories.")]
	public void RestoreWorkspace_Should_Reject_Missing_Workspace_Directory() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		string workspacePath = Path.Combine(Path.GetTempPath(), $"restorew-missing-{Guid.NewGuid():N}");
		FakeRestoreWorkspaceCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		RestoreWorkspaceTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, new System.IO.Abstractions.FileSystem());

		// Act
		CommandExecutionResult result = tool.RestoreWorkspace(new RestoreWorkspaceArgs("dev", workspacePath));

		// Assert
		result.ExitCode.Should().Be(1,
			because: "restore-workspace should fail fast when the requested workspace directory does not exist");
		result.Output.Should().Contain(message =>
			message.GetType() == typeof(ErrorMessage) &&
			Equals(message.Value, $"Workspace path not found: {workspacePath}"),
			because: "the failure should explain that the requested workspace directory was not found");
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the restore-workspace command should not run when the workspace directory is missing");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects UNC workspace paths for push-workspace so MCP callers cannot force execution against remote network shares.")]
	public void PushWorkspace_Should_Reject_Network_Workspace_Path() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakePushWorkspaceCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		PushWorkspaceTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, new System.IO.Abstractions.FileSystem());

		// Act
		CommandExecutionResult result = tool.PushWorkspace(new PushWorkspaceArgs("dev", @"\\server\share\workspace"));

		// Assert
		result.ExitCode.Should().Be(1,
			because: "push-workspace should reject network shares to avoid executing against remote paths");
		result.Output.Should().Contain(message =>
			message.GetType() == typeof(ErrorMessage) &&
			Equals(message.Value, @"Workspace path must be a local absolute path: \\server\share\workspace"),
			because: "the failure should explain that only local absolute workspace paths are allowed");
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the push-workspace command should not run when the workspace path targets a network share");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks both workspace-sync MCP methods as destructive so MCP clients can apply safety policies before mutating local or remote state.")]
	[TestCase(nameof(PushWorkspaceTool.PushWorkspace), typeof(PushWorkspaceTool))]
	[TestCase(nameof(RestoreWorkspaceTool.RestoreWorkspace), typeof(RestoreWorkspaceTool))]
	public void WorkspaceSync_Methods_Should_Be_Marked_As_Destructive(string methodName, Type toolType) {
		// Arrange
		System.Reflection.MethodInfo method = toolType.GetMethod(methodName)!;
		McpServerToolAttribute attribute = method
			.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false)
			.Cast<McpServerToolAttribute>()
			.Single();

		// Act
		bool destructive = attribute.Destructive;

		// Assert
		destructive.Should().BeTrue(
			because: "push-workspace and restore-workspace both mutate local workspace state and/or the target environment");
	}

	[Test]
	[Category("Unit")]
	[Description("Prompt guidance for workspace-sync tools keeps the workspace-path requirement visible and references the exact production tool names.")]
	public void WorkspaceSyncPrompt_Should_Mention_Workspace_Path_And_Tool_Names() {
		// Arrange

		// Act
		string pushPrompt = WorkspaceSyncPrompt.PushWorkspace("dev", @"C:\workspace");
		string restorePrompt = WorkspaceSyncPrompt.RestoreWorkspace("dev", @"C:\workspace");

		// Assert
		pushPrompt.Should().Contain("workspace-path",
			because: "the push-workspace prompt should tell agents how to target the correct local workspace");
		pushPrompt.Should().Contain(PushWorkspaceTool.PushWorkspaceToolName,
			because: "the push-workspace prompt should reference the exact MCP tool name");
		restorePrompt.Should().Contain("workspace-path",
			because: "the restore-workspace prompt should tell agents how to target the correct local workspace");
		restorePrompt.Should().Contain(RestoreWorkspaceTool.RestoreWorkspaceToolName,
			because: "the restore-workspace prompt should reference the exact MCP tool name");
	}

	private sealed class FakePushWorkspaceCommand : PushWorkspaceCommand {
		public PushWorkspaceCommandOptions? CapturedOptions { get; private set; }

		public string? CapturedWorkingDirectory { get; private set; }

		public FakePushWorkspaceCommand()
			: base(
				Substitute.For<IWorkspace>(),
				new UnlockPackageCommand(
					Substitute.For<IPackageLockManager>(),
					Substitute.For<IClioGateway>(),
					Substitute.For<ISysSettingsManager>(),
					Substitute.For<ILogger>()),
				Substitute.For<IApplicationClientFactory>(),
				new EnvironmentSettings(),
				Substitute.For<IServiceUrlBuilder>(),
				Substitute.For<ILogger>(),
				new LinkWorkspaceWithTideRepositoryCommand(
					Substitute.For<ATF.Repository.Providers.IDataProvider>(),
					Substitute.For<IWorkspace>())) {
		}

		public override int Execute(PushWorkspaceCommandOptions options) {
			CapturedOptions = options;
			CapturedWorkingDirectory = Directory.GetCurrentDirectory();
			return 0;
		}
	}

	private sealed class FakeRestoreWorkspaceCommand : RestoreWorkspaceCommand {
		public RestoreWorkspaceOptions? CapturedOptions { get; private set; }

		public string? CapturedWorkingDirectory { get; private set; }

		public FakeRestoreWorkspaceCommand()
			: base(
				Substitute.For<IWorkspace>(),
				Substitute.For<ILogger>(),
				new CreateWorkspaceCommand(
					Substitute.For<IWorkspace>(),
					Substitute.For<ILogger>(),
					Substitute.For<IInstalledApplication>(),
					Substitute.For<IFileSystem>(),
					Substitute.For<ISettingsRepository>(),
					Substitute.For<IWorkspacePathBuilder>(),
					Substitute.For<IWorkingDirectoriesProvider>()),
				Substitute.For<IClioGateway>()) {
		}

		public override int Execute(RestoreWorkspaceOptions options) {
			CapturedOptions = options;
			CapturedWorkingDirectory = Directory.GetCurrentDirectory();
			return 0;
		}
	}
}
