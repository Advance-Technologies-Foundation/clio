using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

	[Test]
	[Description("The server matches the redirect exactly, so replacing the port keeps a trailing slash only when the configured redirect had one.")]
	[TestCase("http://127.0.0.1:37319/", 5000, "http://127.0.0.1:5000/")]
	[TestCase("http://127.0.0.1:37319", 5000, "http://127.0.0.1:5000")]
	[TestCase("http://127.0.0.1:37319/callback", 5000, "http://127.0.0.1:5000/callback")]
	public void ReplacePort_ShouldKeepTheTrailingSlashAsConfigured(string redirect, int port, string expected) {
		// Act
		string result = OAuthAuthorizationCodeService.ReplacePort(redirect, port);

		// Assert
		result.Should().Be(expected);
	}

	[Test]
	[Description("A browser preconnect socket that never sends a request must not hold up the real redirect waiting behind it.")]
	public async Task ReceiveCallbackAsync_ShouldReturnTheRedirect_WhenAnIdleConnectionArrivesFirst() {
		// Arrange
		TcpListener listener = new(IPAddress.Loopback, 0);
		listener.Start();
		try {
			int port = ((IPEndPoint)listener.LocalEndpoint).Port;
			using TcpClient idle = new();
			await idle.ConnectAsync(IPAddress.Loopback, port);
			Task<string> receive = OAuthAuthorizationCodeService.ReceiveCallbackAsync(listener, 10000,
				CancellationToken.None, connectionTimeout: 300);
			using TcpClient real = new();
			await real.ConnectAsync(IPAddress.Loopback, port);
			byte[] request = Encoding.ASCII.GetBytes("GET /?code=abc&state=xyz HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n");
			await real.GetStream().WriteAsync(request);

			// Act
			string redirect = await receive;

			// Assert
			redirect.Should().Be("http://127.0.0.1/?code=abc&state=xyz");
		}
		finally {
			listener.Stop();
		}
	}
}
