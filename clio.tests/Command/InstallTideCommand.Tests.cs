using System;
using NSubstitute;
using NUnit.Framework;
using FluentAssertions;
using Clio.Command;

namespace Clio.Tests.Command
{
    [TestFixture]
    public class InstallTideCommandTests : BaseCommandTests<InstallTideCommandOptions>
    {
        [Test]
        [Description("Should wait for server to become ready after gate install before installing tide")] 
        public void Execute_WaitsForServerReady_ThenInstallsTide()
        {
            // Arrange
            var installNugetPackageCommand = Substitute.For<InstallNugetPackageCommand>();
            var installGatePkgCommand = Substitute.For<InstallGatePkgCommand>();
            var healthCheckCommand = Substitute.For<HealthCheckCommand>();
            var options = new InstallTideCommandOptions();

            installGatePkgCommand.Execute(Arg.Any<InstallGateOptions>()).Returns(0);
            healthCheckCommand.Execute(Arg.Any<HealthCheckOptions>()).Returns(0);
            installNugetPackageCommand.Execute(Arg.Any<InstallNugetPkgOptions>()).Returns(0);

            var command = new InstallTideCommand(
                installNugetPackageCommand,
                installGatePkgCommand,
                healthCheckCommand
            );

            // Act
            var result = command.Execute(options);

            // Assert
            result.Should().Be(0, "should succeed when gate and tide install and server is ready");
            Received.InOrder(() =>
            {
                installGatePkgCommand.Execute(Arg.Any<InstallGateOptions>());
                healthCheckCommand.Execute(Arg.Any<HealthCheckOptions>());
                installNugetPackageCommand.Execute(Arg.Any<InstallNugetPkgOptions>());
            });
        }

        [Test]
        [Description("Should fail if gate install fails and not proceed to tide")] 
        public void Execute_GateInstallFails_ShouldNotInstallTide()
        {
            // Arrange
            var installNugetPackageCommand = Substitute.For<InstallNugetPackageCommand>();
            var installGatePkgCommand = Substitute.For<InstallGatePkgCommand>();
            var healthCheckCommand = Substitute.For<HealthCheckCommand>();
            var options = new InstallTideCommandOptions();

            installGatePkgCommand.Execute(Arg.Any<InstallGateOptions>()).Returns(1);

            var command = new InstallTideCommand(
                installNugetPackageCommand,
                installGatePkgCommand,
                healthCheckCommand
            );

            // Act
            var result = command.Execute(options);

            // Assert
            result.Should().Be(1, "should fail if gate install fails");
            installNugetPackageCommand.DidNotReceive().Execute(Arg.Any<InstallNugetPkgOptions>());
        }

        [Test]
        [Description("Should fail if server does not become ready after gate install")] 
        public void Execute_ServerNotReady_ShouldNotInstallTide()
        {
            // Arrange
            var installNugetPackageCommand = Substitute.For<InstallNugetPackageCommand>();
            var installGatePkgCommand = Substitute.For<InstallGatePkgCommand>();
            var healthCheckCommand = Substitute.For<HealthCheckCommand>();
            var options = new InstallTideCommandOptions();

            installGatePkgCommand.Execute(Arg.Any<InstallGateOptions>()).Returns(0);
            healthCheckCommand.Execute(Arg.Any<HealthCheckOptions>()).Returns(1);

            var command = new InstallTideCommand(
                installNugetPackageCommand,
                installGatePkgCommand,
                healthCheckCommand
            );

            // Act
            var result = command.Execute(options);

            // Assert
            result.Should().Be(1, "should fail if server is not ready after gate install");
            installNugetPackageCommand.DidNotReceive().Execute(Arg.Any<InstallNugetPkgOptions>());
        }
    }
}
