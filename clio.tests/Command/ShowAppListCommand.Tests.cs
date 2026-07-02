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
[Property("Module", "Command")]
public class ShowAppListCommandTestCase : BaseCommandTests<AppListOptions>{
	#region Fields: Private

	private ShowAppListCommand _command;
	private ILogger _loggerMock;
	private ISettingsRepository _settingsRepository;
	private IJsonResponseFormater _jsonResponseFormater;

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


	[Test]
	[Category("Unit")]
	[Description("Should emit the unified envelope (via FormatEnvelope) through the logger when --json is set for a single environment")]
	public void Execute_ShouldEmitEnvelope_WhenJsonForSingleEnvironment() {
		// Arrange
		AppListOptions options = new() { Name = "TestEnvironment", Json = true };
		var testEnv = new EnvironmentSettings { Uri = "https://test", Login = "admin", Password = "secret" };
		_settingsRepository.FindEnvironment(options.Name).Returns(testEnv);
		_settingsRepository.GetActualEnvironmentName(options.Name).Returns("TestEnvironment");

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "a found environment is emitted successfully");
		_jsonResponseFormater.Received(1).FormatEnvelope("list-environments", Arg.Any<ShowWebAppSettingsResult>());
		_loggerMock.Received(1).WriteLine(Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("Should emit an error envelope with environment-not-found when --json is set and the environment is missing")]
	public void Execute_ShouldEmitErrorEnvelope_WhenJsonAndEnvironmentNotFound() {
		// Arrange
		AppListOptions options = new() { Name = "Missing", Json = true };
		_settingsRepository.FindEnvironment(options.Name).Returns((EnvironmentSettings)null);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "a missing environment is a failure");
		_jsonResponseFormater.Received(1).FormatEnvelope("list-environments",
			Clio.Common.CommandErrorCodes.EnvironmentNotFound, Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("Should mask password and client secret in the --json envelope data (secret scrub, AC E1)")]
	public void Execute_ShouldMaskSensitiveData_WhenJsonForSingleEnvironment() {
		// Arrange
		AppListOptions options = new() { Name = "TestEnvironment", Json = true };
		var testEnv = new EnvironmentSettings {
			Uri = "https://test", Login = "admin", Password = "secret", ClientSecret = "topsecret"
		};
		_settingsRepository.FindEnvironment(options.Name).Returns(testEnv);
		_settingsRepository.GetActualEnvironmentName(options.Name).Returns("TestEnvironment");
		ShowWebAppSettingsResult captured = null;
		_jsonResponseFormater
			.FormatEnvelope("list-environments", Arg.Do<ShowWebAppSettingsResult>(data => captured = data))
			.Returns("{}");

		// Act
		_command.Execute(options);

		// Assert
		captured.Should().NotBeNull(because: "the envelope data must be built for a found environment");
		captured!.Password.Should().Be("****", because: "passwords must never leak into machine output (E1)");
		captured.ClientSecret.Should().Be("****", because: "client secrets must never leak into machine output (E1)");
	}

	[Test]
	[Category("Unit")]
	[Description("Should render exact raw field lines with masked password and NO envelope when --format raw (non-JSON) — AC#3 golden for the list-environments legacy path")]
	public void Execute_ShouldRenderRawFieldsMasked_WhenFormatRawNonJson() {
		// Arrange
		AppListOptions options = new() { Name = "prod", Format = "raw" };
		var environment = new EnvironmentSettings { Uri = "https://prod", Login = "admin", Password = "secret" };
		_settingsRepository.FindEnvironment("prod").Returns(environment);
		_settingsRepository.GetActualEnvironmentName("prod").Returns("prod");

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "raw format for a found environment succeeds");
		Received.InOrder(() => {
			_loggerMock.WriteLine("Name: prod");
			_loggerMock.WriteLine("Uri: https://prod");
		});
		_loggerMock.Received(1).WriteLine("Password: ****");
		_jsonResponseFormater.DidNotReceive().FormatEnvelope("list-environments", Arg.Any<ShowWebAppSettingsResult>());
	}

	[Test]
	[Category("Unit")]
	[Description("Should NOT emit the unified envelope when --json is not set — legacy --format behavior is preserved")]
	public void Execute_ShouldNotEmitEnvelope_WhenJsonNotSet() {
		// Arrange
		AppListOptions options = new() { Name = "TestEnvironment", ShowShort = true };
		var testEnv = new EnvironmentSettings { Uri = "https://test" };
		_settingsRepository.FindEnvironment(options.Name).Returns(testEnv);

		// Act
		_command.Execute(options);

		// Assert
		_jsonResponseFormater.DidNotReceive().FormatEnvelope(Arg.Any<string>(), Arg.Any<ShowWebAppSettingsResult>());
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_loggerMock = Substitute.For<ILogger>();
		_jsonResponseFormater = Substitute.For<IJsonResponseFormater>();
		_command = new ShowAppListCommand(_settingsRepository, _loggerMock, _jsonResponseFormater);
	}

	#endregion
}
