using Clio.Command.OAuthAppConfiguration;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.OAuthAppConfiguration;

[TestFixture]
[Property("Module", "Command")]
internal sealed class IdentityServerUrlResolverTests
{
	private readonly IIdentityServerUrlResolver _resolver = new IdentityServerUrlResolver();

	[Test]
	[Category("Unit")]
	[Description("Derives the -is identity host by inserting the suffix into the first host label of a cloud Creatio URL.")]
	public void DeriveIdentityServerUrl_ShouldInsertIsSuffixIntoFirstLabel_WhenCloudHost() {
		// Arrange
		const string creatioUrl = "https://186843-crm-bundle.creatio.com";

		// Act
		string derived = _resolver.DeriveIdentityServerUrl(creatioUrl);

		// Assert
		derived.Should().Be("https://186843-crm-bundle-is.creatio.com",
			because: "the -is suffix must be inserted into the first host label, preserving scheme and domain");
	}

	[Test]
	[Category("Unit")]
	[Description("Preserves a non-default port when deriving the identity host.")]
	public void DeriveIdentityServerUrl_ShouldPreservePort_WhenNonDefaultPortPresent() {
		// Arrange
		const string creatioUrl = "http://localhost:5000";

		// Act
		string derived = _resolver.DeriveIdentityServerUrl(creatioUrl);

		// Assert
		derived.Should().Be("http://localhost-is:5000",
			because: "a non-default port must be preserved on the derived identity host");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns an empty string when the Creatio URL is not an absolute URL.")]
	public void DeriveIdentityServerUrl_ShouldReturnEmpty_WhenUrlIsNotAbsolute() {
		// Arrange
		const string creatioUrl = "not-a-url";

		// Act
		string derived = _resolver.DeriveIdentityServerUrl(creatioUrl);

		// Assert
		derived.Should().BeEmpty(
			because: "a non-absolute URL cannot be parsed into a host to derive from");
	}

	[Test]
	[Category("Unit")]
	[Description("Computes the connect/token endpoint from the identity base URL.")]
	public void GetTokenEndpoint_ShouldAppendConnectToken_WhenBaseUrlProvided() {
		// Arrange
		const string baseUrl = "https://crm-is.creatio.com/";

		// Act
		string endpoint = _resolver.GetTokenEndpoint(baseUrl);

		// Assert
		endpoint.Should().Be("https://crm-is.creatio.com/connect/token",
			because: "the token endpoint is the base URL plus /connect/token with no double slash");
	}

	[Test]
	[Category("Unit")]
	[Description("Computes the OpenID discovery endpoint from the identity base URL.")]
	public void GetDiscoveryEndpoint_ShouldAppendWellKnown_WhenBaseUrlProvided() {
		// Arrange
		const string baseUrl = "https://crm-is.creatio.com";

		// Act
		string endpoint = _resolver.GetDiscoveryEndpoint(baseUrl);

		// Assert
		endpoint.Should().Be("https://crm-is.creatio.com/.well-known/openid-configuration",
			because: "the discovery endpoint is the base URL plus the well-known OpenID path");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns an empty endpoint when the identity base URL is empty.")]
	public void GetTokenEndpoint_ShouldReturnEmpty_WhenBaseUrlEmpty() {
		// Arrange
		string baseUrl = string.Empty;

		// Act
		string endpoint = _resolver.GetTokenEndpoint(baseUrl);

		// Assert
		endpoint.Should().BeEmpty(
			because: "no endpoint can be computed without a base URL");
	}
}
