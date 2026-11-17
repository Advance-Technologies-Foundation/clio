using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// Unit tests for <see cref="CredentialShapeMasker"/>: credentials recognisable by their shape in text a process run
/// returns, and the business text it must leave alone.
/// </summary>
[TestFixture]
[Property("Module", "Common")]
[Category("Unit")]
public sealed class CredentialShapeMaskerTests {

	// "probe-user:fake-pass-0000", a test value.
	private const string BasicToken = "cHJvYmUtdXNlcjpmYWtlLXBhc3MtMDAwMA==";

	[Test]
	[Description("An echoed JSON body loses its Basic credential and its secret-named header value, and keeps the rest.")]
	public void Mask_Should_Hide_The_Credentials_Of_An_Echoed_Body() {
		// Arrange
		string echo = $"{{\"headers\": {{\"Authorization\": \"Basic {BasicToken}\", \"X-Api-Key\": \"fake-probe-key-7731\", "
			+ "\"Host\": \"httpbin.org\"}}";

		// Act
		string masked = CredentialShapeMasker.Mask(echo);

		// Assert
		masked.Should().NotContain(BasicToken, because: "the token decodes to the service's user and password");
		masked.Should().NotContain("fake-probe-key-7731", because: "an API key header is named as a secret");
		masked.Should().Contain("httpbin.org", because: "a host is business data, not a credential");
	}

	[TestCase("The call sent Basic {0} and used Basic authentication", "The call sent Basic [redacted] and used Basic authentication")]
	[TestCase("Authorization=[redacted] {0}", "Authorization=[redacted] [redacted]")]
	[Description("A Basic credential is hidden in prose and after an earlier redaction; the word Basic in prose is not.")]
	public void Mask_Should_Hide_A_Bare_Basic_Credential(string template, string expected) {
		// Arrange
		string text = string.Format(template, BasicToken);

		// Act
		string masked = CredentialShapeMasker.Mask(text);

		// Assert
		masked.Should().Be(expected, because: "only a payload that decodes to user:password is a credential");
	}

	[Test]
	[Description("A Basic credential cut short, as a clamp leaves it, is still recognised.")]
	public void Mask_Should_Hide_A_Truncated_Basic_Credential() {
		// Arrange
		string text = "Basic " + BasicToken[..23];

		// Act
		string masked = CredentialShapeMasker.Mask(text);

		// Assert
		masked.Should().Be("Basic [redacted]", because: "the decodable prefix already holds the user name and the colon");
	}

	[TestCase("Authorization failed for Bearer abc123def456ghi789", "abc123def456ghi789")]
	[TestCase("token eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTYifQ.c2lnbmF0dXJlLXByb2Jl", "eyJzdWIiOiIxMjM0NTYifQ")]
	[TestCase("X-Api-Key: fake-probe-key-7731", "fake-probe-key-7731")]
	[TestCase("password=hunter2-probe", "hunter2-probe")]
	[TestCase("{\"headers\":{\"X-Api-Key\":\"sk_live_cut_short", "sk_live_cut_short")]
	[Description("A bearer token, a JWT, a secret-named header and a secret-named pair are hidden.")]
	public void Mask_Should_Hide_Recognisable_Secrets(string text, string secret) {
		// Act
		string masked = CredentialShapeMasker.Mask(text);

		// Assert
		masked.Should().NotContain(secret, because: "the shape or the name says it is a credential");
	}

	[TestCase("Order 42 was created at https://example.com/orders/42?uid=42&tab=2")]
	[TestCase("Host: crm.example.com, Database: Sales, Pass: 3, Fail: 1")]
	[TestCase("Pay to bearer on demand")]
	[TestCase("Basic Overview")]
	[TestCase("{\"auth_required\":true,\"server_port\":443}")]
	[Description("Business text with no credential comes back unchanged.")]
	public void Mask_Should_Leave_Business_Text_Alone(string text) {
		// Act
		string masked = CredentialShapeMasker.Mask(text);

		// Assert
		masked.Should().Be(text, because: "a result value is business data, and only a credential may be replaced");
	}

	[Test]
	[Description("A missing value stays missing.")]
	public void Mask_Should_Return_Null_For_Null() {
		// Act
		string masked = CredentialShapeMasker.Mask(null);

		// Assert
		masked.Should().BeNull(because: "there is nothing to mask");
	}
}
