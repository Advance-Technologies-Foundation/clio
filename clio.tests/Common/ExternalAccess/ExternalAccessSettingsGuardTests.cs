using System;
using Clio;
using Clio.Common;
using Clio.Common.ExternalAccess;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.ExternalAccess;

/// <summary>
/// The rule about what an external-access token may be combined with. It used to live inside
/// ApplicationClientFactory, so the same command line was refused on one dispatch path and silently
/// authenticated under the support grant on the other two — a connection as an identity the caller
/// never named.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class ExternalAccessSettingsGuardTests {

	[Test]
	[Description("An external-access token is exchanged for a session; an access token is sent on every request. Picking one silently would authenticate as the wrong identity.")]
	public void Validate_ShouldRefuse_WhenAnAccessTokenIsAlsoSupplied() {
		// Arrange
		EnvironmentSettings settings = new() {
			Uri = "https://customer.creatio.com",
			ExternalAccessToken = "jwt",
			AccessToken = "api-token"
		};

		// Act
		Action act = () => ExternalAccessSettingsGuard.Validate(settings);

		// Assert
		act.Should().Throw<NotSupportedException>().Which.Message.Should().Contain("exactly one");
	}

	[Test]
	[Description("An OAuth client authenticates as its own identity, which is not the support grant. A registered environment can carry a stored ClientId, and inheriting it silently is the same wrong-identity failure.")]
	public void Validate_ShouldRefuse_WhenAnOAuthClientIsAlsoPresent() {
		// Arrange
		EnvironmentSettings settings = new() {
			Uri = "https://customer.creatio.com",
			ExternalAccessToken = "jwt",
			ClientId = "clio-client",
			ClientSecret = "clio-secret"
		};

		// Act
		Action act = () => ExternalAccessSettingsGuard.Validate(settings);

		// Assert
		act.Should().Throw<NotSupportedException>().Which.Message.Should().Contain("OAuth client");
	}

	[Test]
	[Description("The supported shape — a url and a token and nothing else — must pass, and so must every environment that carries no external-access token at all.")]
	public void Validate_ShouldAllow_TheSupportedShapesAndEveryNonExternalAccessEnvironment() {
		// Arrange
		EnvironmentSettings externalAccessOnly = new() {
			Uri = "https://customer.creatio.com", ExternalAccessToken = "jwt"
		};
		EnvironmentSettings oauthOnly = new() {
			Uri = "https://work.creatio.com", ClientId = "clio-client", ClientSecret = "clio-secret"
		};

		// Act
		Action externalAccess = () => ExternalAccessSettingsGuard.Validate(externalAccessOnly);
		Action oauth = () => ExternalAccessSettingsGuard.Validate(oauthOnly);
		Action nothing = () => ExternalAccessSettingsGuard.Validate(null);

		// Assert
		externalAccess.Should().NotThrow();
		oauth.Should().NotThrow(because: "the rule only applies when an external-access token is present");
		nothing.Should().NotThrow();
	}

	[Test]
	[Description("Fill must not inherit the token from a stored environment: it is a per-invocation support-grant credential that is never persisted, so it can only come from the command line.")]
	public void Fill_ShouldNotInheritTheExternalAccessToken_FromTheStoredEnvironment() {
		// Arrange
		EnvironmentSettings stored = new() {
			Uri = "https://work.creatio.com",
			ExternalAccessToken = "stored-token"
		};

		// Act
		EnvironmentSettings result = stored.Fill(new EnvironmentOptions(),
			Substitute.For<IInteractiveConsole>());

		// Assert
		result.ExternalAccessToken.Should().BeNullOrEmpty(
			because: "AC-2 — the token is never persisted, so a stored value must never be carried into a run");
	}
}
