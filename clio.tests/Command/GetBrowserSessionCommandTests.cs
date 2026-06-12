using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Common.BrowserSession;
using Clio.UserEnvironment;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Story 5 (browser-session-handoff): the get-browser-session CLI verb resolves the environment,
/// delegates to <see cref="IBrowserSessionService"/>, prints the file path, and reports auth failures
/// with a non-zero exit code.
/// </summary>
[TestFixture]
[Property("Module", "Command")]
public class GetBrowserSessionCommandTests : BaseCommandTests<GetBrowserSessionOptions> {

	private GetBrowserSessionCommand _command = null!;
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
		_command = Container.GetRequiredService<GetBrowserSessionCommand>();
	}

	[TearDown]
	public override void TearDown() {
		_service.ClearReceivedCalls();
		_settingsRepository.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Execute returns 0 and delegates to the session service for a valid environment.")]
	public void Execute_ShouldReturnZeroAndCallService_WhenEnvironmentIsValid() {
		// Arrange
		var options = new GetBrowserSessionOptions { Environment = "MyEnv" };
		_service.GetSessionPathAsync(_env, null, false, Arg.Any<CancellationToken>())
			.Returns(Task.FromResult("/home/.clio/sessions/dev_abc.storageState.json"));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because the service produced a session file path");
		_service.Received(1).GetSessionPathAsync(_env, null, false, Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("Execute forwards --output-path and --force-refresh to the session service.")]
	public void Execute_ShouldForwardOutputPathAndForceRefresh_WhenProvided() {
		// Arrange
		var options = new GetBrowserSessionOptions { Environment = "MyEnv", OutputPath = "/tmp/s.json", ForceRefresh = true };
		_service.GetSessionPathAsync(Arg.Any<EnvironmentSettings>(), "/tmp/s.json", true, Arg.Any<CancellationToken>())
			.Returns(Task.FromResult("/tmp/s.json"));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because the export succeeded");
		_service.Received(1).GetSessionPathAsync(Arg.Any<EnvironmentSettings>(), "/tmp/s.json", true, Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("Execute returns a non-zero exit code when authentication fails.")]
	public void Execute_ShouldReturnNonZero_WhenAuthenticationFails() {
		// Arrange
		var options = new GetBrowserSessionOptions { Environment = "MyEnv" };
		_service.GetSessionPathAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.ThrowsAsync(CreatioAuthenticationException.InvalidCredentials("https://dev.creatio.com"));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "because an authentication failure must surface as a non-zero exit code");
	}
}
