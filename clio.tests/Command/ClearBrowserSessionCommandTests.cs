using System;
using System.Threading;
using Clio.Command;
using Clio.Common.BrowserSession;
using Clio.UserEnvironment;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Story 6 (browser-session-handoff): the clear-browser-session CLI verb deletes the cached session
/// for an environment (idempotent) and reports unknown environments with a non-zero exit code.
/// </summary>
[TestFixture]
[Property("Module", "Command")]
public class ClearBrowserSessionCommandTests : BaseCommandTests<ClearBrowserSessionOptions> {

	private ClearBrowserSessionCommand _command = null!;
	private IBrowserSessionService _service = null!;
	private ISettingsRepository _settingsRepository = null!;
	private readonly EnvironmentSettings _env = new() { Uri = "https://dev.creatio.com", Login = "u", Password = "p" };

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_service = Substitute.For<IBrowserSessionService>();
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_settingsRepository.GetEnvironment(Arg.Any<EnvironmentOptions>()).Returns(_env);
		containerBuilder.AddSingleton(_service);
		containerBuilder.AddSingleton(_settingsRepository);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<ClearBrowserSessionCommand>();
	}

	[TearDown]
	public override void TearDown() {
		_service.ClearReceivedCalls();
		_settingsRepository.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Execute clears the cached session and returns 0 for a valid environment.")]
	public void Execute_ShouldReturnZeroAndClearSession_WhenEnvironmentIsValid() {
		// Arrange
		var options = new ClearBrowserSessionOptions { Environment = "MyEnv" };

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because clearing a session is a successful, idempotent operation");
		_service.Received(1).ClearSessionAsync(_env, null, Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("Execute passes --output-path to the service so a credential file written outside the cache is also deleted.")]
	public void Execute_ShouldPassOutputPath_WhenOutputPathIsProvided() {
		// Arrange
		var options = new ClearBrowserSessionOptions { Environment = "MyEnv", OutputPath = "/tmp/session.json" };

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because the command succeeds when an output-path is supplied");
		_service.Received(1).ClearSessionAsync(_env, "/tmp/session.json", Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("Execute returns a non-zero exit code when the environment cannot be resolved.")]
	public void Execute_ShouldReturnNonZero_WhenEnvironmentNotFound() {
		// Arrange
		_settingsRepository.GetEnvironment(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new Exception("environment 'unknown' not found"));
		var options = new ClearBrowserSessionOptions { Environment = "unknown" };

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "because an unresolvable environment must surface as a non-zero exit code");
		_service.DidNotReceive().ClearSessionAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
	}
}
