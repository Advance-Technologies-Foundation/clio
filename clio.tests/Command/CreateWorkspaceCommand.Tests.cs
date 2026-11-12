using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Common;
using Clio.Tests.Infrastructure;
using Clio.UserEnvironment;
using Clio.WebApplication;
using Clio.Workspace;
using Clio.Workspaces;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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
			AnsweringPing(), new EnvironmentSettings { Uri = "http://dev.example" });
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
			AnsweringPing(), new EnvironmentSettings { Uri = "http://dev.example" });
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
			AnsweringPing(), new EnvironmentSettings { Uri = "http://dev.example" });
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
			AnsweringPing(), new EnvironmentSettings { Uri = "http://dev.example" });
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
			AnsweringPing(), new EnvironmentSettings { Uri = "http://dev.example" });
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
			AnsweringPing(), new EnvironmentSettings { Uri = "http://dev.example" });
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
			AnsweringPing(), new EnvironmentSettings { Uri = "http://dev.example" });
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
			AnsweringPing(), new EnvironmentSettings { Uri = "http://dev.example" });
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
			AnsweringPing(), new EnvironmentSettings { Uri = "http://dev.example" });
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
			AnsweringPing(), new EnvironmentSettings { Uri = "http://dev.example" });
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

/// <summary>
/// The order create-workspace checks things in when an environment is given: the local checks, then the
/// environment, then what is read from it - all before anything is written. The command is resolved from the
/// container, as AGENTS.md "Command tests" asks.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class CreateWorkspaceCommandEnvironmentCheckTests : BaseClioModuleTests
{
	private readonly List<string> _steps = [];
	private IApplicationPing _applicationPing;
	private CreateWorkspaceCommand _command;
	private IInstalledApplication _installedApplication;
	private ILogger _logger;
	private IWorkspace _workspace;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		_workspace = Substitute.For<IWorkspace>();
		_applicationPing = Substitute.For<IApplicationPing>();
		_installedApplication = Substitute.For<IInstalledApplication>();
		_logger = Substitute.For<ILogger>();
		IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		workingDirectoriesProvider.CurrentDirectory.Returns(TestFileSystem.GetRootedPath("temp", "new-workspace"));
		containerBuilder.AddSingleton(_workspace);
		containerBuilder.AddSingleton(_applicationPing);
		containerBuilder.AddSingleton(_installedApplication);
		containerBuilder.AddSingleton(_logger);
		containerBuilder.AddSingleton(workingDirectoriesProvider);
	}

	public override void Setup() {
		// NUnit reuses the fixture instance, so a test that clears the URL must not leak into the next one.
		EnvironmentSettings.Uri = "http://localhost";
		base.Setup();
		_steps.Clear();
		_workspace.When(workspace => workspace.ValidateCanCreate(Arg.Any<bool>())).Do(_ => _steps.Add("validate"));
		_applicationPing.TryPing(Arg.Any<EnvironmentSettings>(), out Arg.Any<string>()).Returns(_ => {
			_steps.Add("ping");
			return true;
		});
		_workspace.When(workspace => workspace.Create(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>()))
			.Do(_ => _steps.Add("create"));
		_workspace.When(workspace => workspace.Restore(Arg.Any<WorkspaceOptions>())).Do(_ => _steps.Add("restore"));
		_command = Container.GetRequiredService<CreateWorkspaceCommand>();
	}

	[Test]
	[Description("With an environment, create-workspace runs the local checks, then pings the environment, then creates and restores - in that order.")]
	public void Execute_ShouldCheckLocallyThenTheEnvironmentThenWrite_WhenEnvironmentProvided() {
		// Act
		int result = _command.Execute(new CreateWorkspaceCommandOptions { Environment = "dev" });

		// Assert
		result.Should().Be(0, because: "an environment that answers lets the workspace be created as before");
		_steps.Should().Equal(["validate", "ping", "create", "restore"],
			because: "nothing is written before both the local checks and the environment check have passed");
	}

	[Test]
	[Description("A folder that is already a workspace is reported as such, before the environment is pinged, so a stopped environment does not hide the local error.")]
	public void Execute_ShouldShowTheLocalError_WhenTheFolderIsAlreadyAWorkspace() {
		// Arrange
		const string localError = "This operation can not execute inside existing workspace!";
		_workspace.When(workspace => workspace.ValidateCanCreate(Arg.Any<bool>()))
			.Do(_ => throw new InvalidOperationException(localError));

		// Act
		int result = _command.Execute(new CreateWorkspaceCommandOptions { Environment = "dev" });

		// Assert
		result.Should().Be(1, because: "a workspace cannot be created inside another one");
		ErrorsLogged().Should().ContainSingle(message => message.StartsWith(localError, StringComparison.Ordinal),
			because: "the local error is the one to fix, not a login against an environment that would not have been used");
		_applicationPing.ReceivedCalls().Should().BeEmpty(because: "the environment is not pinged when a local check has already failed");
	}

	[Test]
	[Description("An environment that does not answer (here a rejected login) fails the command with the reason before anything is written, so no .clio skeleton is left behind.")]
	public void Execute_ShouldNotWriteTheWorkspace_WhenTheEnvironmentDoesNotAnswer() {
		// Arrange
		_applicationPing.TryPing(Arg.Any<EnvironmentSettings>(), out Arg.Any<string>()).Returns(call => {
			call[1] = "Unauthorized Supervisor for http://localhost";
			return false;
		});

		// Act
		int result = _command.Execute(new CreateWorkspaceCommandOptions { Environment = "dev", AppCode = "UsrApp" });

		// Assert
		result.Should().Be(1, because: "a workspace that cannot be filled from its environment must not be reported as created");
		ErrorsLogged().Should().ContainSingle(message => message.StartsWith(
				"Workspace was not created: the environment did not answer (Unauthorized Supervisor for http://localhost).",
				StringComparison.Ordinal),
			because: "the caller must see why nothing was created");
		_steps.Should().Equal(["validate"], because: "neither Create nor Restore runs after the ping fails");
		_installedApplication.ReceivedCalls().Should().BeEmpty(because: "the application is not read from an environment that did not answer");
	}

	[Test]
	[Description("An environment name that is not registered resolves to settings without a URL; the command says so instead of reporting a client-construction failure as a stopped site.")]
	public void Execute_ShouldNameTheUnregisteredEnvironment_WhenItHasNoUrl() {
		// Arrange
		EnvironmentSettings.Uri = null;

		// Act
		int result = _command.Execute(new CreateWorkspaceCommandOptions { Environment = "dev" });

		// Assert
		result.Should().Be(1, because: "there is no environment to create the workspace from");
		ErrorsLogged().Should().ContainSingle(message => message.StartsWith(
				"Workspace was not created: environment 'dev' is not registered.", StringComparison.Ordinal),
			because: "the fix is to register the environment, not to start it");
		_applicationPing.ReceivedCalls().Should().BeEmpty(because: "there is no URL to ping");
		_steps.Should().Equal(["validate"], because: "nothing is written for an environment that is not registered");
	}

	[Test]
	[Description("With an application code, the application is read from the environment before the workspace is written, so a failure there leaves nothing behind.")]
	public void Execute_ShouldReadTheApplicationBeforeWriting_WhenAppCodeProvided() {
		// Arrange
		_installedApplication.GetInstalledAppInfo("UsrApp")
			.Returns(_ => throw new InvalidOperationException("no rights to read installed applications"));

		// Act
		int result = _command.Execute(new CreateWorkspaceCommandOptions { Environment = "dev", AppCode = "UsrApp" });

		// Assert
		result.Should().Be(1, because: "a workspace whose application cannot be read is not created");
		_steps.Should().Equal(["validate", "ping"], because: "Create runs only after the application has been read");
	}

	[Test]
	[Description("Without an environment (plain or --empty), create-workspace never pings: it reads nothing from an environment.")]
	public void Execute_ShouldNotPing_WhenNoEnvironmentProvided() {
		// Act
		int result = _command.Execute(new CreateWorkspaceCommandOptions());

		// Assert
		result.Should().Be(0, because: "a workspace without an environment is created locally");
		_applicationPing.ReceivedCalls().Should().BeEmpty(because: "there is no environment to check");
	}

	private IEnumerable<string> ErrorsLogged() =>
		_logger.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(ILogger.WriteError))
			.Select(call => (string)call.GetArguments()[0]);
}
