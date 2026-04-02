using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using System.Collections.Generic;

namespace Clio.Tests.Command;

[TestFixture]
public class InitWorkspaceCommandDocTests : BaseCommandTests<InitWorkspaceCommandOptions> {
	// Intentionally empty: BaseCommandTests validates that the command is documented.
}

[TestFixture]
public class InitWorkspaceCommandTests {

	[Test]
	[Description("When no environment is provided, init-workspace should initialize the current directory without restoring packages from a remote environment.")]
	public void Execute_ShouldInitializeCurrentDirectory_WhenEnvironmentIsNotProvided() {
		// Arrange
		IWorkspace workspace = Substitute.For<IWorkspace>();
		ILogger logger = Substitute.For<ILogger>();
		IInstalledApplication installedApplication = Substitute.For<IInstalledApplication>();
		IWorkspacePathBuilder workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		workingDirectoriesProvider.CurrentDirectory.Returns(@"C:\repo");
		InitWorkspaceCommand command = new(workspace, logger, installedApplication, workspacePathBuilder, workingDirectoriesProvider);
		InitWorkspaceCommandOptions options = new();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, because: "init-workspace should support initializing an existing local directory without an environment");
		workspacePathBuilder.Received(1).RootPath = @"C:\repo";
		workspace.Received(1).Initialize(null, false);
		workspace.DidNotReceive().Restore(Arg.Any<WorkspaceOptions>());
		installedApplication.DidNotReceiveWithAnyArgs().GetInstalledAppInfo(default!);
		logger.Received(1).WriteInfo(@"Workspace initialized at: C:\repo");
	}

	[Test]
	[Description("When environment is provided without AppCode, init-workspace should initialize the current directory and then restore editable packages from that environment.")]
	public void Execute_ShouldInitializeAndRestore_WhenEnvironmentProvided() {
		// Arrange
		IWorkspace workspace = Substitute.For<IWorkspace>();
		ILogger logger = Substitute.For<ILogger>();
		IInstalledApplication installedApplication = Substitute.For<IInstalledApplication>();
		IWorkspacePathBuilder workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		workingDirectoriesProvider.CurrentDirectory.Returns(@"C:\repo");
		InitWorkspaceCommand command = new(workspace, logger, installedApplication, workspacePathBuilder, workingDirectoriesProvider);
		InitWorkspaceCommandOptions options = new() {
			Environment = "dev"
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, because: "init-workspace should preserve the environment-backed restore flow");
		workspace.Received(1).Initialize("dev", true);
		workspace.Received(1).Restore(options);
	}

	[Test]
	[Description("When AppCode is provided, init-workspace should switch to the application-code path and query installed application metadata after safe initialization.")]
	public void Execute_ShouldQueryInstalledApplication_WhenAppCodeProvided() {
		// Arrange
		IWorkspace workspace = Substitute.For<IWorkspace>();
		ILogger logger = Substitute.For<ILogger>();
		IInstalledApplication installedApplication = Substitute.For<IInstalledApplication>();
		IWorkspacePathBuilder workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		workingDirectoriesProvider.CurrentDirectory.Returns(@"C:\repo");
		InitWorkspaceCommand command = new(workspace, logger, installedApplication, workspacePathBuilder, workingDirectoriesProvider);
		InitWorkspaceCommandOptions options = new() {
			Environment = "dev",
			AppCode = "app-code"
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, because: "init-workspace should still support app-code-based package enrollment");
		workspace.Received(1).Initialize("dev", false);
		installedApplication.Received(1).GetInstalledAppInfo("app-code");
		workspace.Received(1).Restore(options);
	}
}
