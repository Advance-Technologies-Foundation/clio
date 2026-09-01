using System;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
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
}
