using System;
using System.Runtime.InteropServices;
using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using Clio.Utilities;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("UnitTests")]
[Author("GitHub Copilot", "copilot@github.com")]
[Description("Tests for OpenAppCommand - verifies opening web applications in browsers across different platforms")]
public class OpenAppCommandTests : BaseCommandTests<OpenAppOptions>{
	#region Fields: Private

	private IApplicationClient _applicationClient;
	private OpenAppCommand _command;
	private EnvironmentSettings _environmentSettings;
	private IProcessExecutor _processExecutor;
	private ISettingsRepository _settingsRepository;
	private IWebBrowser _webBrowser;

	#endregion

	#region Methods: Public

	[Test]
	[Description("Execute should handle environment with trailing slash in URI")]
	public void Execute_ShouldHandleTrailingSlashInUri() {
		// Arrange
		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
			Assert.Ignore("This test is only applicable on Windows");
		}

		OpenAppOptions options = new() { Environment = "test-env" };
		EnvironmentSettings environment = new() {
			Uri = "https://test.creatio.com/",
			Login = "admin",
			Password = "password"
		};

		_settingsRepository.GetEnvironment(options).Returns(environment);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "command should succeed with trailing slash in URI");
		_webBrowser.Received(1).OpenUrl(Arg.Any<string>());
	}

	[Test]
	[Description("Execute should handle valid HTTPS URI correctly")]
	public void Execute_ShouldHandleValidHttpsUri() {
		// Arrange
		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
			Assert.Ignore("This test is only applicable on Windows");
		}

		OpenAppOptions options = new() { Environment = "test-env" };
		EnvironmentSettings environment = new() {
			Uri = "https://secure.creatio.com:443",
			Login = "admin",
			Password = "password"
		};

		_settingsRepository.GetEnvironment(options).Returns(environment);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "command should succeed with valid HTTPS URI");
		_webBrowser.Received(1).OpenUrl(Arg.Any<string>());
	}

	[Test]
	[Description("Execute should handle valid HTTP URI correctly")]
	public void Execute_ShouldHandleValidHttpUri() {
		// Arrange
		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
			Assert.Ignore("This test is only applicable on Windows");
		}

		OpenAppOptions options = new() { Environment = "test-env" };
		EnvironmentSettings environment = new() {
			Uri = "http://localhost:8080",
			Login = "admin",
			Password = "password"
		};

		_settingsRepository.GetEnvironment(options).Returns(environment);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "command should succeed with valid HTTP URI");
		_webBrowser.Received(1).OpenUrl(Arg.Any<string>());
	}

	[Test]
	[Description("Execute should not call browser methods when URI is invalid")]
	public void Execute_ShouldNotCallBrowserMethods_WhenUriIsInvalid() {
		// Arrange
		OpenAppOptions options = new() { Environment = "test-env" };
		EnvironmentSettings environment = new() {
			Uri = "invalid-uri-format",
			Login = "admin",
			Password = "password"
		};

		_settingsRepository.GetEnvironment(options).Returns(environment);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "command should fail when URI format is invalid");
		_webBrowser.DidNotReceive().OpenUrl(Arg.Any<string>());
		_processExecutor.DidNotReceive().Execute(
			Arg.Any<string>(),
			Arg.Any<string>(),
			Arg.Any<bool>(),
			Arg.Any<string>(),
			Arg.Any<bool>(),
			Arg.Any<bool>());
	}

	[Test]
	[Description("Execute should return error when environment URI has invalid format")]
	[TestCase("not-a-valid-uri")]
	[TestCase("://missing-scheme.com")]
	[TestCase("http://")]
	public void Execute_ShouldReturnError_WhenEnvironmentUriHasInvalidFormat(string invalidUri) {
		// Arrange
		OpenAppOptions options = new() { Environment = "test-env" };
		EnvironmentSettings environment = new() {
			Uri = invalidUri,
			Login = "admin",
			Password = "password"
		};

		_settingsRepository.GetEnvironment(options).Returns(environment);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, $"command should fail when environment URI '{invalidUri}' has invalid format");
		_webBrowser.DidNotReceiveWithAnyArgs().OpenUrl(default);
		_processExecutor.DidNotReceiveWithAnyArgs().Execute(default, default, default);
	}

	[Test]
	[Description("Execute should return error when environment URI is empty")]
	public void Execute_ShouldReturnError_WhenEnvironmentUriIsEmpty() {
		// Arrange
		OpenAppOptions options = new() { Environment = "test-env" };
		EnvironmentSettings environment = new() {
			Uri = string.Empty,
			Login = "admin",
			Password = "password"
		};

		_settingsRepository.GetEnvironment(options).Returns(environment);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "command should fail when environment URI is empty");
		_webBrowser.DidNotReceiveWithAnyArgs().OpenUrl(default);
		_processExecutor.DidNotReceiveWithAnyArgs().Execute(default, default, default);
	}

	[Test]
	[Description("Execute should return error when environment URI is null")]
	public void Execute_ShouldReturnError_WhenEnvironmentUriIsNull() {
		// Arrange
		OpenAppOptions options = new() { Environment = "test-env" };
		EnvironmentSettings environment = new() {
			Uri = null,
			Login = "admin",
			Password = "password"
		};

		_settingsRepository.GetEnvironment(options).Returns(environment);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "command should fail when environment URI is null");
		_webBrowser.DidNotReceiveWithAnyArgs().OpenUrl(default);
		_processExecutor.DidNotReceiveWithAnyArgs().Execute(default, default, default);
	}

	[Test]
	[Description("Execute should return error and log message when exception occurs")]
	public void Execute_ShouldReturnError_WhenExceptionOccurs() {
		// Arrange
		OpenAppOptions options = new() { Environment = "test-env" };
		_settingsRepository.GetEnvironment(options).Returns(x => throw new Exception("Test exception"));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "command should fail when exception occurs");
	}

	[Test]
	[Description("Execute should return success when environment has valid URI and opens browser on macOS")]
	public void Execute_ShouldReturnSuccess_WhenEnvironmentHasValidUri_OnMacOS() {
		// Arrange
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
			Assert.Ignore("This test is only applicable on macOS");
		}

		OpenAppOptions options = new() { Environment = "test-env" };
		EnvironmentSettings environment = new() {
			Uri = "https://test.creatio.com",
			Login = "admin",
			Password = "password"
		};

		_settingsRepository.GetEnvironment(options).Returns(environment);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "command should succeed when environment has valid URI on macOS");
		_processExecutor.Received(1).Execute(
			"open",
			Arg.Is<string>(url => url.Contains("test.creatio.com")),
			false);
	}

	[Test]
	[Description("Execute should return success when environment has valid URI and opens browser on Windows")]
	public void Execute_ShouldReturnSuccess_WhenEnvironmentHasValidUri_OnWindows() {
		// Arrange
		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
			Assert.Ignore("This test is only applicable on Windows");
		}

		OpenAppOptions options = new() { Environment = "test-env" };
		EnvironmentSettings environment = new() {
			Uri = "https://test.creatio.com",
			Login = "admin",
			Password = "password"
		};

		_settingsRepository.GetEnvironment(options).Returns(environment);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "command should succeed when environment has valid URI");
		_webBrowser.Received(1).OpenUrl(Arg.Is<string>(url => url.Contains("test.creatio.com")));
	}

	[Test]
	[Description("Execute should use SimpleloginUri property from environment settings")]
	public void Execute_ShouldUseSimpleloginUri() {
		// Arrange
		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
			Assert.Ignore("This test is only applicable on Windows");
		}

		OpenAppOptions options = new() { Environment = "test-env" };
		EnvironmentSettings environment = new() {
			Uri = "https://test.creatio.com",
			Login = "testuser",
			Password = "testpass"
		};

		_settingsRepository.GetEnvironment(options).Returns(environment);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "command should succeed");
		_webBrowser.Received(1).OpenUrl(environment.SimpleloginUri);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_applicationClient = Substitute.For<IApplicationClient>();
		_environmentSettings = new EnvironmentSettings();
		_webBrowser = Substitute.For<IWebBrowser>();
		_processExecutor = Substitute.For<IProcessExecutor>();
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_command = new OpenAppCommand(_applicationClient, _environmentSettings, _webBrowser, _processExecutor,
			_settingsRepository);
	}

	#endregion
}
