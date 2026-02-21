using Clio.Command;
using Clio.Common;
using Clio.Workspace;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class CreateWorkspaceCommandDocTests : BaseCommandTests<CreateWorkspaceCommandOptions>
{
	// Intentionally empty: BaseCommandTests validates that the command is documented.
}

[TestFixture]
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
		var workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		var workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		workingDirectoriesProvider.CurrentDirectory.Returns("/tmp/root");
		fileSystem.ExistsDirectory(Arg.Any<string>()).Returns(false);
		var command = new CreateWorkspaceCommand(workspace, logger, installedApplication, fileSystem, workspacePathBuilder, workingDirectoriesProvider);
		var options = new CreateWorkspaceCommandOptions {
			WorkspaceName = "my-workspace",
			Empty = true
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "empty create-workspace should succeed");
		fileSystem.Received(1).CreateDirectoryIfNotExists(Arg.Any<string>());
		workspacePathBuilder.Received(1).RootPath = Arg.Any<string>();
		workspace.Received(1).Create(null, false, false);
		installedApplication.DidNotReceiveWithAnyArgs().GetInstalledAppInfo(default);
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
		var workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		var workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		workingDirectoriesProvider.CurrentDirectory.Returns("/tmp/new-workspace");
		var command = new CreateWorkspaceCommand(workspace, logger, installedApplication, fileSystem, workspacePathBuilder, workingDirectoriesProvider);
		var options = new CreateWorkspaceCommandOptions {
			Environment = "dev",
			Empty = false,
			AppCode = null
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "online create-workspace should succeed");
		workspacePathBuilder.Received(1).RootPath = "/tmp/new-workspace";
		workspace.Received(1).Create("dev", true, false);
		workspace.Received(1).Restore(options);
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
		var workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		var workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		workingDirectoriesProvider.CurrentDirectory.Returns("/tmp/root");
		var command = new CreateWorkspaceCommand(workspace, logger, installedApplication, fileSystem, workspacePathBuilder, workingDirectoriesProvider);
		var options = new CreateWorkspaceCommandOptions {
			WorkspaceName = "my-workspace",
			Empty = false
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, "workspace name without --empty should be rejected");
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
		var fileSystem = Substitute.For<IFileSystem>();
		var workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		var workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		workingDirectoriesProvider.CurrentDirectory.Returns("/tmp/root");
		fileSystem.ExistsDirectory(Arg.Any<string>()).Returns(true);
		fileSystem.IsEmptyDirectory(Arg.Any<string>()).Returns(false);

		var command = new CreateWorkspaceCommand(workspace, logger, installedApplication, fileSystem,
			workspacePathBuilder, workingDirectoriesProvider);
		var options = new CreateWorkspaceCommandOptions {
			WorkspaceName = "my-workspace",
			Empty = true,
			Force = true
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "--force should allow creating workspace in a non-empty destination folder");
		workspace.Received(1).Create(null, false, true);
	}
}
