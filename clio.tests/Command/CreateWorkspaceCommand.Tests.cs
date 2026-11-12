using System;
using Clio.Command;
using Clio.Common;
using Clio.Tests.Infrastructure;
using Clio.UserEnvironment;
using Clio.WebApplication;
using Clio.Workspace;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using System.IO;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public class CreateWorkspaceCommandDocTests : BaseCommandTests<CreateWorkspaceCommandOptions>
{
	// Intentionally empty: BaseCommandTests validates that the command is documented.
}

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class CreateWorkspaceCommandTests
{
	[Test]
	[Description("When --empty is enabled, create-workspace should create a workspace in a new subfolder and not connect to any environment.")]
	public void Execute_ShouldCreateWorkspaceInSubfolder_WhenEmptyEnabled()
	{
		// Arrange
		var workspace = Substitute.For<IWorkspace>();
		var logger = Substitute.For<ILogger>();
		var installedApplication = Substitute.For<IInstalledApplication>();
		var fileSystem = Substitute.For<IFileSystem>();
		
		fileSystem.Combine(Arg.Any<string[]>())
				  .Returns(callInfo => 
					  Path.Combine(callInfo.Arg<string[]>()));
		
		ConfigurePathOperations(fileSystem);
		var settingsRepository = Substitute.For<ISettingsRepository>();
		var workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		var workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		string rootPath = GetRootedPath("temp", "root");
		string workspacePath = Path.Combine(rootPath, "my-workspace");
		workingDirectoriesProvider.CurrentDirectory.Returns(rootPath);
		settingsRepository.GetWorkspacesRoot().Returns(rootPath);
		fileSystem.ExistsDirectory(rootPath).Returns(true);
		fileSystem.ExistsDirectory(workspacePath).Returns(false);
		var command = new CreateWorkspaceCommand(workspace, logger, installedApplication, fileSystem, settingsRepository, workspacePathBuilder, workingDirectoriesProvider,
			AnsweringPing(), new EnvironmentSettings());
		var options = new CreateWorkspaceCommandOptions {
			WorkspaceName = "my-workspace",
			Empty = true
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, because: "empty create-workspace should succeed when the configured workspaces root exists");
		fileSystem.Received(1).CreateDirectoryIfNotExists(Arg.Any<string>());
		workspacePathBuilder.Received(1).RootPath = Arg.Any<string>();
		workspace.Received(1).Create(null, false, false);
		installedApplication.DidNotReceiveWithAnyArgs().GetInstalledAppInfo(default);
		logger.Received(1).WriteInfo($"Workspace created at: {workspacePath}");
	}

	[Test]
	[Description("When environment is specified and --offline is not set, create-workspace should keep existing behavior and request package names from environment.")]
	public void Execute_ShouldCreateWorkspaceWithPackageNames_WhenEnvironmentProvidedAndNotOffline()
	{
		// Arrange
		var workspace = Substitute.For<IWorkspace>();
		var logger = Substitute.For<ILogger>();
		var installedApplication = Substitute.For<IInstalledApplication>();
		var fileSystem = Substitute.For<IFileSystem>();
		
		fileSystem.Combine(Arg.Any<string[]>())
			.Returns(callInfo => 
				Path.Combine(callInfo.Arg<string[]>()));
		
		ConfigurePathOperations(fileSystem);
		var settingsRepository = Substitute.For<ISettingsRepository>();
		var workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		var workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		string currentDirectory = GetRootedPath("temp", "new-workspace");
		workingDirectoriesProvider.CurrentDirectory.Returns(currentDirectory);
		var command = new CreateWorkspaceCommand(workspace, logger, installedApplication, fileSystem, settingsRepository, workspacePathBuilder, workingDirectoriesProvider,
			AnsweringPing(), new EnvironmentSettings());
		var options = new CreateWorkspaceCommandOptions {
			Environment = "dev",
			Empty = false,
			AppCode = null
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, because: "online create-workspace should preserve the existing environment-backed behavior");
		workspacePathBuilder.Received(1).RootPath = currentDirectory;
		workspace.Received(1).Create("dev", true, false);
		workspace.Received(1).Restore(options);
		logger.Received(1).WriteInfo($"Workspace created at: {currentDirectory}");
	}

	[Test]
	[Description("When WorkspaceName is provided without --empty, create-workspace should fail with a clear message.")]
	public void Execute_ShouldFail_WhenWorkspaceNameProvidedWithoutEmpty()
	{
		// Arrange
		var workspace = Substitute.For<IWorkspace>();
		var logger = Substitute.For<ILogger>();
		var installedApplication = Substitute.For<IInstalledApplication>();
		var fileSystem = Substitute.For<IFileSystem>();
		ConfigurePathOperations(fileSystem);
		var settingsRepository = Substitute.For<ISettingsRepository>();
		var workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		var workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		workingDirectoriesProvider.CurrentDirectory.Returns(GetRootedPath("temp", "root"));
		var command = new CreateWorkspaceCommand(workspace, logger, installedApplication, fileSystem, settingsRepository, workspacePathBuilder, workingDirectoriesProvider,
			AnsweringPing(), new EnvironmentSettings());
		var options = new CreateWorkspaceCommandOptions {
			WorkspaceName = "my-workspace",
			Empty = false
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, because: "workspace name without --empty should be rejected");
		workspace.DidNotReceiveWithAnyArgs().Create(default, default);
	}

	[Test]
	[Description("When --force is enabled, create-workspace --empty should not fail even if destination folder is not empty.")]
	public void Execute_ShouldBypassDestinationNotEmptyCheck_WhenForceEnabled()
	{
		// Arrange
		var workspace = Substitute.For<IWorkspace>();
		var logger = Substitute.For<ILogger>();
		var installedApplication = Substitute.For<IInstalledApplication>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();

		fileSystem.Combine(Arg.Any<string>(), Arg.Any<string>())
			.Returns(callInfo => Path.Combine(callInfo.Arg<string>(), callInfo.Arg<string>()));
		
		fileSystem.Combine(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
			.Returns(callInfo => Path.Combine(callInfo.Arg<string>(), callInfo.Arg<string>(), callInfo.Arg<string>()));
		
		
		
		ConfigurePathOperations(fileSystem);
		var settingsRepository = Substitute.For<ISettingsRepository>();
		var workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		var workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		string rootPath = GetRootedPath("temp", "root");
		settingsRepository.GetWorkspacesRoot().Returns(rootPath);
		workingDirectoriesProvider.CurrentDirectory.Returns(rootPath);
		fileSystem.ExistsDirectory(Arg.Any<string>()).Returns(true);
		fileSystem.IsEmptyDirectory(Arg.Any<string>()).Returns(false);

		var command = new CreateWorkspaceCommand(workspace, logger, installedApplication, fileSystem,
			settingsRepository, workspacePathBuilder, workingDirectoriesProvider,
			AnsweringPing(), new EnvironmentSettings());
		var options = new CreateWorkspaceCommandOptions {
			WorkspaceName = "my-workspace",
			Empty = true,
			Force = true
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, because: "--force should allow creating workspace in a non-empty destination folder");
		workspace.Received(1).Create(null, false, true);
	}

	[Test]
	[Description("When --directory is provided with --empty, create-workspace should create the workspace under that explicit absolute directory.")]
	public void Execute_ShouldCreateWorkspaceUnderExplicitDirectory_WhenDirectoryProvided() {
		// Arrange
		IWorkspace workspace = Substitute.For<IWorkspace>();
		ILogger logger = Substitute.For<ILogger>();
		IInstalledApplication installedApplication = Substitute.For<IInstalledApplication>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.Combine(Arg.Any<string[]>())
				  .Returns(callInfo => 
					  Path.Combine(callInfo.Arg<string[]>()));
		ConfigurePathOperations(fileSystem);
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		IWorkspacePathBuilder workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		string baseDirectory = GetRootedPath("workspaces");
		string workspacePath = Path.Combine(baseDirectory, "my-workspace");
		fileSystem.ExistsDirectory(baseDirectory).Returns(true);
		fileSystem.ExistsDirectory(workspacePath).Returns(false);
		CreateWorkspaceCommand command = new (workspace, logger, installedApplication, fileSystem,
			settingsRepository, workspacePathBuilder, workingDirectoriesProvider,
			AnsweringPing(), new EnvironmentSettings());
		CreateWorkspaceCommandOptions options = new () {
			WorkspaceName = "my-workspace",
			Empty = true,
			Directory = baseDirectory
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, because: "an explicit absolute directory should override the global workspaces root");
		workspacePathBuilder.Received(1).RootPath = workspacePath;
		settingsRepository.DidNotReceive().GetWorkspacesRoot();
		logger.Received(1).WriteInfo($"Workspace created at: {workspacePath}");
	}

	[Test]
	[Description("When --directory is omitted, create-workspace --empty should fall back to the global workspaces-root setting.")]
	public void Execute_ShouldUseGlobalWorkspacesRoot_WhenDirectoryOmitted() {
		// Arrange
		var workspace = Substitute.For<IWorkspace>();
		var logger = Substitute.For<ILogger>();
		var installedApplication = Substitute.For<IInstalledApplication>();
		var fileSystem = Substitute.For<IFileSystem>();
		fileSystem.Combine(Arg.Any<string[]>())
				  .Returns(callInfo => 
					  Path.Combine(callInfo.Arg<string[]>()));
		ConfigurePathOperations(fileSystem);
		var settingsRepository = Substitute.For<ISettingsRepository>();
		var workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		var workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		string globalRoot = GetRootedPath("global-workspaces");
		string workspacePath = Path.Combine(globalRoot, "my-workspace");
		settingsRepository.GetWorkspacesRoot().Returns(globalRoot);
		fileSystem.ExistsDirectory(globalRoot).Returns(true);
		fileSystem.ExistsDirectory(workspacePath).Returns(false);
		var command = new CreateWorkspaceCommand(workspace, logger, installedApplication, fileSystem,
			settingsRepository, workspacePathBuilder, workingDirectoriesProvider,
			AnsweringPing(), new EnvironmentSettings());
		var options = new CreateWorkspaceCommandOptions {
			WorkspaceName = "my-workspace",
			Empty = true
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, because: "create-workspace --empty should use the configured global workspaces root when no explicit directory is provided");
		settingsRepository.Received(1).GetWorkspacesRoot();
		workspacePathBuilder.Received(1).RootPath = workspacePath;
		logger.Received(1).WriteInfo($"Workspace created at: {workspacePath}");
	}

	[Test]
	[Description("When neither --directory nor appsettings workspaces-root is available, create-workspace --empty should fail with a clear error.")]
	public void Execute_ShouldFail_WhenDirectoryAndGlobalRootAreMissing() {
		// Arrange
		var workspace = Substitute.For<IWorkspace>();
		var logger = Substitute.For<ILogger>();
		var installedApplication = Substitute.For<IInstalledApplication>();
		var fileSystem = Substitute.For<IFileSystem>();
		ConfigurePathOperations(fileSystem);
		var settingsRepository = Substitute.For<ISettingsRepository>();
		var workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		var workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		settingsRepository.GetWorkspacesRoot().Returns(string.Empty);
		var command = new CreateWorkspaceCommand(workspace, logger, installedApplication, fileSystem,
			settingsRepository, workspacePathBuilder, workingDirectoriesProvider,
			AnsweringPing(), new EnvironmentSettings());
		var options = new CreateWorkspaceCommandOptions {
			WorkspaceName = "my-workspace",
			Empty = true
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, because: "empty workspace creation needs either an explicit directory or a configured workspaces root");
		logger.Received().WriteError(Arg.Is<string>(message => message.Contains("workspaces-root")));
	}

	[Test]
	[Description("When the resolved workspaces root does not exist, create-workspace --empty should fail before creating local files.")]
	public void Execute_ShouldFail_WhenResolvedWorkspacesRootDoesNotExist() {
		// Arrange
		var workspace = Substitute.For<IWorkspace>();
		var logger = Substitute.For<ILogger>();
		var installedApplication = Substitute.For<IInstalledApplication>();
		var fileSystem = Substitute.For<IFileSystem>();
		ConfigurePathOperations(fileSystem);
		var settingsRepository = Substitute.For<ISettingsRepository>();
		var workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		var workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		string missingRoot = GetRootedPath("missing-root");
		settingsRepository.GetWorkspacesRoot().Returns(missingRoot);
		fileSystem.ExistsDirectory(missingRoot).Returns(false);
		var command = new CreateWorkspaceCommand(workspace, logger, installedApplication, fileSystem,
			settingsRepository, workspacePathBuilder, workingDirectoriesProvider,
			AnsweringPing(), new EnvironmentSettings());
		var options = new CreateWorkspaceCommandOptions {
			WorkspaceName = "my-workspace",
			Empty = true
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, because: "workspace creation should not proceed under a missing base directory");
		workspace.DidNotReceiveWithAnyArgs().Create(default, default, default);
	}

	[Test]
	[Description("When --directory is relative, create-workspace --empty should reject it because the command requires an absolute path.")]
	public void Execute_ShouldFail_WhenDirectoryIsRelative() {
		// Arrange
		var workspace = Substitute.For<IWorkspace>();
		var logger = Substitute.For<ILogger>();
		var installedApplication = Substitute.For<IInstalledApplication>();
		var fileSystem = Substitute.For<IFileSystem>();
		ConfigurePathOperations(fileSystem);
		var settingsRepository = Substitute.For<ISettingsRepository>();
		var workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		var workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		var command = new CreateWorkspaceCommand(workspace, logger, installedApplication, fileSystem,
			settingsRepository, workspacePathBuilder, workingDirectoriesProvider,
			AnsweringPing(), new EnvironmentSettings());
		var options = new CreateWorkspaceCommandOptions {
			WorkspaceName = "my-workspace",
			Empty = true,
			Directory = "relative-root"
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, because: "the command contract requires an absolute directory path");
		logger.Received().WriteError(Arg.Is<string>(message => message.Contains("absolute")));
	}

	[Test]
	[Description("When --directory is provided without --empty, create-workspace should fail with a clear validation error.")]
	public void Execute_ShouldFail_WhenDirectoryProvidedWithoutEmpty() {
		// Arrange
		var workspace = Substitute.For<IWorkspace>();
		var logger = Substitute.For<ILogger>();
		var installedApplication = Substitute.For<IInstalledApplication>();
		var fileSystem = Substitute.For<IFileSystem>();
		ConfigurePathOperations(fileSystem);
		var settingsRepository = Substitute.For<ISettingsRepository>();
		var workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		var workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		var command = new CreateWorkspaceCommand(workspace, logger, installedApplication, fileSystem,
			settingsRepository, workspacePathBuilder, workingDirectoriesProvider,
			AnsweringPing(), new EnvironmentSettings());
		var options = new CreateWorkspaceCommandOptions {
			Directory = GetRootedPath("workspaces"),
			Empty = false
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, because: "--directory is only valid for the empty-workspace flow");
		logger.Received().WriteError(Arg.Is<string>(message => message.Contains("--directory")));
	}

	[Test]
	[Description("When an environment is given and it does not answer (here a rejected login), create-workspace should fail with the reason before it writes anything, so no .clio skeleton is left behind.")]
	public void Execute_ShouldNotWriteTheWorkspace_WhenTheEnvironmentDoesNotAnswer() {
		// Arrange
		var workspace = Substitute.For<IWorkspace>();
		var logger = Substitute.For<ILogger>();
		var installedApplication = Substitute.For<IInstalledApplication>();
		var fileSystem = Substitute.For<IFileSystem>();
		ConfigurePathOperations(fileSystem);
		var workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		workingDirectoriesProvider.CurrentDirectory.Returns(GetRootedPath("temp", "new-workspace"));
		var applicationPing = Substitute.For<IApplicationPing>();
		var environmentSettings = new EnvironmentSettings { Uri = "http://dev.example" };
		applicationPing.TryPing(environmentSettings, out Arg.Any<string>()).Returns(call => {
			call[1] = "Unauthorized Supervisor for http://dev.example";
			return false;
		});
		var command = new CreateWorkspaceCommand(workspace, logger, installedApplication, fileSystem,
			Substitute.For<ISettingsRepository>(), Substitute.For<IWorkspacePathBuilder>(), workingDirectoriesProvider,
			applicationPing, environmentSettings);
		var options = new CreateWorkspaceCommandOptions { Environment = "dev", AppCode = "UsrApp" };

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, because: "a workspace that cannot be filled from its environment must not be reported as created");
		workspace.DidNotReceiveWithAnyArgs().Create(default, default, default);
		workspace.DidNotReceiveWithAnyArgs().Restore(default);
		installedApplication.DidNotReceiveWithAnyArgs().GetInstalledAppInfo(default);
		logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.StartsWith("Workspace was not created: the environment did not answer (Unauthorized Supervisor for http://dev.example).", StringComparison.Ordinal)));
		logger.DidNotReceive().WriteInfo("Done");
	}

	[Test]
	[Description("When an environment is given, create-workspace should check that it answers before it creates the workspace from it.")]
	public void Execute_ShouldCheckTheEnvironmentBeforeCreatingTheWorkspace_WhenEnvironmentProvided() {
		// Arrange
		var workspace = Substitute.For<IWorkspace>();
		var fileSystem = Substitute.For<IFileSystem>();
		ConfigurePathOperations(fileSystem);
		var workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		workingDirectoriesProvider.CurrentDirectory.Returns(GetRootedPath("temp", "new-workspace"));
		IApplicationPing applicationPing = AnsweringPing();
		var environmentSettings = new EnvironmentSettings { Uri = "http://dev.example" };
		var command = new CreateWorkspaceCommand(workspace, Substitute.For<ILogger>(),
			Substitute.For<IInstalledApplication>(), fileSystem, Substitute.For<ISettingsRepository>(),
			Substitute.For<IWorkspacePathBuilder>(), workingDirectoriesProvider, applicationPing, environmentSettings);
		var options = new CreateWorkspaceCommandOptions { Environment = "dev" };

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, because: "an environment that answers lets the workspace be created as before");
		Received.InOrder(() => {
			applicationPing.TryPing(environmentSettings, out Arg.Any<string>());
			workspace.Create("dev", true, false);
			workspace.Restore(options);
		});
	}

	[Test]
	[Description("Without an environment (plain or --empty), create-workspace should not ping anything: it never reads from an environment.")]
	public void Execute_ShouldNotPing_WhenNoEnvironmentProvided() {
		// Arrange
		var fileSystem = Substitute.For<IFileSystem>();
		ConfigurePathOperations(fileSystem);
		var workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		workingDirectoriesProvider.CurrentDirectory.Returns(GetRootedPath("temp", "new-workspace"));
		var applicationPing = Substitute.For<IApplicationPing>();
		var command = new CreateWorkspaceCommand(Substitute.For<IWorkspace>(), Substitute.For<ILogger>(),
			Substitute.For<IInstalledApplication>(), fileSystem, Substitute.For<ISettingsRepository>(),
			Substitute.For<IWorkspacePathBuilder>(), workingDirectoriesProvider, applicationPing, new EnvironmentSettings());

		// Act
		int result = command.Execute(new CreateWorkspaceCommandOptions());

		// Assert
		result.Should().Be(0, because: "a workspace without an environment is created locally");
		applicationPing.DidNotReceiveWithAnyArgs().TryPing(default, out Arg.Any<string>());
	}

	/// <summary>A ping that answers, for the tests whose subject is not the environment check.</summary>
	private static IApplicationPing AnsweringPing() {
		var applicationPing = Substitute.For<IApplicationPing>();
		applicationPing.TryPing(Arg.Any<EnvironmentSettings>(), out Arg.Any<string>()).Returns(true);
		return applicationPing;
	}

	private static void ConfigurePathOperations(IFileSystem fileSystem) {
		fileSystem.IsPathRooted(Arg.Any<string>())
			.Returns(callInfo => Path.IsPathRooted(callInfo.Arg<string>()));
		fileSystem.GetFullPath(Arg.Any<string>())
			.Returns(callInfo => Path.GetFullPath(callInfo.Arg<string>()));
		fileSystem.Combine(Arg.Any<string[]>())
			.Returns(callInfo => Path.Combine(callInfo.Arg<string[]>()));
		fileSystem.DirectorySeparatorChar.Returns(Path.DirectorySeparatorChar);
	}

	private static string GetRootedPath(params string[] segments) {
		return TestFileSystem.GetRootedPath(segments);
	}
}
