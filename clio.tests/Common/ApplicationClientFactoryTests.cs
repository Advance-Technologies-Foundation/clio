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

	private static ApplicationClientFactory CreateFactory() {
		// The passthrough executor is substituted; the factory only forwards it into the adapter's
		// bearer branch and never invokes it during construction (the CreatioClient is lazy).
		IReauthExecutor noReauthExecutor = Substitute.For<IReauthExecutor>();
		return new ApplicationClientFactory(noReauthExecutor);
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
}
