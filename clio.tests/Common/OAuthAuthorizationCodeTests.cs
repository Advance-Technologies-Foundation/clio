using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class OAuthAuthorizationCodeTests {
	[Test]
	[Description("PKCE challenge matches the RFC 7636 S256 test vector")]
	public void CreateCodeChallenge_ShouldMatchRfc7636Vector_WhenVerifierIsKnown() {
		// Arrange
		const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

		// Act
		string challenge = OAuthAuthorizationCodeProtocol.CreateCodeChallenge(verifier);

		// Assert
		challenge.Should().Be("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
			because: "the authorization server validates the exact RFC 7636 S256 transformation");
	}

	[Test]
	[Description("PKCE verifier uses the RFC 7636 unreserved alphabet and required length")]
	public void CreateCodeVerifier_ShouldUseAllowedLengthAndCharacters_WhenGenerated() {
		// Arrange & Act
		string verifier = OAuthAuthorizationCodeProtocol.CreateCodeVerifier();

		// Assert
		verifier.Length.Should().BeInRange(43, 128, because: "RFC 7636 limits the code verifier length");
		verifier.Should().MatchRegex("^[A-Za-z0-9._~-]+$", because: "PKCE verifiers may contain only RFC 7636 unreserved characters");
	}

	[Test]
	[Description("Callback parser rejects a state mismatch before a caller can exchange the code")]
	public void ParseCallback_ShouldRejectStateMismatch_WhenCallbackStateDiffers() {
		// Arrange
		const string callback = "http://127.0.0.1/callback?code=opaque-code&state=wrong";

		// Act
		System.Action act = () => OAuthAuthorizationCodeProtocol.ParseCallback(callback, "expected");

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*state*",
			because: "a callback from another authorization request must never reach the token endpoint");
	}

	[Test]
	[Description("Callback parser accepts the code and state while ignoring unrelated parameters")]
	public void ParseCallback_ShouldReturnCode_WhenCallbackContainsExtraParameters() {
		// Arrange
		const string callback = "https://redirect.example/cb?code=opaque%2Dcode&state=expected&foo=bar";

		// Act
		(string code, string state) = OAuthAuthorizationCodeProtocol.ParseCallback(callback, "expected");

		// Assert
		code.Should().Be("opaque-code", because: "the authorization code is percent-decoded exactly once");
		state.Should().Be("expected", because: "the validated state is returned for the completed exchange");
	}
}
