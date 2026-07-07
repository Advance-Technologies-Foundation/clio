using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Common;
using Clio.Common.BrowserSession;
using Clio.UserEnvironment;
using Clio.Utilities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[NonParallelizable]
[Category("Unit")]
[Author("GitHub Copilot", "copilot@github.com")]
[Description("Tests for OpenAppCommand - verifies opening web applications in browsers across different platforms")]
[Property("Module", "Command")]
public class OpenAppCommandTests : BaseCommandTests<OpenAppOptions>{
	#region Fields: Private

	private IApplicationClient _applicationClient;
	private OpenAppCommand _command;
	private EnvironmentSettings _environmentSettings;
	private IProcessExecutor _processExecutor;
	private ISettingsRepository _settingsRepository;
	private IWebBrowser _webBrowser;
	private ILogger _logger;
	private IBrowserSessionService _browserSessionService;
	private IAuthenticatedBrowserLauncher _authenticatedBrowserLauncher;

	#endregion

	#region Methods: Public

	[Test]
	[Description("EnvironmentName positional value should set Environment property so 'clio open myEnv' works")]
	public void EnvironmentName_ShouldSetEnvironmentProperty() {
		// Arrange
		OpenAppOptions options = new() { EnvironmentName = "my-dev-env" };

		// Act & Assert
		options.Environment.Should().Be("my-dev-env",
			"because positional EnvironmentName should map to Environment property");
	}

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
	[Description("Execute should log only the exception message without stack trace in normal mode")]
	public void Execute_ShouldLogMessageOnly_WhenExceptionOccurs_InNormalMode() {
		bool originalDebugMode = Program.IsDebugMode;
		Program.IsDebugMode = false;
		ILogger mockLogger = Substitute.For<ILogger>();
		_command.Logger = mockLogger;
		OpenAppOptions options = new() { Environment = "test-env" };
		try {
			_settingsRepository.GetEnvironment(options).Returns(_ => throw new Exception("Site unavailable"));

			_command.Execute(options);

			mockLogger.Received(1).WriteError("Site unavailable");
			mockLogger.DidNotReceive().WriteError(Arg.Is<string>(s => s.Contains("   at ")));
		} finally {
			Program.IsDebugMode = originalDebugMode;
		}
	}

	[Test]
	[Description("Execute should log full stack trace when exception occurs in debug mode")]
	public void Execute_ShouldLogFullStackTrace_WhenExceptionOccurs_InDebugMode() {
		bool originalDebugMode = Program.IsDebugMode;
		Program.IsDebugMode = true;
		ILogger mockLogger = Substitute.For<ILogger>();
		_command.Logger = mockLogger;
		OpenAppOptions options = new() { Environment = "test-env" };
		try {
			_settingsRepository.GetEnvironment(options).Returns(_ => throw new Exception("Site unavailable"));

			_command.Execute(options);

			mockLogger.Received(1).WriteError(Arg.Is<string>(s => s.Contains("   at ")));
		} finally {
			Program.IsDebugMode = originalDebugMode;
		}
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
		_processExecutor.Received(1).FireAndForgetAsync(
			Arg.Is<ProcessExecutionOptions>(execution =>
				execution.Program == "open" &&
				execution.Arguments.Contains("test.creatio.com")));
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
		_webBrowser.Received(1).OpenUrl(environment.Uri);
	}

	[Test]
	[Description("Execute without --authenticated leaves existing behavior unchanged: the browser-session service is never consulted (AC-02).")]
	public void Execute_ShouldNotCallBrowserSession_WhenAuthenticatedFlagAbsent() {
		// Arrange
		OpenAppOptions options = new() { Environment = "test-env", Authenticated = false };
		EnvironmentSettings environment = new() {
			Uri = "https://test.creatio.com", Login = "admin", Password = "password"
		};
		_settingsRepository.GetEnvironment(options).Returns(environment);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because the unauthenticated path opens the browser as before");
		_ = _browserSessionService.DidNotReceiveWithAnyArgs().GetSessionPathAsync(default, default, default, default);
		_ = _authenticatedBrowserLauncher.DidNotReceiveWithAnyArgs().LaunchAsync(default, default, default);
	}

	[Test]
	[Description("Execute with --authenticated obtains a session and injects it before launch: GetSessionPathAsync is called, then LaunchAsync, and the plain browser open is skipped (AC-01).")]
	public void Execute_ShouldCallGetSessionPathAsync_WhenAuthenticatedFlagIsSet() {
		// Arrange
		OpenAppOptions options = new() { Environment = "test-env", Authenticated = true };
		EnvironmentSettings environment = new() {
			Uri = "https://test.creatio.com", Login = "admin", Password = "password"
		};
		_settingsRepository.GetEnvironment(options).Returns(environment);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because a session was obtained and injected successfully");
		Received.InOrder(() => {
			_browserSessionService.GetSessionPathAsync(environment, Arg.Any<string>(), Arg.Any<bool>(),
				Arg.Any<CancellationToken>());
			_authenticatedBrowserLauncher.LaunchAsync(environment, Arg.Any<string>(), Arg.Any<CancellationToken>());
		});
		_webBrowser.DidNotReceiveWithAnyArgs().OpenUrl(default);
	}

	[Test]
	[Description("Execute with --authenticated does not launch the browser when session retrieval fails: an auth exception is reported and LaunchAsync is never called (AC-05).")]
	public void Execute_ShouldNotLaunchChromium_WhenGetSessionThrows() {
		// Arrange
		OpenAppOptions options = new() { Environment = "test-env", Authenticated = true };
		EnvironmentSettings environment = new() {
			Uri = "https://test.creatio.com", Login = "admin", Password = "password"
		};
		_settingsRepository.GetEnvironment(options).Returns(environment);
		_browserSessionService.GetSessionPathAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<string>(),
				Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromException<string>(
				CreatioAuthenticationException.InvalidCredentials("https://test.creatio.com")));
		ILogger mockLogger = Substitute.For<ILogger>();
		_command.Logger = mockLogger;

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "because authentication failed before any browser could be launched");
		_ = _authenticatedBrowserLauncher.DidNotReceiveWithAnyArgs().LaunchAsync(default, default, default);
		mockLogger.ReceivedWithAnyArgs(1).WriteError(default);
	}

	[Test]
	[Description("Execute with --authenticated exits non-zero with an actionable error when no Chromium-based browser is found, instead of silently falling back (AC-04).")]
	public void Execute_ShouldReturnError_WhenChromiumNotFound() {
		// Arrange
		OpenAppOptions options = new() { Environment = "test-env", Authenticated = true };
		EnvironmentSettings environment = new() {
			Uri = "https://test.creatio.com", Login = "admin", Password = "password"
		};
		_settingsRepository.GetEnvironment(options).Returns(environment);
		_authenticatedBrowserLauncher.LaunchAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<string>(),
				Arg.Any<CancellationToken>())
			.Returns(Task.FromException(new ChromiumNotFoundException(
				"Error: Chromium binary not found — ensure a Chromium-based browser is installed")));
		ILogger mockLogger = Substitute.For<ILogger>();
		_command.Logger = mockLogger;

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "because a missing browser must fail rather than open an unauthenticated window");
		mockLogger.Received(1).WriteError(Arg.Is<string>(s => s.Contains("Chromium binary not found")));
	}

	[Test]
	[Description("OpenAppCommand should be resolvable from the DI composition root so that open-web-app never throws InvalidOperationException at runtime.")]
	public void OpenAppCommand_ShouldBeResolvable_WhenCompositionRootIsBuilt() {
		// Arrange & Act
		Action act = () => Container.GetRequiredService<OpenAppCommand>();

		// Assert
		act.Should().NotThrow("because IChromiumLocator and IAuthenticatedBrowserLauncher must be " +
			"registered so that open-web-app works for all invocations, not just --authenticated ones");
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_applicationClient = Substitute.For<IApplicationClient>();
		_environmentSettings = new EnvironmentSettings();
		_webBrowser = Substitute.For<IWebBrowser>();
		_processExecutor = Substitute.For<IProcessExecutor>();
		_processExecutor.FireAndForgetAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessLaunchResult { Started = true }));
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_browserSessionService = Substitute.For<IBrowserSessionService>();
		_browserSessionService.GetSessionPathAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<string>(),
				Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult("/tmp/clio-session.storageState.json"));
		_authenticatedBrowserLauncher = Substitute.For<IAuthenticatedBrowserLauncher>();
		_authenticatedBrowserLauncher.LaunchAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<string>(),
				Arg.Any<CancellationToken>())
			.Returns(Task.CompletedTask);
		_command = new OpenAppCommand(_applicationClient, _environmentSettings, _webBrowser, _processExecutor,
			_settingsRepository, _browserSessionService, _authenticatedBrowserLauncher);
	}

	#endregion
}
