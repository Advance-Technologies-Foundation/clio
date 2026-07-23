using System;
using System.Net;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[NonParallelizable]
[Property("Module", "Command")]
public class RestartCommandTestCase : BaseCommandTests<RestartOptions> {

	/// <summary>
	/// Verifies that RestartCommand forms the correct application request when the application runs under .NET Core and settings are picked from the environment.
	/// </summary>
	[Test]
	[Description("Ensures RestartCommand sends the correct request for .NET Core environments.")]
	public void RestartCommand_FormsCorrectRequest_ForNetCoreEnvironment() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		EnvironmentSettings environmentSettings = new() {
			Login = "Test",
			Password = "Test",
			IsNetCore = true,
			Maintainer = "Test",
			Uri = "http://test.domain.com"
		};
		RestartCommand restartCommand = new(applicationClient, environmentSettings, Substitute.For<IServerReadinessWaiter>());
		RestartOptions options = new();

		// Act
		restartCommand.Execute(options);

		// Assert
		applicationClient.Received(1).ExecutePostRequest(
			environmentSettings.Uri + "/ServiceModel/AppInstallerService.svc/RestartApp",
			"{}", 100_000, 3);
	}

	/// <summary>
	/// Verifies that RestartCommand forms the correct application request when the application runs under .NET Framework and settings are picked from the environment.
	/// </summary>
	[Test]
	[Description("Ensures RestartCommand sends the correct request for .NET Framework environments.")]
	public void RestartCommand_FormsCorrectRequest_ForNetFrameworkEnvironment() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		EnvironmentSettings environmentSettings = new() {
			Login = "Test",
			Password = "Test",
			IsNetCore = false,
			Maintainer = "Test",
			Uri = "http://test.domain.com"
		};
		RestartCommand restartCommand = new(applicationClient, environmentSettings, Substitute.For<IServerReadinessWaiter>());
		RestartOptions options = new();

		// Act
		restartCommand.Execute(options);

		// Assert
		applicationClient.Received(1).ExecutePostRequest(
			environmentSettings.Uri + "/0/ServiceModel/AppInstallerService.svc/UnloadAppDomain",
			"{}", 100_000, 3);
	}

	[Test]
	[Description("RemoteCommand should log a user-friendly message when site is unreachable (AggregateException wrapping WebException ConnectFailure)")]
	public void Execute_ShouldLogFriendlyMessage_WhenSiteIsUnreachable() {
		bool originalDebugMode = Program.IsDebugMode;
		Program.IsDebugMode = false;
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		ILogger mockLogger = Substitute.For<ILogger>();
		EnvironmentSettings environmentSettings = new() {
			Login = "Test", Password = "Test", IsNetCore = true,
			Maintainer = "Test", Uri = "http://localhost:1616"
		};
		var connectFailure = new WebException("Connection refused (localhost:1616)", WebExceptionStatus.ConnectFailure);
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(_ => throw new AggregateException(connectFailure));
		RestartCommand restartCommand = new(applicationClient, environmentSettings, Substitute.For<IServerReadinessWaiter>());
		restartCommand.Logger = mockLogger;
		try {
			restartCommand.Execute(new RestartOptions());

			mockLogger.Received(1).WriteError(Arg.Is<string>(s =>
				s.StartsWith("Cannot connect to the application:") &&
				s.Contains("Make sure the site is running")));
		} finally {
			Program.IsDebugMode = originalDebugMode;
		}
	}

	[Test]
	[Description("RemoteCommand should log full stack trace when site is unreachable and debug mode is on")]
	public void Execute_ShouldLogFullStackTrace_WhenSiteIsUnreachable_InDebugMode() {
		bool originalDebugMode = Program.IsDebugMode;
		Program.IsDebugMode = true;
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		ILogger mockLogger = Substitute.For<ILogger>();
		EnvironmentSettings environmentSettings = new() {
			Login = "Test", Password = "Test", IsNetCore = true,
			Maintainer = "Test", Uri = "http://localhost:1616"
		};
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(_ => throw new AggregateException(new WebException("Connection refused", WebExceptionStatus.ConnectFailure)));
		RestartCommand restartCommand = new(applicationClient, environmentSettings, Substitute.For<IServerReadinessWaiter>());
		restartCommand.Logger = mockLogger;
		try {
			restartCommand.Execute(new RestartOptions());

			mockLogger.Received(1).WriteError(Arg.Is<string>(s => s.Contains("   at ")));
		} finally {
			Program.IsDebugMode = originalDebugMode;
		}
	}

	[Test]
	[Description("When --wait-ready is not set, the readiness waiter must never be invoked, preserving the pre-ENG-91315 fire-and-forget behavior.")]
	public void Execute_Should_NotCallWaiter_When_WaitReady_Is_False() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		EnvironmentSettings environmentSettings = new() {
			Login = "Test", Password = "Test", IsNetCore = true, Maintainer = "Test", Uri = "http://test.domain.com"
		};
		IServerReadinessWaiter waiter = Substitute.For<IServerReadinessWaiter>();
		RestartCommand restartCommand = new(applicationClient, environmentSettings, waiter);

		// Act
		int exitCode = restartCommand.Execute(new RestartOptions { WaitReady = false });

		// Assert
		exitCode.Should().Be(0, because: "the restart request itself succeeded");
		waiter.DidNotReceive().WaitForReady(Arg.Any<ServerReadinessOptions>());
	}

	[Test]
	[Description("When --wait-ready is set and the waiter confirms readiness, Execute returns 0.")]
	public void Execute_Should_ReturnZero_When_WaitReady_True_And_WaiterConfirmsReady() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		EnvironmentSettings environmentSettings = new() {
			Login = "Test", Password = "Test", IsNetCore = true, Maintainer = "Test", Uri = "http://test.domain.com"
		};
		IServerReadinessWaiter waiter = Substitute.For<IServerReadinessWaiter>();
		waiter.WaitForReady(Arg.Any<ServerReadinessOptions>()).Returns(true);
		RestartCommand restartCommand = new(applicationClient, environmentSettings, waiter);

		// Act
		int exitCode = restartCommand.Execute(new RestartOptions { WaitReady = true, ReadyTimeout = 120 });

		// Assert
		exitCode.Should().Be(0, because: "the instance answered its health-check within the readiness budget");
		waiter.Received(1).WaitForReady(Arg.Is<ServerReadinessOptions>(readinessOptions =>
			readinessOptions.Uri == environmentSettings.Uri &&
			readinessOptions.IsNetCore == environmentSettings.IsNetCore &&
			readinessOptions.Timeout == TimeSpan.FromSeconds(120)));
	}

	[Test]
	[Description("When --wait-ready is set and the waiter times out, Execute returns a non-zero exit code.")]
	public void Execute_Should_ReturnOne_When_WaitReady_True_And_WaiterTimesOut() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		EnvironmentSettings environmentSettings = new() {
			Login = "Test", Password = "Test", IsNetCore = false, Maintainer = "Test", Uri = "http://test.domain.com"
		};
		IServerReadinessWaiter waiter = Substitute.For<IServerReadinessWaiter>();
		waiter.WaitForReady(Arg.Any<ServerReadinessOptions>()).Returns(false);
		RestartCommand restartCommand = new(applicationClient, environmentSettings, waiter);

		// Act
		int exitCode = restartCommand.Execute(new RestartOptions { WaitReady = true });

		// Assert
		exitCode.Should().Be(1, because: "the instance never answered its health-check within the readiness budget");
	}

	[Test]
	[Description("When the restart request itself fails, the readiness waiter must not run even if --wait-ready was requested.")]
	public void Execute_Should_NotCallWaiter_When_RestartRequestFails() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		EnvironmentSettings environmentSettings = new() {
			Login = "Test", Password = "Test", IsNetCore = true, Maintainer = "Test", Uri = "http://localhost:1616"
		};
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(_ => throw new AggregateException(new WebException("Connection refused", WebExceptionStatus.ConnectFailure)));
		IServerReadinessWaiter waiter = Substitute.For<IServerReadinessWaiter>();
		RestartCommand restartCommand = new(applicationClient, environmentSettings, waiter) { Logger = Substitute.For<ILogger>() };

		// Act
		int exitCode = restartCommand.Execute(new RestartOptions { WaitReady = true });

		// Assert
		exitCode.Should().Be(1, because: "the restart request itself failed before any readiness wait could start");
		waiter.DidNotReceive().WaitForReady(Arg.Any<ServerReadinessOptions>());
	}

}
