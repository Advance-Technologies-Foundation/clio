using System;
using System.IO;
using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class ShowAppListCommandTestCase : BaseCommandTests<AppListOptions>{
	#region Fields: Private

	private ShowAppListCommand _command;
	private ILogger _loggerMock;
	private ISettingsRepository _settingsRepository;

	#endregion

	#region Methods: Public

	[Test]
	[Category("Unit")]
	[Description("Should maintain backward compatibility with Name and ShowShort options")]
	public void Execute_BackwardCompatibility_NameAndShowShort() {
		// Arrange
		AppListOptions options = new() {
			Name = "TestEnvironment",
			ShowShort = true
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because backward compatibility should be maintained");
		_settingsRepository.Received(1).ShowSettingsTo(Arg.Any<TextWriter>(), options.Name, true);
	}

	[Test]
	[Category("Unit")]
	[Description("Should return error when environment not found")]
	public void Execute_EnvironmentNotFound_ReturnsError() {
		// Arrange
		AppListOptions options = new() {
			Name = "NonExistentEnvironment",
			ShowShort = false
		};
		_settingsRepository.FindEnvironment(options.Name).Returns((EnvironmentSettings)null);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "because the environment was not found");
	}

	[Test]
	[Category("Unit")]
	[Description("Should support json format option")]
	public void Execute_WithJsonFormat_ReturnsSuccess() {
		// Arrange
		EnvironmentSettings testEnv = new() {
			Uri = "http://test.com",
			Login = "testuser",
			Password = "testpass"
		};
		AppListOptions options = new() {
			Name = "TestEnvironment",
			Format = "json"
		};
		_settingsRepository.FindEnvironment(options.Name).Returns(testEnv);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because json format should be supported");
	}

	[Test]
	[Category("Unit")]
	[Description("Should support raw flag as shorthand for --format raw")]
	public void Execute_WithRawFlag_UsesRawFormat() {
		// Arrange
		EnvironmentSettings testEnv = new() {
			Uri = "http://test.com",
			Login = "testuser",
			Password = "testpass"
		};
		AppListOptions options = new() {
			Name = "TestEnvironment",
			Format = "json",
			Raw = true
		};
		_settingsRepository.FindEnvironment(options.Name).Returns(testEnv);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because raw flag should override format option");
	}

	[Test]
	[Category("Unit")]
	[Description("Should support raw format option")]
	public void Execute_WithRawFormat_ReturnsSuccess() {
		// Arrange
		EnvironmentSettings testEnv = new() {
			Uri = "http://test.com",
			Login = "testuser",
			Password = "testpass"
		};
		AppListOptions options = new() {
			Name = "TestEnvironment",
			Format = "raw"
		};
		_settingsRepository.FindEnvironment(options.Name).Returns(testEnv);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because raw format should be supported");
	}

	[Test]
	[Category("Unit")]
	[Description("Should call ShowSettingsTo with short flag when ShowShort option is set")]
	public void Execute_WithShowShortFlag_CallsShowSettingsToWithShort() {
		// Arrange
		AppListOptions options = new() {
			Name = "TestEnvironment",
			ShowShort = true
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because the command should execute successfully");
		_settingsRepository.Received(1).ShowSettingsTo(Console.Out, options.Name, true);
	}

	[Test]
	[Category("Unit")]
	[Description("Should support table format option")]
	public void Execute_WithTableFormat_ReturnsSuccess() {
		// Arrange
		EnvironmentSettings testEnv = new() {
			Uri = "http://test.com",
			Login = "testuser",
			Password = "testpass"
		};
		AppListOptions options = new() {
			Name = "TestEnvironment",
			Format = "table"
		};
		_settingsRepository.FindEnvironment(options.Name).Returns(testEnv);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because table format should be supported");
	}

	[Test]
	[Category("Unit")]
	[Description("Should reject unknown format")]
	public void Execute_WithUnknownFormat_ReturnsError() {
		// Arrange
		EnvironmentSettings testEnv = new() {
			Uri = "http://test.com",
			Login = "testuser",
			Password = "testpass"
		};
		AppListOptions options = new() {
			Name = "TestEnvironment",
			Format = "unknown"
		};
		_settingsRepository.FindEnvironment(options.Name).Returns(testEnv);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "because unknown format should be rejected");
	}


	[SetUp]
	public override void Setup() {
		base.Setup();
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_loggerMock = Substitute.For<ILogger>();
		_command = new ShowAppListCommand(_settingsRepository, _loggerMock);
	}

	#endregion
}
