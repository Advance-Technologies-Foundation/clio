using System;
using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Exit-code contracts for <c>login</c>, <c>logout</c> and <c>auth-status</c>. These codes are the
/// whole interface a script or an agent sees: <c>auth-status</c> is the documented automation gate,
/// so a missing session reading as 0 would let a pipeline proceed unauthenticated.
/// </summary>
[TestFixture]
public sealed class LoginCommandTests : BaseCommandTests<LoginOptions> {

	private static EnvironmentSettings SsoEnvironment() => new() {
		Uri = "https://work.creatio.com",
		EnvironmentName = "work",
		ClientId = "clio-client",
		AuthFlow = OAuthFlow.AuthorizationCode
	};

	[Test]
	[Description("A cached session that is still usable short-circuits: no browser is opened and no interactive sign-in is started.")]
	public void Execute_ShouldShortCircuit_WhenAUsableSessionIsCached() {
		// Arrange
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		settings.GetEnvironment(Arg.Any<EnvironmentNameOptions>()).Returns(SsoEnvironment());
		IOAuthTokenStore store = Substitute.For<IOAuthTokenStore>();
		store.TryRead(Arg.Any<EnvironmentSettings>(), out Arg.Any<OAuthTokenSet>()).Returns(call => {
			call[1] = new OAuthTokenSet("access", "refresh", DateTimeOffset.UtcNow.AddHours(1),
				"https://id.example/connect/token", "clio-client", DateTimeOffset.UtcNow);
			return true;
		});
		IOAuthAuthorizationCodeService service = Substitute.For<IOAuthAuthorizationCodeService>();
		LoginCommand sut = new(settings, service, store, Substitute.For<ILogger>());

		// Act
		int exitCode = sut.Execute(new LoginOptions());

		// Assert
		exitCode.Should().Be(0);
		service.DidNotReceive().LoginAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<bool>(), Arg.Any<int>(),
			Arg.Any<System.Threading.CancellationToken>());
	}

	[Test]
	[Description("A failed sign-in reports 1 rather than throwing, so a caller sees a non-zero exit instead of a stack trace.")]
	public void Execute_ShouldReturnOne_WhenSignInFails() {
		// Arrange
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		settings.GetEnvironment(Arg.Any<EnvironmentNameOptions>()).Returns(SsoEnvironment());
		IOAuthTokenStore store = Substitute.For<IOAuthTokenStore>();
		store.TryRead(Arg.Any<EnvironmentSettings>(), out Arg.Any<OAuthTokenSet>()).Returns(false);
		IOAuthAuthorizationCodeService service = Substitute.For<IOAuthAuthorizationCodeService>();
		service.LoginAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<bool>(), Arg.Any<int>(),
				Arg.Any<System.Threading.CancellationToken>())
			.Returns<OAuthTokenSet>(_ => throw new InvalidOperationException("no"));
		LoginCommand sut = new(settings, service, store, Substitute.For<ILogger>());

		// Act
		int exitCode = sut.Execute(new LoginOptions());

		// Assert
		exitCode.Should().Be(1);
	}
}

[TestFixture]
public sealed class LogoutCommandTests : BaseCommandTests<LogoutOptions> {

	[Test]
	[Description("The local session is gone either way, so a failed server-side revocation is a warning and still exits 0 - re-running logout must not be a scripted failure.")]
	public void Execute_ShouldReturnZero_WhenRevocationFails() {
		// Arrange
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		settings.GetEnvironment(Arg.Any<EnvironmentNameOptions>()).Returns(new EnvironmentSettings());
		IOAuthAuthorizationCodeService service = Substitute.For<IOAuthAuthorizationCodeService>();
		service.LogoutAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<System.Threading.CancellationToken>())
			.Returns(_ => throw new InvalidOperationException("revocation refused"));
		LogoutCommand sut = new(settings, service, Substitute.For<ILogger>());

		// Act
		int exitCode = sut.Execute(new LogoutOptions());

		// Assert
		exitCode.Should().Be(0);
	}

	[Test]
	[Description("An unresolvable environment is a caller error and must exit 1 rather than silently succeeding.")]
	public void Execute_ShouldReturnOne_WhenTheEnvironmentCannotBeResolved() {
		// Arrange
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		settings.GetEnvironment(Arg.Any<EnvironmentNameOptions>())
			.Returns(_ => throw new InvalidOperationException("unknown environment"));
		LogoutCommand sut = new(settings, Substitute.For<IOAuthAuthorizationCodeService>(),
			Substitute.For<ILogger>());

		// Act
		int exitCode = sut.Execute(new LogoutOptions());

		// Assert
		exitCode.Should().Be(1);
	}
}

[TestFixture]
public sealed class AuthStatusCommandTests : BaseCommandTests<AuthStatusOptions> {

	private static EnvironmentSettings SsoEnvironment() => new() {
		Uri = "https://work.creatio.com",
		EnvironmentName = "work",
		ClientId = "clio-client",
		AuthFlow = OAuthFlow.AuthorizationCode
	};

	[Test]
	[Description("This command is the automation gate: no cached session must exit 1, or a pipeline would carry on unauthenticated.")]
	public void Execute_ShouldReturnOne_WhenNoSessionIsCached() {
		// Arrange
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		settings.GetEnvironment(Arg.Any<EnvironmentNameOptions>()).Returns(SsoEnvironment());
		IOAuthTokenStore store = Substitute.For<IOAuthTokenStore>();
		store.TryRead(Arg.Any<EnvironmentSettings>(), out Arg.Any<OAuthTokenSet>()).Returns(false);
		AuthStatusCommand sut = new(settings, store, Substitute.For<ILogger>());

		// Act
		int exitCode = sut.Execute(new AuthStatusOptions());

		// Assert
		exitCode.Should().Be(1);
	}

	[Test]
	[Description("An expired access token with a refresh token still counts as a usable session, because the next command refreshes it without a browser.")]
	public void Execute_ShouldReturnZero_WhenOnlyTheAccessTokenExpired() {
		// Arrange
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		settings.GetEnvironment(Arg.Any<EnvironmentNameOptions>()).Returns(SsoEnvironment());
		IOAuthTokenStore store = Substitute.For<IOAuthTokenStore>();
		store.TryRead(Arg.Any<EnvironmentSettings>(), out Arg.Any<OAuthTokenSet>()).Returns(call => {
			call[1] = new OAuthTokenSet("access", "refresh", DateTimeOffset.UtcNow.AddMinutes(-5),
				"https://id.example/connect/token", "clio-client", DateTimeOffset.UtcNow);
			return true;
		});
		AuthStatusCommand sut = new(settings, store, Substitute.For<ILogger>());

		// Act
		int exitCode = sut.Execute(new AuthStatusOptions());

		// Assert
		exitCode.Should().Be(0);
	}

	[Test]
	[Description("A client-credentials environment has no interactive session to report, and reporting it as unauthenticated would break every non-SSO pipeline.")]
	public void Execute_ShouldReturnZero_ForAClientCredentialsEnvironment() {
		// Arrange
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		settings.GetEnvironment(Arg.Any<EnvironmentNameOptions>())
			.Returns(new EnvironmentSettings { Uri = "https://work.creatio.com" });
		IOAuthTokenStore store = Substitute.For<IOAuthTokenStore>();
		AuthStatusCommand sut = new(settings, store, Substitute.For<ILogger>());

		// Act
		int exitCode = sut.Execute(new AuthStatusOptions());

		// Assert
		exitCode.Should().Be(0);
		store.DidNotReceive().TryRead(Arg.Any<EnvironmentSettings>(), out Arg.Any<OAuthTokenSet>());
	}
}
