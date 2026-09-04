using System;
using System.Net;
using System.Net.Sockets;
using System.Net.Http;
using System.Security.Authentication;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
internal sealed class AuthenticationFailureClassifierTests {

	[Test]
	[TestCase("The SSL connection could not be established.")]
	[TestCase("SSL connection failed")]
	[TestCase("TLS failure")]
	[TestCase("The remote certificate is invalid according to the validation procedure.")]
	[TestCase("Could not create secure channel")]
	[TestCase("A call to SSPI failed: the handshake was aborted")]
	[Description("Transport-security prose is not reported as rejected credentials")]
	public void IsAuthenticationFailure_ShouldReturnFalse_WhenMessageNamesTransportSecurity(string message) {
		// Arrange
		AuthenticationException exception = new(message);

		// Act
		bool result = AuthenticationFailureClassifier.IsAuthenticationFailure(exception);

		// Assert
		result.Should().BeFalse(
			because: "a TLS/certificate failure needs a certificate diagnosis, not 'verify the environment "
			+ "credentials' - the word boundaries around SSL and TLS must be regex boundaries, not literal "
			+ "backspace characters, or a status-less AuthenticationException falls through to the credential arm");
	}

	[Test]
	[Description("A certificate failure wrapped by a status-less HttpRequestException stays a transport failure")]
	public void IsAuthenticationFailure_ShouldReturnFalse_WhenCertificateFailureIsWrapped() {
		// Arrange
		HttpRequestException exception = new(
			"The SSL connection could not be established, see inner exception.",
			new AuthenticationException("The remote certificate is invalid."));

		// Act
		bool result = AuthenticationFailureClassifier.IsAuthenticationFailure(exception);

		// Assert
		result.Should().BeFalse(
			because: "the certificate prose sits on the inner exception while the outer one only reports a "
			+ "failed connection, so the whole chain must be examined");
	}

	[Test]
	[Description("A rejected credential is still classified as an authentication failure")]
	public void IsAuthenticationFailure_ShouldReturnTrue_WhenCredentialsAreRejected() {
		// Arrange
		AuthenticationException exception = new("Creatio rejected the credentials.");

		// Act
		bool result = AuthenticationFailureClassifier.IsAuthenticationFailure(exception);

		// Assert
		result.Should().BeTrue(
			because: "the transport-security guard must not swallow the credential diagnosis it was added beside");
	}

	[Test]
	[Description("A typed 401 outranks certificate prose in the same message")]
	public void IsAuthenticationFailure_ShouldReturnTrue_WhenTypedUnauthorizedCarriesCertificateProse() {
		// Arrange
		HttpRequestException exception = new(
			"certificate check logged during the request", null, HttpStatusCode.Unauthorized);

		// Act
		bool result = AuthenticationFailureClassifier.IsAuthenticationFailure(exception);

		// Assert
		result.Should().BeTrue(
			because: "a typed status is authoritative and is read before any prose match");
	}

	[Test]
	[TestCase("Connection refused at http://localhost:40124")]
	[TestCase("Correlation id x401y")]
	[Description("Digits that merely look like a status are not rejected credentials")]
	public void IsAuthenticationFailure_ShouldReturnFalse_WhenStatusTokenIsNotStandalone(string message) {
		// Arrange
		InvalidOperationException exception = new(message);

		// Act
		bool result = AuthenticationFailureClassifier.IsAuthenticationFailure(exception);

		// Assert
		result.Should().BeFalse(
			because: "401 counts only as a standalone token, never as a substring of a port or an identifier");
	}

	[Test]
	[TestCase("5: Your password has expired.", TestName = "ExpiredPasswordProse")]
	[TestCase("Authentication failed.", TestName = "AuthenticationFailedProse")]
	[TestCase("The remote server returned an error: 401.", TestName = "StandaloneStatusToken")]
	[TestCase("{\"responseStatus\":{\"ErrorCode\":\"5\",\"Message\":\"x\"}}", TestName = "DataServiceErrorCodeFive")]
	[Description("The message-only classifier returns Authentication only for a message that NAMES a credential outcome - a status token, the platform's prose, or DataService ErrorCode 5.")]
	public void ClassifyProviderErrorMessage_ShouldReturnAuthentication_ForACredentialNamingMessage(string message) {
		// Arrange
		// (the message is the whole input)

		// Act
		AuthenticationFailureClassifier.ProviderFailureVerdict verdict =
			AuthenticationFailureClassifier.ClassifyProviderErrorMessage(message);

		// Assert
		verdict.Should().Be(AuthenticationFailureClassifier.ProviderFailureVerdict.Authentication,
			because: "ATF's provider hands back only ErrorMessage, so a message that names the credential outcome is the strongest evidence available");
	}

	[Test]
	[Description("The HTML-where-JSON signal ALONE is not evidence of an authentication failure: an IIS/nginx 404 page, a WAF block and a gateway error page all produce the byte-identical Newtonsoft message.")]
	public void ClassifyProviderErrorMessage_ShouldReturnNonJsonPage_ForAnUncorroboratedParserFailure() {
		// Arrange
		const string parserFailure =
			"Unexpected character encountered while parsing value: <. Path '', line 0, position 0.";

		// Act
		AuthenticationFailureClassifier.ProviderFailureVerdict verdict =
			AuthenticationFailureClassifier.ClassifyProviderErrorMessage(parserFailure);

		// Assert
		verdict.Should().Be(AuthenticationFailureClassifier.ProviderFailureVerdict.NonJsonPage,
			because: "claiming the credentials were rejected would send the operator to repair a working login whenever the real cause was a proxy, a gateway or a wrong path");
	}

	[Test]
	[Description("A parser failure that DOES carry a corroborating credential marker is an authentication failure, so requiring corroboration did not switch the login-page signal off entirely.")]
	public void ClassifyProviderErrorMessage_ShouldReturnAuthentication_ForACorroboratedParserFailure() {
		// Arrange
		const string corroborated =
			"Unexpected character encountered while parsing value: <. The remote server returned an error: 401.";

		// Act
		AuthenticationFailureClassifier.ProviderFailureVerdict verdict =
			AuthenticationFailureClassifier.ClassifyProviderErrorMessage(corroborated);

		// Assert
		verdict.Should().Be(AuthenticationFailureClassifier.ProviderFailureVerdict.Authentication,
			because: "a status beside the parser prose removes the ambiguity that made the bare signal unusable");
	}

	[Test]
	[TestCase(null, TestName = "NullMessage")]
	[TestCase("", TestName = "EmptyMessage")]
	[TestCase("SqlException: deadlock victim", TestName = "GenericPlatformFailure")]
	[TestCase("Connection refused at http://localhost:40124", TestName = "PortContaining401")]
	[TestCase("Correlation id x401y", TestName = "IdentifierContaining401")]
	[TestCase("The remote certificate is invalid according to the validation procedure.",
		TestName = "CertificateFailure")]
	[TestCase("Column <Name> is required.", TestName = "AngleBracketInPlatformProse")]
	[Description("The message-only classifier stays narrow: an angle bracket, a port, an identifier and a certificate problem must not be reported as rejected credentials.")]
	public void ClassifyProviderErrorMessage_ShouldReturnNotAuthentication_ForANonCredentialMessage(string message) {
		// Arrange
		// (the message is the whole input)

		// Act
		AuthenticationFailureClassifier.ProviderFailureVerdict verdict =
			AuthenticationFailureClassifier.ClassifyProviderErrorMessage(message);

		// Assert
		verdict.Should().Be(AuthenticationFailureClassifier.ProviderFailureVerdict.NotAuthentication,
			because: "sending the operator to repair working credentials replaces the only diagnosis that leads to the fix");
	}

	[Test]
	[TestCase("<!DOCTYPE html><html><head><title>Creatio</title></head><body><form action=\"/Login/NuiLogin.aspx\"></form></body></html>",
		TestName = "NetFrameworkLoginPage")]
	[TestCase("{\"Message\":\"Authentication failed.\",\"StackTrace\":null}", TestName = "JsonAuthFaultEnvelope")]
	[TestCase("{\"responseStatus\":{\"ErrorCode\":\"5\",\"Message\":\"Your password has expired.\"},\"success\":false}",
		TestName = "DataServiceFaultEnvelope")]
	[Description("A caller that still holds the RAW response body gets a definite answer: the login-page markers and the fault envelopes are all recognizable in the body, which the message-only classifier never sees.")]
	public void IsAuthenticationFailureResponse_ShouldReturnTrue_ForARejectedSessionBody(string body) {
		// Arrange
		// (the body is the whole input)

		// Act
		bool result = AuthenticationFailureClassifier.IsAuthenticationFailureResponse(body);

		// Assert
		result.Should().BeTrue(
			because: "the write path posts through IApplicationClient and keeps the body, so an expired password there is provable rather than ambiguous");
	}

	[Test]
	[TestCase("<!DOCTYPE html><html><head><title>404 Not Found</title></head><body>nginx</body></html>",
		TestName = "GatewayNotFoundPage")]
	[TestCase("{\"rows\":[],\"success\":true}", TestName = "OrdinarySuccessEnvelope")]
	[TestCase(null, TestName = "NullBody")]
	[Description("A gateway error page is not a rejected session: the raw-body check must not treat any HTML as a login page, or a wrong URL would be reported as bad credentials.")]
	public void IsAuthenticationFailureResponse_ShouldReturnFalse_ForANonRejectionBody(string body) {
		// Arrange
		// (the body is the whole input)

		// Act
		bool result = AuthenticationFailureClassifier.IsAuthenticationFailureResponse(body);

		// Assert
		result.Should().BeFalse(
			because: "the body check keys off Creatio's auth-routing markers, which a 404 or proxy page does not carry");
	}

	[Test]
	[TestCase(true, TestName = "TypedStatusPresent")]
	[TestCase(false, TestName = "NoTypedStatus")]
	[Description("A typed HTTP status is detectable so a caller can refuse to fall back to prose matching when one is present.")]
	public void HasTypedStatus_ShouldReportWhetherAnAuthoritativeStatusIsPresent(bool typed) {
		// Arrange
		Exception exception = typed
			? new HttpRequestException("not found", null, HttpStatusCode.NotFound)
			: new HttpRequestException("Connection refused");

		// Act
		bool result = AuthenticationFailureClassifier.HasTypedStatus(exception);

		// Assert
		result.Should().Be(typed,
			because: "a typed 404 must not be overridden by its own prose, and a status-less transport fault has nothing but prose to go on");
	}

	[Test]
	[TestCase("Msg 1205, Level 13, State 5: Transaction (Process ID 62) was deadlocked on lock resources",
		TestName = "SqlDeadlockProse")]
	[TestCase("Unexpected token at line 5: '<'", TestName = "ParserErrorProse")]
	[Description("The composed ErrorCode-5 rendering is anchored at the start, so ordinary provider prose carrying a '5: ' mid-sentence is NOT promoted to an authentication diagnosis (PR #1372 review).")]
	public void ClassifyProviderErrorMessage_ShouldNotReportAuthentication_ForProseCarryingAFloatingFive(string message) {
		// Act
		AuthenticationFailureClassifier.ProviderFailureVerdict verdict = AuthenticationFailureClassifier.ClassifyProviderErrorMessage(message);

		// Assert
		verdict.Should().NotBe(AuthenticationFailureClassifier.ProviderFailureVerdict.Authentication,
			because: "a deadlock and a parser error are not rejected credentials, and the decorator puts this predicate on every command");
	}

	[Test]
	[TestCase("{\"responseStatus\":{\"ErrorCode\":\"50\",\"Message\":\"x\"}}", TestName = "QuotedFiftyErrorCode")]
	[TestCase("{\"responseStatus\":{\"ErrorCode\":500,\"Message\":\"x\"}}", TestName = "BareFiveHundredErrorCode")]
	[TestCase("{\"responseStatus\":{\"ErrorCode\":\"55\",\"Message\":\"x\"}}", TestName = "QuotedFiftyFiveErrorCode")]
	[Description("The JSON ErrorCode arm needs a digit boundary: without it the optional trailing quote lets any code merely STARTING with 5 match, so an ordinary server-side DataService failure in the 5x/5xx range was reported as rejected credentials (PR #1372 review).")]
	public void ClassifyProviderErrorMessage_ShouldNotReportAuthentication_ForAnErrorCodeThatMerelyStartsWithFive(string message) {
		// Act
		AuthenticationFailureClassifier.ProviderFailureVerdict verdict = AuthenticationFailureClassifier.ClassifyProviderErrorMessage(message);

		// Assert
		verdict.Should().NotBe(AuthenticationFailureClassifier.ProviderFailureVerdict.Authentication,
			because: "only ErrorCode 5 exactly is the platform's authentication-rejection code, and this predicate is the strongest authentication signal there is");
	}

	[Test]
	[TestCase("{\"responseStatus\":{\"ErrorCode\":\"5\",\"Message\":\"x\"}}", TestName = "QuotedFiveErrorCode")]
	[TestCase("{\"responseStatus\":{\"ErrorCode\":5,\"Message\":\"x\"}}", TestName = "BareFiveErrorCode")]
	[Description("The digit boundary must not cost the rendering the arm exists for: ErrorCode 5, quoted or bare, is still an authentication verdict (PR #1372 review).")]
	public void ClassifyProviderErrorMessage_ShouldReportAuthentication_ForErrorCodeFiveExactly(string message) {
		// Act
		AuthenticationFailureClassifier.ProviderFailureVerdict verdict = AuthenticationFailureClassifier.ClassifyProviderErrorMessage(message);

		// Assert
		verdict.Should().Be(AuthenticationFailureClassifier.ProviderFailureVerdict.Authentication,
			because: "tightening the pattern must not lose the code it was written to match");
	}

	[Test]
	[Description("ATF composes errorCode + \": \" + message, so the code leads the text and the anchored alternative still matches it (PR #1372 review).")]
	public void ClassifyProviderErrorMessage_ShouldReportAuthentication_ForTheComposedLeadingCode() {
		// Act
		AuthenticationFailureClassifier.ProviderFailureVerdict verdict =
			AuthenticationFailureClassifier.ClassifyProviderErrorMessage("5: Your password has expired.");

		// Assert
		verdict.Should().Be(AuthenticationFailureClassifier.ProviderFailureVerdict.Authentication,
			because: "anchoring the alternative must not lose the rendering it exists for");
	}

	[Test]
	[Description("A SUCCESSFUL DataService payload is never free-text scanned: a lookup row that happens to read 'Unauthorized' must not turn a write that landed into an authentication failure (PR #1372 review).")]
	public void IsAuthenticationFailureResponse_ShouldReturnFalse_ForASuccessfulPayloadCarryingAuthWords() {
		// Arrange
		const string body =
			"{\"rows\":[{\"Id\":\"e1\",\"Name\":\"Unauthorized users\",\"Note\":\"Authentication failed.\"}],\"success\":true}";

		// Act
		bool result = AuthenticationFailureClassifier.IsAuthenticationFailureResponse(body);

		// Assert
		result.Should().BeFalse(
			because: "on a write the caller cannot tell whether it landed, so a false authentication verdict on a successful response is the worst answer available");
	}

	[Test]
	[Description("HasTypedStatus unwraps the AggregateException the Creatio client's Task.Result wrapping produces, so it agrees with IsAuthenticationFailure about the same object (PR #1372 review).")]
	public void HasTypedStatus_ShouldSeeThrough_AnAggregateWrapper() {
		// Arrange
		Exception wrapped = new AggregateException(
			new HttpRequestException("not found", null, HttpStatusCode.NotFound));

		// Act & Assert
		AuthenticationFailureClassifier.HasTypedStatus(wrapped).Should().BeTrue(
			because: "a shallow match made the guard fall back to prose matching on a status it had already been told was authoritative");
	}

	[Test]
	[Description("The nested-inner walk is bounded and terminates on a plain chain that carries no typed status (PR #1372 review).")]
	public void HasTypedStatus_ShouldReturnFalse_ForAChainWithoutATypedStatus() {
		// Arrange
		Exception chain = new InvalidOperationException("outer", new AggregateException(new SocketException(61)));

		// Act & Assert
		AuthenticationFailureClassifier.HasTypedStatus(chain).Should().BeFalse(
			because: "unwrapping must not invent a status the chain does not carry");
	}
}
