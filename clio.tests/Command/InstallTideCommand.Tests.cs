#region

using Clio.Command;
using Clio.Common;
using Clio.Package;
using Clio.Project.NuGet;
using Clio.WebApplication;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

#endregion

namespace Clio.Tests.Command;

#region Class: InstallTideCommandTests

[TestFixture]
public class InstallTideCommandTests : BaseCommandTests<InstallTideCommandOptions>{
	#region Methods: Public

	[Test]
	[Description("Should fail if gate install fails and not proceed to tide")]
	public void Execute_GateInstallFails_ShouldNotInstallTide() {
		// Arrange
		IInstallNugetPackage installNugetPackage = Substitute.For<IInstallNugetPackage>();
		InstallNugetPackageCommand installNugetPackageCommand
			= Substitute.For<InstallNugetPackageCommand>(installNugetPackage);

		EnvironmentSettings environmentSettings = Substitute.For<EnvironmentSettings>();
		IPackageInstaller packageInstaller = Substitute.For<IPackageInstaller>();
		IMarketplace marketplace = Substitute.For<IMarketplace>();
		ICompileConfigurationCommand compileConfigurationCommand = Substitute.For<ICompileConfigurationCommand>();
		IApplication application = Substitute.For<IApplication>();
		ILogger logger = Substitute.For<ILogger>();
		InstallGatePkgCommand installGatePkgCommand = Substitute.For<InstallGatePkgCommand>(
			environmentSettings, packageInstaller, marketplace, compileConfigurationCommand, application, logger);

		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		HealthCheckCommand healthCheckCommand
			= Substitute.For<HealthCheckCommand>(applicationClient, environmentSettings);

		InstallTideCommandOptions options = new();

		// Setup gate install to fail
		installGatePkgCommand
			.Execute(Arg.Any<PushPkgOptions>())
			.Returns(1);

		InstallTideCommand command = new(
			installNugetPackageCommand,
			installGatePkgCommand,
			healthCheckCommand,
			logger
		);

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, "should fail if gate install fails");
		installNugetPackageCommand.DidNotReceive().Execute(Arg.Any<InstallNugetPkgOptions>());
	}

	[Test]
	[Description("Should fail if server does not become ready after gate install")]
	public void Execute_ServerNotReady_ShouldNotInstallTide() {
		// Arrange
		IInstallNugetPackage installNugetPackage = Substitute.For<IInstallNugetPackage>();
		InstallNugetPackageCommand installNugetPackageCommand
			= Substitute.For<InstallNugetPackageCommand>(installNugetPackage);

		EnvironmentSettings environmentSettings = Substitute.For<EnvironmentSettings>();
		IPackageInstaller packageInstaller = Substitute.For<IPackageInstaller>();
		IMarketplace marketplace = Substitute.For<IMarketplace>();
		ICompileConfigurationCommand compileConfigurationCommand = Substitute.For<ICompileConfigurationCommand>();
		IApplication application = Substitute.For<IApplication>();
		ILogger logger = Substitute.For<ILogger>();
		InstallGatePkgCommand installGatePkgCommand = Substitute.For<InstallGatePkgCommand>(
			environmentSettings, packageInstaller, marketplace, compileConfigurationCommand, application, logger);

		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		HealthCheckCommand healthCheckCommand
			= Substitute.For<HealthCheckCommand>(applicationClient, environmentSettings);

		InstallTideCommandOptions options = new();

		// Setup gate install to succeed but health check to fail
		installGatePkgCommand
			.Execute(Arg.Any<PushPkgOptions>())
			.Returns(0);

		healthCheckCommand
			.Execute(Arg.Any<HealthCheckOptions>())
			.Returns(1);

		InstallTideCommand command = new(
			installNugetPackageCommand,
			installGatePkgCommand,
			healthCheckCommand,
			logger
		);

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, "should fail if server is not ready after gate install");
		installNugetPackageCommand.DidNotReceive().Execute(Arg.Any<InstallNugetPkgOptions>());
	}

	[Test]
	[Description("Should wait for server to become ready after gate install before installing tide")]
	public void Execute_WaitsForServerReady_ThenInstallsTide() {
		// Arrange
		IInstallNugetPackage installNugetPackage = Substitute.For<IInstallNugetPackage>();
		InstallNugetPackageCommand installNugetPackageCommand
			= Substitute.For<InstallNugetPackageCommand>(installNugetPackage);

		EnvironmentSettings environmentSettings = Substitute.For<EnvironmentSettings>();
		IPackageInstaller packageInstaller = Substitute.For<IPackageInstaller>();
		IMarketplace marketplace = Substitute.For<IMarketplace>();
		ICompileConfigurationCommand compileConfigurationCommand = Substitute.For<ICompileConfigurationCommand>();
		IApplication application = Substitute.For<IApplication>();
		ILogger logger = Substitute.For<ILogger>();
		InstallGatePkgCommand installGatePkgCommand = Substitute.For<InstallGatePkgCommand>(
			environmentSettings, packageInstaller, marketplace, compileConfigurationCommand, application, logger);

		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		HealthCheckCommand healthCheckCommand
			= Substitute.For<HealthCheckCommand>(applicationClient, environmentSettings);

		InstallTideCommandOptions options = new();

		// Setup the command mocks to simulate successful execution
		installGatePkgCommand.Execute(Arg.Any<PushPkgOptions>()).Returns(0);
		healthCheckCommand.Execute(Arg.Any<HealthCheckOptions>()).Returns(0);
		installNugetPackageCommand.Execute(Arg.Any<InstallNugetPkgOptions>()).Returns(0);

		InstallTideCommand command = new(
			installNugetPackageCommand,
			installGatePkgCommand,
			healthCheckCommand,
			logger
		);

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "should succeed when gate and tide install and server is ready");
		installGatePkgCommand.Received(1).Execute(Arg.Any<PushPkgOptions>());
		healthCheckCommand.Received().Execute(Arg.Any<HealthCheckOptions>());
		installNugetPackageCommand.Received(1).Execute(Arg.Any<InstallNugetPkgOptions>());
	}

	#endregion
}

#endregion
