using System;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Text.Json;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// The credential diagnosis has to survive the wrappers the Creatio client puts around a transport
/// fault, and a typed HTTP status has to be believed in both directions.
/// </summary>
[TestFixture]
[Property("Module", "Command")]
[Category("Unit")]
public sealed class SysSettingsErrorClassificationTests {

	private const string Operation = "reading sys-setting";
	private const string AuthenticationError = "Authentication error reading sys-setting.";
	private const string NetworkError = "Network error reading sys-setting.";
	private const string GenericFailure = "Failed reading sys-setting.";

	[Test]
	[Description("An AggregateException wrapping an AuthenticationException still reports rejected credentials, because Task.Result is how the client surfaces the fault.")]
	public void CategorizeError_Should_Report_Authentication_For_An_Aggregate_Wrapping_AuthenticationException() {
		AggregateException exception = new(new AuthenticationException("credentials were rejected"));

		string message = SysSettingsCommand.CategorizeError(exception, Operation);

		message.Should().Be(AuthenticationError,
			because: "switching on the outer type alone saw the wrapper and reported the generic failure");
	}

	[Test]
	[Description("An AggregateException wrapping a typed 401 reports rejected credentials rather than the generic failure.")]
	public void CategorizeError_Should_Report_Authentication_For_An_Aggregate_Wrapping_A_Typed401() {
		AggregateException exception = new(
			new HttpRequestException("request failed", null, HttpStatusCode.Unauthorized));

		string message = SysSettingsCommand.CategorizeError(exception, Operation);

		message.Should().Be(AuthenticationError);
	}

	[Test]
	[Description("A nested aggregate is unwrapped too, so a fault reached through two Task.Result boundaries keeps its diagnosis.")]
	public void CategorizeError_Should_Unwrap_A_Nested_Aggregate() {
		AggregateException exception = new(new AggregateException(
			new HttpRequestException("request failed", null, HttpStatusCode.Unauthorized)));

		string message = SysSettingsCommand.CategorizeError(exception, Operation);

		message.Should().Be(AuthenticationError);
	}

	[Test]
	[Description("An aggregate carrying several faults, one of them a credential rejection, is still reported as an authentication failure.")]
	public void CategorizeError_Should_Report_Authentication_For_A_MultiFault_Aggregate_Holding_A_Rejection() {
		AggregateException exception = new(
			new HttpRequestException("service unavailable", null, HttpStatusCode.ServiceUnavailable),
			new AuthenticationException("credentials were rejected"));

		string message = SysSettingsCommand.CategorizeError(exception, Operation);

		message.Should().Be(AuthenticationError,
			because: "no single inner represents a multi-fault aggregate, but a credential rejection among "
				+ "them is still the thing the operator has to act on");
	}

	[Test]
	[Description("An aggregate whose faults are all non-credential failures is not reported as an authentication error.")]
	public void CategorizeError_Should_Not_Report_Authentication_For_A_MultiFault_Aggregate_Without_A_Rejection() {
		AggregateException exception = new(
			new HttpRequestException("service unavailable", null, HttpStatusCode.ServiceUnavailable),
			new InvalidOperationException("nothing to do"));

		string message = SysSettingsCommand.CategorizeError(exception, Operation);

		message.Should().Be(GenericFailure);
	}

	[TestCase(HttpStatusCode.NotFound, TestName = "Typed404")]
	[TestCase(HttpStatusCode.InternalServerError, TestName = "Typed500")]
	[Description("A typed non-401 status is authoritative: the prose is not consulted, so a body mentioning a standalone 401 cannot turn a routing or server failure into rejected credentials.")]
	public void CategorizeError_Should_Trust_A_Typed_NonUnauthorized_Status_Over_The_Prose(HttpStatusCode status) {
		HttpRequestException exception = new(
			"upstream said 401 somewhere in its body", null, status);

		string message = SysSettingsCommand.CategorizeError(exception, Operation);

		message.Should().Be(NetworkError,
			because: "only 401 short-circuited, so a typed 404 or 500 fell through to the text match and "
				+ "sent the operator off to fix a working login");
	}

	[Test]
	[Description("A typed non-401 status is authoritative even when an inner exception is a genuine credential failure, because the response is what the server actually answered.")]
	public void CategorizeError_Should_Trust_A_Typed_NonUnauthorized_Status_Over_An_Inner_Rejection() {
		HttpRequestException exception = new(
			"not found", new AuthenticationException("unauthorized"), HttpStatusCode.NotFound);

		string message = SysSettingsCommand.CategorizeError(exception, Operation);

		message.Should().Be(NetworkError);
	}

	[Test]
	[Description("A transport that reports only in prose still yields the credential diagnosis, so removing the text match is not what the typed-status rule does.")]
	public void CategorizeError_Should_Still_Match_Prose_When_No_Typed_Status_Is_Present() {
		HttpRequestException exception = new("The remote server returned 401");

		string message = SysSettingsCommand.CategorizeError(exception, Operation);

		message.Should().Be(AuthenticationError);
	}

	[Test]
	[Description("A chain deeper than the bound terminates with an answer instead of walking without end.")]
	public void CategorizeError_Should_Terminate_On_A_Chain_Deeper_Than_The_Bound() {
		//Deeper than the 16-level bound the walk applies. A credential signal sitting past it is not
		//found - which is the point: the bound is what a hostile or malformed chain runs into.
		Exception exception = new AuthenticationException("unauthorized");
		for (int depth = 0; depth < 40; depth++) {
			exception = new AggregateException("wrapped", exception);
		}

		string message = SysSettingsCommand.CategorizeError(exception, Operation);

		message.Should().NotBeNullOrEmpty(
			because: "the walk is depth-bounded, so an arbitrarily deep chain returns an answer rather than hanging");
	}

	[Test]
	[Description("A TLS handshake failure - a status-less HttpRequestException wrapping an AuthenticationException - is a network error, because reporting it as rejected credentials hides the certificate diagnosis.")]
	public void CategorizeError_Should_Report_Network_For_A_Tls_HandshakeFailure() {
		HttpRequestException exception = new(
			"The SSL connection could not be established, see inner exception.",
			new AuthenticationException("The remote certificate is invalid according to the validation procedure."));

		string message = SysSettingsCommand.CategorizeError(exception, Operation);

		message.Should().Be(NetworkError,
			because: "AuthenticationException is the framework's TLS exception as well as its credential one, and the certificate is what needs fixing");
	}

	[Test]
	[Description("A bare TLS AuthenticationException is a network error too, so the diagnosis does not depend on which wrapper the transport happened to use.")]
	public void CategorizeError_Should_Report_Network_For_A_Bare_Tls_AuthenticationException() {
		AuthenticationException exception = new("The remote certificate is invalid according to the validation procedure.");

		string message = SysSettingsCommand.CategorizeError(exception, Operation);

		message.Should().Be(NetworkError);
	}

	[Test]
	[Description("A WebException carrying TrustFailure reports a network error: the status sits on the exception, not on a response, so nothing else in the chain can be read for it.")]
	public void CategorizeError_Should_Report_Network_For_A_WebException_TrustFailure() {
		WebException exception = new(
			"Could not establish trust relationship for the SSL/TLS secure channel.",
			innerException: null,
			WebExceptionStatus.TrustFailure,
			response: null);

		string message = SysSettingsCommand.CategorizeError(exception, Operation);

		message.Should().Be(NetworkError);
	}

	[Test]
	[Description("A domain credential rejection still reports an authentication error, so the TLS carve-out did not turn the classifier off for the case it exists to catch.")]
	public void CategorizeError_Should_Still_Report_Authentication_For_A_Credential_Rejection() {
		AuthenticationException exception = new("Creatio rejected the supplied credentials: the password has expired.");

		string message = SysSettingsCommand.CategorizeError(exception, Operation);

		message.Should().Be(AuthenticationError);
	}

	[Test]
	[Description("A typed 401 stays a credential failure even when its prose mentions a certificate, because a typed status is authoritative.")]
	public void CategorizeError_Should_Report_Authentication_For_A_Typed401_Mentioning_A_Certificate() {
		HttpRequestException exception = new(
			"Rejected while presenting the client certificate", null, HttpStatusCode.Unauthorized);

		string message = SysSettingsCommand.CategorizeError(exception, Operation);

		message.Should().Be(AuthenticationError);
	}

	[Test]
	[Description("A JsonException - what a proxy/gateway or 404 page raises when it reaches JsonSerializer.Deserialize on the write path - is reported as a non-JSON response naming the likely cause, not as the uncategorized \"Failed ...\" the operator used to get after the preflight probe was removed.")]
	public void CategorizeError_Should_Report_A_NonJson_Response_For_A_JsonException() {
		JsonException fault = new("'<' is an invalid start of a value. Path: $ | LineNumber: 0 | BytePositionInLine: 0.");

		string message = SysSettingsCommand.CategorizeError(fault, "creating sys-setting");

		message.Should().Be(
			"Creatio returned a non-JSON response creating sys-setting - the URL may not reach Creatio, "
			+ "or a proxy/gateway answered instead of it.",
			because: "the write path's only remaining diagnosis for a non-login-page body must name the cause the operator can act on");
	}

	[Test]
	[Description("The JsonException arm is reached through the transport wrapper too: the Creatio client surfaces faults via Task.Result, so an AggregateException carrying a single JsonException must classify as the same non-JSON response.")]
	public void CategorizeError_Should_Report_A_NonJson_Response_For_An_Aggregate_Wrapping_A_JsonException() {
		AggregateException fault = new(new JsonException("'<' is an invalid start of a value."));

		string message = SysSettingsCommand.CategorizeError(fault, "updating sys-setting");

		message.Should().Contain("non-JSON response updating sys-setting",
			because: "unwrapping a single-fault aggregate must not lose the non-JSON diagnosis");
	}
}
