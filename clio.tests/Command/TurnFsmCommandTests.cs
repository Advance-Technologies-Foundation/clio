using System;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Clio.Command;
using Clio.Common;

namespace Clio.Tests.Command
{
    [TestFixture]
    public class TurnFsmCommandTests : BaseCommandTests<TurnFsmCommand>
    {
        private SetFsmConfigCommand _setFsmConfigCommand;
        private LoadPackagesToFileSystemCommand _loadPackagesToFileSystemCommand;
        private LoadPackagesToDbCommand _loadPackagesToDbCommand;
        private IApplicationClient _applicationClient;
        private EnvironmentSettings _environmentSettings;
        private TurnFsmCommand _command;

        [SetUp]
        public void SetUp()
        {
            _setFsmConfigCommand = Substitute.For<SetFsmConfigCommand>();
            _loadPackagesToFileSystemCommand = Substitute.For<LoadPackagesToFileSystemCommand>();
            _loadPackagesToDbCommand = Substitute.For<LoadPackagesToDbCommand>();
            _applicationClient = Substitute.For<IApplicationClient>();
            _environmentSettings = Substitute.For<EnvironmentSettings>();

            _command = new TurnFsmCommand(
                _setFsmConfigCommand,
                _loadPackagesToFileSystemCommand,
                _loadPackagesToDbCommand,
                _applicationClient,
                _environmentSettings
            );
        }

        [Test]
        public void Execute_ShouldTurnOnFsm_WhenOptionsIsFsmOn()
        {
            // Arrange
            var options = new TurnFsmCommandOptions { IsFsm = "on", Environment = "TestEnv" };
            _setFsmConfigCommand.Execute(options).Returns(0);
            _loadPackagesToFileSystemCommand.Execute(options).Returns(0);
            _environmentSettings.IsNetCore.Returns(true);

            // Act
            var result = _command.Execute(options);

            // Assert
            result.Should().Be(0, "FSM should be turned on successfully");
            _setFsmConfigCommand.Received(1).Execute(options);
            _loadPackagesToFileSystemCommand.Received(1).Execute(options);
            _applicationClient.Received(1).Login();
        }

        [Test]
        public void Execute_ShouldTurnOffFsm_WhenOptionsIsFsmOff()
        {
            // Arrange
            var options = new TurnFsmCommandOptions { IsFsm = "off", Environment = "TestEnv" };
            _loadPackagesToDbCommand.Execute(options).Returns(0);
            _setFsmConfigCommand.Execute(options).Returns(0);

            // Act
            var result = _command.Execute(options);

            // Assert
            result.Should().Be(0, "FSM should be turned off successfully");
            _loadPackagesToDbCommand.Received(1).Execute(options);
            _setFsmConfigCommand.Received(1).Execute(options);
        }

        [Test]
        public void Execute_ShouldReturnError_WhenSetFsmConfigFails()
        {
            // Arrange
            var options = new TurnFsmCommandOptions { IsFsm = "on", Environment = "TestEnv" };
            _setFsmConfigCommand.Execute(options).Returns(1);

            // Act
            var result = _command.Execute(options);

            // Assert
            result.Should().Be(1, "FSM configuration failed");
            _setFsmConfigCommand.Received(1).Execute(options);
            _loadPackagesToFileSystemCommand.DidNotReceive().Execute(options);
        }

        [Test]
        public void Execute_ShouldReturnError_WhenLoadPackagesToFileSystemFails()
        {
            // Arrange
            var options = new TurnFsmCommandOptions { IsFsm = "on", Environment = "TestEnv" };
            _setFsmConfigCommand.Execute(options).Returns(0);
            _loadPackagesToFileSystemCommand.Execute(options).Returns(1);

            // Act
            var result = _command.Execute(options);

            // Assert
            result.Should().Be(1, "Loading packages to file system failed");
            _setFsmConfigCommand.Received(1).Execute(options);
            _loadPackagesToFileSystemCommand.Received(1).Execute(options);
        }
    }
}
