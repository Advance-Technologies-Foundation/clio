using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests;

[TestFixture]
[Property("Module", "Core")]
[Category("Unit")]
public class ExceptionReadableMessageExtensionTestCase
{
	[Test]
	public void GetReadableMessageException_PrintsCorrectMessage_WhenFileNotFoundException() {
		var exception = new FileNotFoundException("FileMessage", "FileName");
		var messageResult = exception.GetReadableMessageException();
		messageResult.Should().Be($"{exception.Message}{exception.FileName}");
	}

	[Test]
	public void GetReadableMessageException_PrintsCorrectMessage_WhenFileNotFoundExceptionAndSetsDebug() {
		var exception = new FileNotFoundException("FileMessage", "FileName");
		var messageResult = exception.GetReadableMessageException(true);
		messageResult.Should().Be(exception.ToString());
	}

	[Test]
	public void GetReadableMessageException_PrintsCorrectMessage_WhenException() {
		var exception = new Exception("Message");
		var messageResult = exception.GetReadableMessageException();
		messageResult.Should().Be($"{exception.Message}");
	}

	[Test]
	public void GetReadableMessageException_PrintsCorrectMessage_WhenExceptionAndSetsDebug() {
		var exception = new Exception("Message");
		var messageResult = exception.GetReadableMessageException(true);
		messageResult.Should().Be(exception.ToString());
	}

	[Test]
	public void GetReadableMessageException_PrintsCorrectMessage_WhenInvalidOperationException() {
		var innerException = new Exception("InnerMessage");
		var exception = new InvalidOperationException("Message", innerException);
		var messageResult = exception.GetReadableMessageException();
		messageResult.Should().Be($"{innerException.Message}");
	}

	[Test]
	public void
		GetReadableMessageException_PrintsCorrectMessage_WhenInvalidOperationExceptionWithoutInnerException() {
		var exception = new InvalidOperationException("Message");
		var messageResult = exception.GetReadableMessageException();
		messageResult.Should().Be($"{exception.Message}");
	}

	[Test]
	public void GetReadableMessageException_PrintsCorrectMessage_WhenInvalidOperationExceptionAndSetsDebug() {
		var innerException = new Exception("InnerMessage");
		var exception = new InvalidOperationException("Message", innerException);
		var messageResult = exception.GetReadableMessageException(true);
		messageResult.Should().Be(exception.ToString());
	}

	[Test]
	public void GetReadableMessageException_UnwrapsInnerException_WhenAggregateException() {
		var inner = new WebException("Connection refused (localhost:1616)", WebExceptionStatus.ConnectFailure);
		var exception = new AggregateException(inner);
		var result = exception.GetReadableMessageException();
		result.Should().StartWith("Cannot connect to the application:");
		result.Should().Contain("Make sure the site is running and accessible");
	}

	[Test]
	public void GetReadableMessageException_PrintsFullStackTrace_WhenAggregateExceptionAndDebugMode() {
		AggregateException exception;
		try { throw new AggregateException(new WebException("Connection refused", WebExceptionStatus.ConnectFailure)); }
		catch (AggregateException e) { exception = e; }
		var result = exception.GetReadableMessageException(debug: true);
		result.Should().Be(exception.ToString());
	}

	[Test]
	public void GetReadableMessageException_PrintsFriendlyMessage_WhenWebExceptionConnectFailure() {
		var exception = new WebException("Connection refused (localhost:1616)", WebExceptionStatus.ConnectFailure);
		var result = exception.GetReadableMessageException();
		result.Should().StartWith("Cannot connect to the application:");
		result.Should().Contain("Make sure the site is running and accessible");
		result.Should().NotContain("   at ");
	}

	[Test]
	public void GetReadableMessageException_PrintsFullStackTrace_WhenWebExceptionConnectFailureAndDebugMode() {
		WebException exception;
		try { throw new WebException("Connection refused (localhost:1616)", WebExceptionStatus.ConnectFailure); }
		catch (WebException e) { exception = e; }
		var result = exception.GetReadableMessageException(debug: true);
		result.Should().Be(exception.ToString());
	}

	[Test]
	public void GetReadableMessageException_PrintsMessageAndStatus_WhenWebExceptionOtherStatus() {
		var exception = new WebException("Not Found", WebExceptionStatus.ProtocolError);
		var result = exception.GetReadableMessageException();
		result.Should().StartWith(exception.Message);
		result.Should().Contain("WebException: ProtocolError");
		result.Should().NotContain("Cannot connect to the application");
	}

	[Test]
	[Description(
		"A WebException whose Status is ProtocolError and whose Response is an HttpWebResponse 401 "
		+ "must surface the HTTP status code and reason in the non-debug readable message so CI can "
		+ "tell an auth failure apart from a connect/timeout failure.")]
	public void GetReadableMessageException_ShouldIncludeHttpStatusCodeAndStatus_WhenWebExceptionIsProtocolError401() {
		// Arrange
		using HttpWebResponse response = CreateHttpWebResponse(HttpStatusCode.Unauthorized);
		var exception = new WebException(
			"The remote server returned an error: (401) Unauthorized.",
			null,
			WebExceptionStatus.ProtocolError,
			response);

		// Act
		string result = exception.GetReadableMessageException();

		// Assert
		result.Should().Contain("401",
			because: "the numeric HTTP status code must be visible so a 401 is unambiguous in CI logs");
		result.Should().Contain("Unauthorized",
			because: "the HTTP status reason must accompany the code for human-readable diagnosis");
		result.Should().Contain("ProtocolError",
			because: "the WebException.Status must be reported alongside the HTTP code");
		result.Should().NotContain("   at ",
			because: "non-debug output must stay free of stack-trace noise");
	}

	[Test]
	[Description(
		"A WebException whose Status is ConnectFailure (no HTTP response) must keep the friendly "
		+ "connect guidance while also reporting its WebExceptionStatus, so a warm-up/connect failure "
		+ "is distinguishable from an auth failure in non-debug output.")]
	public void GetReadableMessageException_ShouldIncludeConnectFailureStatus_WhenWebExceptionIsConnectFailure() {
		// Arrange
		var exception = new WebException("Unable to connect to the remote server", WebExceptionStatus.ConnectFailure);

		// Act
		string result = exception.GetReadableMessageException();

		// Assert
		result.Should().StartWith("Cannot connect to the application:",
			because: "the friendly connect guidance must be preserved for connect failures");
		result.Should().Contain("WebException: ConnectFailure",
			because: "the WebExceptionStatus must be reported so the failure mode is explicit");
		result.Should().Contain("Make sure the site is running and accessible",
			because: "the actionable hint for connect failures must remain in the message");
		result.Should().NotContain("HTTP ",
			because: "a connect failure has no HTTP response, so no HTTP status code should be invented");
	}

	[Test]
	[Description(
		"When a WebException is wrapped inside another exception type, the readable message must still "
		+ "surface the inner WebException status and HTTP code rather than only the outer message.")]
	public void GetReadableMessageException_ShouldSurfaceInnerWebExceptionStatus_WhenWebExceptionIsNested() {
		// Arrange
		using HttpWebResponse response = CreateHttpWebResponse(HttpStatusCode.Unauthorized);
		var inner = new WebException(
			"The remote server returned an error: (401) Unauthorized.",
			null,
			WebExceptionStatus.ProtocolError,
			response);
		var exception = new ApplicationException("Upload failed", inner);

		// Act
		string result = exception.GetReadableMessageException();

		// Assert
		result.Should().StartWith("Upload failed",
			because: "the outer exception message must lead so the operation context is preserved");
		result.Should().Contain("401",
			because: "the wrapped WebException HTTP status code must still be visible in the readable message");
		result.Should().Contain("ProtocolError",
			because: "the wrapped WebException status must still be reported when nested");
	}

	[Test]
	[Description(
		"An InvalidOperationException whose inner is a WebException must surface the inner WebException "
		+ "status and HTTP code: the InvalidOperationException arm must no longer shadow the WebException "
		+ "enrichment, otherwise a login fault rewrapped as IOE would lose the 401-vs-connect signal.")]
	public void GetReadableMessageException_ShouldSurfaceInnerWebExceptionStatus_WhenInvalidOperationExceptionWrapsWebException() {
		// Arrange
		using HttpWebResponse response = CreateHttpWebResponse(HttpStatusCode.Unauthorized);
		var inner = new WebException(
			"The remote server returned an error: (401) Unauthorized.",
			null,
			WebExceptionStatus.ProtocolError,
			response);
		var exception = new InvalidOperationException("Operation failed", inner);

		// Act
		string result = exception.GetReadableMessageException();

		// Assert
		result.Should().StartWith("Operation failed",
			because: "the outer InvalidOperationException message must lead so the operation context is preserved");
		result.Should().Contain("401",
			because: "the wrapped WebException HTTP status code must survive the InvalidOperationException arm");
		result.Should().Contain("ProtocolError",
			because: "the wrapped WebException status must be reported even when the wrapper is an InvalidOperationException");
	}

	[Test]
	[Description(
		"Issue #1505: the SelectQuery failure path (SelectQueryHelper / DataServiceSelectResponse) throws a "
		+ "plain InvalidOperationException carrying the server's errorInfo.message, so the credential the "
		+ "server echoed must be redacted while clio's own 'SelectQuery failed:' prefix survives.")]
	public void GetReadableMessageException_ShouldRedactServerCredentials_WhenSelectQueryFailureIsRendered() {
		// Arrange
		var exception = new InvalidOperationException(
			"SelectQuery failed: Configuration service failed: backend echoed "
			+ "password=s3cr3t server=db.internal");

		// Act
		string result = exception.GetReadableMessageException();

		// Assert
		result.Should().StartWith("SelectQuery failed: Configuration service failed:",
			because: "clio's own diagnosis must survive redaction, otherwise the operator loses the reason");
		result.Should().NotContain("s3cr3t",
			because: "a credential the server echoed must never reach the console in the clear");
		result.Should().NotContain("db.internal",
			because: "the connection-string host the server echoed must be redacted like the password");
		result.Should().Contain("password=[redacted]",
			because: "the key is kept so the line still reads sensibly while the value is replaced");
		result.Should().Contain("server=[redacted]",
			because: "the same placeholder policy as the MCP path (ClioRunTool.RedactFailureContent) applies");
	}

	[Test]
	[Description(
		"Issue #1505: the InvalidOperationException arm prefers the inner message, so a bearer token in an "
		+ "INNER exception must be redacted too - the arm that is actually taken is the one that has to scrub.")]
	public void GetReadableMessageException_ShouldRedactBearerToken_WhenInvalidOperationExceptionWrapsInner() {
		// Arrange
		var inner = new Exception("Request rejected with Authorization: Bearer eyJhbGciOi.eyJzdWIiOi.c2lnbmF0dXJl");
		var exception = new InvalidOperationException("SelectQuery failed", inner);

		// Act
		string result = exception.GetReadableMessageException();

		// Assert
		result.Should().NotContain("eyJhbGciOi",
			because: "a JWT surfaced by an inner exception must not reach the console in the clear");
		result.Should().Contain("[redacted]",
			because: "the token has to be replaced by the redactor's stable placeholder");
		result.Should().StartWith("Request rejected with",
			because: "redaction must not change which arm is taken nor the surrounding prose");
	}

	[Test]
	[Description(
		"Issue #1505: redaction is applied to the COMPOSED line, so the WebException enrichment that CI reads "
		+ "must still be appended while a credential inside the exception message is replaced.")]
	public void GetReadableMessageException_ShouldKeepWebExceptionEnrichment_WhenMessageCarriesCredential() {
		// Arrange
		using HttpWebResponse response = CreateHttpWebResponse(HttpStatusCode.Unauthorized);
		var exception = new WebException(
			"The remote server rejected password=s3cr3t",
			null,
			WebExceptionStatus.ProtocolError,
			response);

		// Act
		string result = exception.GetReadableMessageException();

		// Assert
		result.Should().NotContain("s3cr3t",
			because: "the credential must be redacted even on the WebException arm");
		result.Should().Contain("(WebException: ProtocolError (HTTP 401 Unauthorized))",
			because: "the enrichment arm's structure must survive the redaction wrapper unchanged");
	}

	[Test]
	[Description(
		"Issue #1505: the debug path must stay exception.ToString() verbatim - --debug is the bridge back to "
		+ "the raw server text, so redaction must not be applied there.")]
	public void GetReadableMessageException_ShouldNotRedact_WhenDebugIsRequested() {
		// Arrange
		var exception = new InvalidOperationException(
			"SelectQuery failed: backend echoed password=s3cr3t server=db.internal");

		// Act
		string result = exception.GetReadableMessageException(true);

		// Assert
		result.Should().Be(exception.ToString(),
			because: "the debug render is unchanged by this issue and must remain the raw ToString()");
		result.Should().Contain("password=s3cr3t",
			because: "--debug deliberately keeps the raw text so an operator can diagnose the fault");
	}

	[Test]
	[Description(
		"Issue #1505: a message with no secret shapes must come back byte-identical - Scrub replaces known "
		+ "secret shapes only, and does not flatten or clamp clio's own multi-line prose.")]
	public void GetReadableMessageException_ShouldReturnMessageUnchanged_WhenNothingIsSensitive() {
		// Arrange
		string message = "Package 'MyPackage' was not found." + Environment.NewLine
			+ "Run list-packages to see what is installed. " + new string('x', 400);
		var exception = new InvalidOperationException(message);

		// Act
		string result = exception.GetReadableMessageException();

		// Assert
		result.Should().Be(message,
			because: "a secret-free line must not be altered, re-wrapped or truncated by the redaction wrapper");
	}

	[Test]
	[Description(
		"Issue #1505 follow-up: a local absolute path in clio's OWN prose must survive verbatim - the full "
		+ "Redact turned `clio compress <missing dir>` into \"Could not find a part of the path "
		+ "'[redacted-path]'.\", an error that names nothing.")]
	public void GetReadableMessageException_ShouldKeepLocalPath_WhenMessageQuotesAnAbsolutePath() {
		// Arrange
		const string message = "Could not find a part of the path '/Users/x/y.json'.";
		var exception = new InvalidOperationException(message);

		// Act
		string result = exception.GetReadableMessageException();

		// Assert
		result.Should().Be(message,
			because: "a path on the operator's own terminal is the diagnosis, not a secret to hide from them");
	}

	[Test]
	[Description(
		"Issue #1505 follow-up: the operator's own environment URL and host:port must survive verbatim, so a "
		+ "connectivity error still names the environment that could not be reached.")]
	public void GetReadableMessageException_ShouldKeepEnvironmentUrl_WhenMessageQuotesAnEndpoint() {
		// Arrange
		const string message = "Cannot connect to http://ts1-core-dev04:88/site";
		var exception = new InvalidOperationException(message);

		// Act
		string result = exception.GetReadableMessageException();

		// Assert
		result.Should().Be(message,
			because: "a credential-free URL naming the operator's own stand must not be replaced by a placeholder");
	}

	[Test]
	[Description(
		"Issue #1505 follow-up: a credential embedded in a URL authority is still a credential - the userinfo "
		+ "is removed while the host survives, so the line stays diagnosable.")]
	public void GetReadableMessageException_ShouldRedactUriUserInfoOnly_WhenUrlCarriesCredentials() {
		// Arrange
		var exception = new InvalidOperationException("Request to https://user:pw@host.example.com/x failed");

		// Act
		string result = exception.GetReadableMessageException();

		// Assert
		result.Should().NotContain("user:pw",
			because: "credentials embedded in a URL authority must never reach the console in the clear");
		result.Should().Contain("host.example.com/x",
			because: "the host and path must survive so the operator still knows which endpoint failed");
		result.Should().Contain("[redacted]@",
			because: "only the userinfo prefix is replaced, by the redactor's stable placeholder");
	}

	/// <summary>
	/// Builds a real <see cref="HttpWebResponse"/> carrying the supplied status code without opening a
	/// socket. On modern .NET <see cref="HttpWebResponse"/> has no usable public constructor and its
	/// <see cref="HttpWebResponse.StatusCode"/> delegates to a private backing
	/// <see cref="HttpResponseMessage"/>, so the test allocates an uninitialised instance and assigns
	/// that backing field a real <see cref="HttpResponseMessage"/> with the desired status. This is
	/// runtime/OS-agnostic and does not rely on any platform networking stack.
	/// </summary>
	private static HttpWebResponse CreateHttpWebResponse(HttpStatusCode statusCode) {
		var response = (HttpWebResponse)RuntimeHelpers.GetUninitializedObject(typeof(HttpWebResponse));
		FieldInfo backingField = typeof(HttpWebResponse).GetField(
			"_httpResponseMessage", BindingFlags.Instance | BindingFlags.NonPublic);
		backingField.Should().NotBeNull(
			because: "the test must be able to set the HttpWebResponse backing message on this runtime");
		backingField!.SetValue(response, new HttpResponseMessage(statusCode));
		return response;
	}
}
