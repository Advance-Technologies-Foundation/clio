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

	private static ApplicationClientFactory CreateFactory(
		Clio.Common.ExternalAccess.IExternalAccessSessionProvider externalAccessSessionProvider) {
		IReauthExecutor noReauthExecutor = Substitute.For<IReauthExecutor>();
		return new ApplicationClientFactory(noReauthExecutor, externalAccessSessionProvider);
	}

	private static ApplicationClientFactory CreateFactory() {
		// The passthrough executor is substituted; the factory only forwards it into the adapter's
		// bearer branch and never invokes it during construction (the CreatioClient is lazy).
		IReauthExecutor noReauthExecutor = Substitute.For<IReauthExecutor>();
		return new ApplicationClientFactory(noReauthExecutor, Substitute.For<Clio.Common.ExternalAccess.IExternalAccessSessionProvider>());
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

	[Test]
	[Description("CreateEnvironmentClient resolves an external-access session before returning a client")]
	public void CreateEnvironmentClient_ShouldResolveSession_WhenExternalAccessTokenIsSet() {
		// Arrange
		Clio.Common.ExternalAccess.IExternalAccessSessionProvider provider =
			Substitute.For<Clio.Common.ExternalAccess.IExternalAccessSessionProvider>();
		provider.GetSession(Arg.Any<EnvironmentSettings>(), Arg.Any<string>())
			.Returns(new[] {
				new Creatio.Client.CreatioSessionCookie(".ASPXAUTH", "v", "external.creatio.com", "/",
					true, true, null, DateTime.MinValue)
			});
		ApplicationClientFactory sut = CreateFactory(provider);
		EnvironmentSettings settings = new() {
			Uri = "https://external.creatio.com",
			ExternalAccessToken = "jwt-value"
		};

		// Act
		IApplicationClient result = sut.CreateEnvironmentClient(settings);

		// Assert
		provider.Received(1).GetSession(settings, "jwt-value");
		result.Should().BeOfType<CreatioClientAdapter>(
			because: "an external-access session is served through the same adapter as every other client");
	}

	[Test]
	[Description("CreateEnvironmentClient refuses an external-access token combined with an access token")]
	public void CreateEnvironmentClient_ShouldThrow_WhenBothTokenKindsAreSet() {
		// Arrange
		Clio.Common.ExternalAccess.IExternalAccessSessionProvider provider =
			Substitute.For<Clio.Common.ExternalAccess.IExternalAccessSessionProvider>();
		ApplicationClientFactory sut = CreateFactory(provider);
		EnvironmentSettings settings = new() {
			Uri = "https://external.creatio.com",
			ExternalAccessToken = "jwt-value",
			AccessToken = "api-token"
		};

		// Act
		Action act = () => sut.CreateEnvironmentClient(settings);

		// Assert
		act.Should().Throw<NotSupportedException>(
			because: "the two tokens are different authentication models against different endpoints, and "
				+ "silently picking one would connect as an identity the caller did not ask for");
		provider.DidNotReceive().GetSession(Arg.Any<EnvironmentSettings>(), Arg.Any<string>());
	}
}
