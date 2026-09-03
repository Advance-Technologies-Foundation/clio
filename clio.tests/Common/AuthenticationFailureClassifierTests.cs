using System;
using System.Net;
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
	[TestCase("Unexpected character encountered while parsing value: <. Path '', line 0, position 0.",
		TestName = "LoginPageArrivesAsAParserError")]
	[TestCase("5: Your password has expired.", TestName = "ExpiredPasswordProse")]
	[TestCase("Authentication failed.", TestName = "AuthenticationFailedProse")]
	[TestCase("The remote server returned an error: 401.", TestName = "StandaloneStatusToken")]
	[Description("The message-only overload classifies the shapes ATF.Repository reports, which is all that survives when the provider swallows the exception into ErrorMessage.")]
	public void IsAuthenticationFailure_ShouldReturnTrue_ForACredentialNamingMessage(string message) {
		// Arrange
		// (the message is the whole input)

		// Act
		bool result = AuthenticationFailureClassifier.IsAuthenticationFailure(message);

		// Assert
		result.Should().BeTrue(
			because: "ATF's provider hands back only ErrorMessage, so the message alone has to carry the credential diagnosis");
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
	[Description("The message-only overload stays narrow: an angle bracket, a port, an identifier and a certificate problem must not be reported as rejected credentials.")]
	public void IsAuthenticationFailure_ShouldReturnFalse_ForANonCredentialMessage(string message) {
		// Arrange
		// (the message is the whole input)

		// Act
		bool result = AuthenticationFailureClassifier.IsAuthenticationFailure(message);

		// Assert
		result.Should().BeFalse(
			because: "sending the operator to repair working credentials replaces the only diagnosis that leads to the fix");
	}
}
