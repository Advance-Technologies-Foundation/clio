using System;
using Clio;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
internal sealed class ApplicationClientFactoryTests {

	#region Methods: Private

	private static ApplicationClientFactory CreateFactory(IOAuthAuthorizationCodeService oauthService = null) {
		// The passthrough executor is substituted; the factory only forwards it into the adapter's
		// bearer branch and never invokes it during construction (the CreatioClient is lazy).
		IReauthExecutor noReauthExecutor = Substitute.For<IReauthExecutor>();
		return new ApplicationClientFactory(noReauthExecutor, oauthService);
	}

	private static EnvironmentSettings AuthorizationCodeEnvironment() => new() {
		Uri = "https://sso.creatio.com",
		ClientId = "clio-client",
		AuthFlow = OAuthFlow.AuthorizationCode,
		IsNetCore = true
	};

	private static void ForceClientCreation(IApplicationClient client) {
		System.Reflection.FieldInfo transportField = client.GetType().GetField("_transport",
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
		transportField.Should().NotBeNull(
			because: "CreatioClientAdapter._transport is what defers the client construction");
		object transport = transportField!.GetValue(client);
		try {
			transport.GetType().GetMethod("EnsureCreated")!.Invoke(transport, null);
		} catch (System.Reflection.TargetInvocationException e) when (e.InnerException is not null) {
			throw e.InnerException;
		}
	}

	private static IOAuthAuthorizationCodeService SubstituteOAuthService() {
		IOAuthAuthorizationCodeService service = Substitute.For<IOAuthAuthorizationCodeService>();
		service.ResolveAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<System.Threading.CancellationToken>())
			.Returns(new OAuthTokenSet("sso-access", "sso-refresh", DateTimeOffset.UtcNow.AddHours(1),
				"https://id.example/connect/token", "clio-client", DateTimeOffset.UtcNow));
		return service;
	}

	#endregion

	[Test]
	[Description("CreateClient builds a CreatioClientAdapter via the bearer branch when AccessToken is set")]
	public void CreateClient_ShouldBuildAdapter_WhenAccessTokenIsSet() {
		// Arrange
		ApplicationClientFactory sut = CreateFactory();
		EnvironmentSettings settings = new() {
			Uri = "https://passthrough.creatio.com",
			AccessToken = "opaque-token"
		};

		// Act
		IApplicationClient result = sut.CreateClient(settings);

		// Assert
		result.Should().BeOfType<CreatioClientAdapter>(
			because: "a non-empty AccessToken must route through the bearer branch and wrap a CreatioClientAdapter");
	}

	[Test]
	[Description("CreateEnvironmentClient builds a CreatioClientAdapter via the bearer branch when AccessToken is set")]
	public void CreateEnvironmentClient_ShouldBuildAdapter_WhenAccessTokenIsSet() {
		// Arrange
		ApplicationClientFactory sut = CreateFactory();
		EnvironmentSettings settings = new() {
			Uri = "https://passthrough.creatio.com",
			AccessToken = "opaque-token"
		};

		// Act
		IApplicationClient result = sut.CreateEnvironmentClient(settings);

		// Assert
		result.Should().BeOfType<CreatioClientAdapter>(
			because: "a non-empty AccessToken must route through the bearer branch even when a service-url builder is wired");
	}

	[Test]
	[Description("CreateClient keeps the login/password branch unchanged when no token or cookie is present")]
	public void CreateClient_ShouldUseLoginPasswordBranch_WhenNoTokenPresent() {
		// Arrange
		ApplicationClientFactory sut = CreateFactory();
		EnvironmentSettings settings = new() {
			Uri = "https://legacy.creatio.com",
			Login = "Supervisor",
			Password = "Supervisor"
		};

		// Act
		IApplicationClient result = sut.CreateClient(settings);

		// Assert
		result.Should().BeOfType<CreatioClientAdapter>(
			because: "without a token the factory must fall through to the existing login/password path with no regression");
	}

	[Test]
	[Description("CreateEnvironmentClient keeps the login/password branch unchanged when no token or cookie is present")]
	public void CreateEnvironmentClient_ShouldUseLoginPasswordBranch_WhenNoTokenPresent() {
		// Arrange
		ApplicationClientFactory sut = CreateFactory();
		EnvironmentSettings settings = new() {
			Uri = "https://legacy.creatio.com",
			Login = "Supervisor",
			Password = "Supervisor"
		};

		// Act
		IApplicationClient result = sut.CreateEnvironmentClient(settings);

		// Assert
		result.Should().BeOfType<CreatioClientAdapter>(
			because: "without a token the factory must fall through to the existing login/password path with no regression");
	}

	[Test]
	[Description("CreateClient throws a url-naming ArgumentException when an AccessToken is set but the url is blank")]
	public void CreateClient_ShouldThrowArgumentException_WhenAccessTokenSetButUrlBlank() {
		// Arrange
		ApplicationClientFactory sut = CreateFactory();
		EnvironmentSettings settings = new() {
			Uri = "   ",
			AccessToken = "opaque-token"
		};

		// Act
		Action act = () => sut.CreateClient(settings);

		// Assert
		act.Should().Throw<ArgumentException>()
			.Which.Message.Should().Contain("url",
				because: "the error must be caller-actionable and name the missing url, never echo the secret token");
		act.Should().Throw<ArgumentException>()
			.Which.Message.Should().NotContain("opaque-token",
				because: "the error must never expose the secret access token value (FR-12)");
	}

	[Test]
	[Description("CreateClient throws NotSupportedException when the access-token type is not Bearer")]
	public void CreateClient_ShouldThrowNotSupportedException_WhenAccessTokenTypeIsNotBearer() {
		// Arrange
		ApplicationClientFactory sut = CreateFactory();
		EnvironmentSettings settings = new() {
			Uri = "https://passthrough.creatio.com",
			AccessToken = "opaque-token",
			AccessTokenType = "MAC"
		};

		// Act
		Action act = () => sut.CreateClient(settings);

		// Assert
		act.Should().Throw<NotSupportedException>()
			.Which.Message.Should().Contain("Bearer",
				because: "only the Bearer access-token type is supported in v1 and the error must say so");
	}

	[Test]
	[Description("CreateClient throws NotSupportedException when only a Cookie is present (cookie leg dropped in v1)")]
	public void CreateClient_ShouldThrowNotSupportedException_WhenOnlyCookiePresent() {
		// Arrange
		ApplicationClientFactory sut = CreateFactory();
		EnvironmentSettings settings = new() {
			Uri = "https://passthrough.creatio.com",
			Cookie = "BPMLOADER=abc; .ASPXAUTH=def"
		};

		// Act
		Action act = () => sut.CreateClient(settings);

		// Assert
		act.Should().Throw<NotSupportedException>()
			.Which.Message.Should().Contain("Cookie",
				because: "cookie-based authentication is dropped from v1 and must fail with a clear message");
	}

	[Test]
	[Description("CreateEnvironmentClient throws NotSupportedException when only a Cookie is present (cookie leg dropped in v1)")]
	public void CreateEnvironmentClient_ShouldThrowNotSupportedException_WhenOnlyCookiePresent() {
		// Arrange
		ApplicationClientFactory sut = CreateFactory();
		EnvironmentSettings settings = new() {
			Uri = "https://passthrough.creatio.com",
			Cookie = "BPMLOADER=abc; .ASPXAUTH=def"
		};

		// Act
		Action act = () => sut.CreateEnvironmentClient(settings);

		// Assert
		act.Should().Throw<NotSupportedException>()
			.Which.Message.Should().Contain("Cookie",
				because: "cookie-based authentication is dropped from v1 and must fail with a clear message");
	}

	[TestCase(null, "password")]
	[TestCase("login", null)]
	[TestCase(" ", "password")]
	[Description("The dedicated forms client rejects incomplete credentials before any transport is created.")]
	public void CreateFormsEnvironmentClient_ShouldRejectIncompleteCredentials(string login, string password) {
		// Arrange
		ApplicationClientFactory sut = CreateFactory();
		EnvironmentSettings settings = new() { Uri = "https://creatio.test", Login = login, Password = password };

		// Act
		Action act = () => sut.CreateFormsEnvironmentClient(settings);

		// Assert
		act.Should().Throw<ArgumentException>()
			.Which.Message.Should().Contain("login and password",
				because: "forms authentication must fail closed before CreatioClient can attempt an empty login");
	}

	[Test]
	[Description("An authorization-code environment resolves through the OAuth service on BOTH entry points, and the token is read lazily: constructing the client must cost no token-store read and no refresh round-trip, because a client is often built and never used.")]
	public void AuthorizationCodeEnvironment_ShouldResolveTheTokenLazily_OnBothEntryPoints() {
		// Arrange
		IOAuthAuthorizationCodeService oauthService = SubstituteOAuthService();
		ApplicationClientFactory sut = CreateFactory(oauthService);

		// Act
		IApplicationClient client = sut.CreateClient(AuthorizationCodeEnvironment());
		IApplicationClient environmentClient = sut.CreateEnvironmentClient(AuthorizationCodeEnvironment());

		// Assert
		client.Should().BeOfType<CreatioClientAdapter>();
		environmentClient.Should().BeOfType<CreatioClientAdapter>();
		oauthService.DidNotReceive().ResolveAsync(Arg.Any<EnvironmentSettings>(),
			Arg.Any<System.Threading.CancellationToken>());
	}

	[Test]
	[Description("Without the OAuth service the factory must fail with a named reason instead of silently producing an unauthenticated client. The parameter is optional for older call sites, so nothing else catches this.")]
	public void AuthorizationCodeEnvironment_ShouldFail_WhenTheOAuthServiceIsNotRegistered() {
		// Arrange
		ApplicationClientFactory sut = CreateFactory();
		IApplicationClient client = sut.CreateEnvironmentClient(AuthorizationCodeEnvironment());

		// Act
		// Force the deferred client creation the same way the adapter does on first use, without
		// reaching the network.
		Action act = () => ForceClientCreation(client);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.Which.Message.Should().Contain("OAuth authorization-code service is not registered");
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase(" ")]
	[Description("The dedicated bearer client rejects a blank token before any transport is created.")]
	public void CreateBearerEnvironmentClient_ShouldRejectBlankToken(string token) {
		// Arrange
		ApplicationClientFactory sut = CreateFactory();
		EnvironmentSettings settings = new() { Uri = "https://creatio.test", IsNetCore = true };

		// Act
		Action act = () => sut.CreateBearerEnvironmentClient(settings, token);

		// Assert
		act.Should().Throw<ArgumentException>()
			.Which.Message.Should().Contain("access token",
				because: "a missing bearer token must not fall through to implicit forms authentication");
	}
}
