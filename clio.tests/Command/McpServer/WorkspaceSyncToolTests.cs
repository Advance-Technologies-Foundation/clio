using System;
using System.IO;
using System.Linq;
using System.Threading;
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
[Property("Module", "McpServer")]
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
	[Description("Resolves a fresh push-workspace command for the requested environment and applies the requested workspace root to IWorkspacePathBuilder instead of the process working directory.")]
	public void PushWorkspace_Should_Resolve_Command_And_Use_Requested_Workspace() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		string originalDirectory = Directory.GetCurrentDirectory();
		string workspacePath = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"pushw-tool-{Guid.NewGuid():N}")).FullName;
		FakePushWorkspaceCommand defaultCommand = new();
		FakePushWorkspaceCommand resolvedCommand = new();
		IWorkspacePathBuilder workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		resolvedCommand.WorkspacePathBuilder = workspacePathBuilder;
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PushWorkspaceCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(resolvedCommand);
		commandResolver.Resolve<IWorkspacePathBuilder>(Arg.Any<EnvironmentOptions>())
			.Returns(workspacePathBuilder);
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
			NormalizeTempPathAlias(resolvedCommand.CapturedRootPath).Should().Be(
				NormalizeTempPathAlias(workspacePath),
				because: "push-workspace must apply the requested workspace root to IWorkspacePathBuilder (ENG-93208 H1 fix) instead of mutating the process working directory");
			Directory.GetCurrentDirectory().Should().Be(originalDirectory,
				because: "push-workspace must never mutate the process-wide working directory");
			workspacePathBuilder.RootPath.Should().BeNull(
				because: "the tool must reset the explicit workspace root once execution completes");
		}
		finally {
			ConsoleLogger.Instance.ClearMessages();
			Directory.Delete(workspacePath, recursive: true);
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Forwards skip-backup to push-workspace only when the caller explicitly sets it in MCP arguments.")]
	public void PushWorkspace_Should_Forward_SkipBackup_When_Requested() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		string originalDirectory = Directory.GetCurrentDirectory();
		string workspacePath = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"pushw-skip-backup-{Guid.NewGuid():N}")).FullName;
		FakePushWorkspaceCommand defaultCommand = new();
		FakePushWorkspaceCommand resolvedCommand = new();
		IWorkspacePathBuilder workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		resolvedCommand.WorkspacePathBuilder = workspacePathBuilder;
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PushWorkspaceCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(resolvedCommand);
		commandResolver.Resolve<IWorkspacePathBuilder>(Arg.Any<EnvironmentOptions>())
			.Returns(workspacePathBuilder);
		PushWorkspaceTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, new System.IO.Abstractions.FileSystem());

		try {
			// Act
			CommandExecutionResult result = tool.PushWorkspace(new PushWorkspaceArgs("dev", workspacePath, true));

			// Assert
			result.ExitCode.Should().Be(0,
				because: "the MCP tool should remain executable when skip-backup is explicitly requested");
			resolvedCommand.CapturedOptions.Should().NotBeNull(
				because: "the resolved push-workspace command should receive the forwarded MCP options");
			resolvedCommand.CapturedOptions!.SkipBackup.Should().BeTrue(
				because: "the MCP wrapper should preserve an explicitly requested backup skip flag");
		}
		finally {
			ConsoleLogger.Instance.ClearMessages();
			Directory.SetCurrentDirectory(originalDirectory);
			Directory.Delete(workspacePath, recursive: true);
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves a fresh restore-workspace command for the requested environment and applies the requested workspace root to IWorkspacePathBuilder instead of the process working directory.")]
	public void RestoreWorkspace_Should_Resolve_Command_And_Use_Requested_Workspace() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		string originalDirectory = Directory.GetCurrentDirectory();
		string workspacePath = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"restorew-tool-{Guid.NewGuid():N}")).FullName;
		FakeRestoreWorkspaceCommand defaultCommand = new();
		FakeRestoreWorkspaceCommand resolvedCommand = new();
		IWorkspacePathBuilder workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		resolvedCommand.WorkspacePathBuilder = workspacePathBuilder;
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<RestoreWorkspaceCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(resolvedCommand);
		commandResolver.Resolve<IWorkspacePathBuilder>(Arg.Any<EnvironmentOptions>())
			.Returns(workspacePathBuilder);
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
			NormalizeTempPathAlias(resolvedCommand.CapturedRootPath).Should().Be(
				NormalizeTempPathAlias(workspacePath),
				because: "restore-workspace must apply the requested workspace root to IWorkspacePathBuilder (ENG-93208 H1 fix) instead of mutating the process working directory");
			Directory.GetCurrentDirectory().Should().Be(originalDirectory,
				because: "restore-workspace must never mutate the process-wide working directory");
			workspacePathBuilder.RootPath.Should().BeNull(
				because: "the tool must reset the explicit workspace root once execution completes");
		}
		finally {
			ConsoleLogger.Instance.ClearMessages();
			Directory.Delete(workspacePath, recursive: true);
		}
	}

	[Test]
	[Category("Unit")]
	[Description("push-workspace must not contend for the process-wide CwdLock, so a concurrent page-sync-style operation holding it never head-of-line-blocks a different tenant's workspace push (ENG-93208 cross-tenant isolation).")]
	public void PushWorkspace_Should_Not_Block_On_CwdLock_Held_By_Concurrent_Operation() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		string workspacePath = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"pushw-cwdlock-{Guid.NewGuid():N}")).FullName;
		FakePushWorkspaceCommand defaultCommand = new();
		FakePushWorkspaceCommand resolvedCommand = new();
		IWorkspacePathBuilder workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		resolvedCommand.WorkspacePathBuilder = workspacePathBuilder;
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PushWorkspaceCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(resolvedCommand);
		commandResolver.Resolve<IWorkspacePathBuilder>(Arg.Any<EnvironmentOptions>())
			.Returns(workspacePathBuilder);
		PushWorkspaceTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, new System.IO.Abstractions.FileSystem());

		using ManualResetEventSlim cwdLockHeld = new(false);
		using ManualResetEventSlim releaseLock = new(false);
		Thread cwdLockHolder = new(() => {
			lock (McpToolExecutionLock.CwdLock) {
				cwdLockHeld.Set();
				releaseLock.Wait(TimeSpan.FromSeconds(5));
			}
		});

		try {
			// Act
			cwdLockHolder.Start();
			bool lockAcquiredByHolder = cwdLockHeld.Wait(TimeSpan.FromSeconds(5));
			System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
			CommandExecutionResult result = tool.PushWorkspace(new PushWorkspaceArgs("dev", workspacePath));
			stopwatch.Stop();

			// Assert
			lockAcquiredByHolder.Should().BeTrue(
				because: "the background thread must actually hold CwdLock before push-workspace runs, or this test proves nothing");
			result.ExitCode.Should().Be(0,
				because: "push-workspace should still succeed while a concurrent operation holds CwdLock");
			stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
				because: "push-workspace must not wait on McpToolExecutionLock.CwdLock, which a concurrent page-sync-style operation holds for up to 5s in this test");
		}
		finally {
			releaseLock.Set();
			cwdLockHolder.Join(TimeSpan.FromSeconds(5));
			ConsoleLogger.Instance.ClearMessages();
			Directory.Delete(workspacePath, recursive: true);
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Reports an unknown environment as a graceful exit-code-1 failure when it surfaces while resolving IWorkspacePathBuilder, matching the failure shape execute() itself would produce a moment later for the same unresolvable environment.")]
	public void PushWorkspace_Should_Report_Unresolvable_Environment_Gracefully() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		string workspacePath = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"pushw-badenv-{Guid.NewGuid():N}")).FullName;
		FakePushWorkspaceCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IWorkspacePathBuilder>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new EnvironmentResolutionException("Environment 'missing' was not found."));
		PushWorkspaceTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, new System.IO.Abstractions.FileSystem());

		try {
			// Act
			CommandExecutionResult result = tool.PushWorkspace(new PushWorkspaceArgs("missing", workspacePath));

			// Assert
			result.ExitCode.Should().Be(1,
				because: "an unresolvable environment is an expected, caller-actionable failure (exit code 1), not an unexpected runtime crash");
			result.Output.Should().Contain(message =>
				message.GetType() == typeof(ErrorMessage) &&
				((string)message.Value!).Contains("Environment 'missing' was not found."),
				because: "the failure should surface the environment-resolution diagnostic to the caller");
			defaultCommand.CapturedOptions.Should().BeNull(
				because: "push-workspace must not execute the command when its workspace root cannot be resolved");
		}
		finally {
			ConsoleLogger.Instance.ClearMessages();
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

		public IWorkspacePathBuilder? WorkspacePathBuilder { get; set; }

		public string? CapturedRootPath { get; private set; }

		public FakePushWorkspaceCommand()
			: base(
				Substitute.For<IWorkspace>(),
				new UnlockPackageCommand(
					Substitute.For<IPackageLockManager>(),
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
			CapturedRootPath = WorkspacePathBuilder?.RootPath;
			return 0;
		}
	}

	private static string? NormalizeTempPathAlias(string? path) {
		if (path is null) {
			return null;
		}

		return path.StartsWith("/private/var/", StringComparison.Ordinal)
			? path.Substring("/private".Length)
			: path;
	}

	private sealed class FakeRestoreWorkspaceCommand : RestoreWorkspaceCommand {
		public RestoreWorkspaceOptions? CapturedOptions { get; private set; }

		public IWorkspacePathBuilder? WorkspacePathBuilder { get; set; }

		public string? CapturedRootPath { get; private set; }

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
					Substitute.For<IWorkingDirectoriesProvider>())) {
		}

		public override int Execute(RestoreWorkspaceOptions options) {
			CapturedOptions = options;
			CapturedRootPath = WorkspacePathBuilder?.RootPath;
			return 0;
		}
	}
}
