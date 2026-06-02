using System;
using System.Threading;
using Clio.Common;
using Clio.Common.Responses;
using Creatio.Client;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
internal class CreatioClientAdapterReauthTests {

	#region Methods: Private

	private const string LoginPageBody =
		"<!DOCTYPE html><html><head><title>Login</title></head>" +
		"<body><form action=\"/Login/NuiLogin.aspx\">" +
		"<input id=\"LoginEdit\" name=\"UserName\"/></form></body></html>";

	private sealed record class CapturedExecute {

		public Func<string> Call { get; set; }
		public Func<string, bool> Predicate { get; set; }
	}

	private static CreatioClientAdapter CreateAdapter(IReauthExecutor executor) {
		// The Lazy is intentionally never resolved: the substituted executor never invokes
		// the wrapped callback, so Client is never dereferenced inside the adapter.
		Lazy<CreatioClient> lazyClient = new(() => null);
		return new CreatioClientAdapter(lazyClient, executor);
	}

	private static (CreatioClientAdapter Adapter, CapturedExecute Captured) CreateAdapterWithCapture(
		string canned) {
		CapturedExecute captured = new();
		IReauthExecutor executor = Substitute.For<IReauthExecutor>();
		executor.Execute(Arg.Any<Func<string>>(), Arg.Any<Func<string, bool>>())
			.Returns(ci => {
				captured.Call = ci.Arg<Func<string>>();
				captured.Predicate = ci.Arg<Func<string, bool>>();
				return canned;
			});
		return (CreateAdapter(executor), captured);
	}

	#endregion

	#region Tests: ExecuteDeleteRequest

	[Test]
	[Description("ExecuteDeleteRequest routes through IReauthExecutor with the HTML login-page predicate")]
	public void ExecuteDeleteRequest_ShouldRouteThroughReauthExecutorWithLoginPagePredicate_WhenInvoked() {
		// Arrange
		(CreatioClientAdapter adapter, CapturedExecute captured) = CreateAdapterWithCapture("{}");

		// Act
		string result = adapter.ExecuteDeleteRequest("/x", "data");

		// Assert
		result.Should().Be("{}",
			because: "the adapter must return what the reauth executor produced, unchanged");
		captured.Predicate.Should().NotBeNull(
			because: "the adapter must hand a predicate to the executor for unauthorized detection");
		captured.Predicate(LoginPageBody).Should().BeTrue(
			because: "the predicate must classify Creatio login HTML as an expired session");
		captured.Predicate("{\"ok\":true}").Should().BeFalse(
			because: "the predicate must accept JSON payloads as healthy responses");
	}

	#endregion

	#region Tests: ExecuteGetRequest

	[Test]
	[Description("ExecuteGetRequest routes through IReauthExecutor with the HTML login-page predicate")]
	public void ExecuteGetRequest_ShouldRouteThroughReauthExecutorWithLoginPagePredicate_WhenInvoked() {
		// Arrange
		(CreatioClientAdapter adapter, CapturedExecute captured) = CreateAdapterWithCapture("{\"a\":1}");

		// Act
		string result = adapter.ExecuteGetRequest("/x");

		// Assert
		result.Should().Be("{\"a\":1}",
			because: "the adapter must return what the reauth executor produced, unchanged");
		captured.Predicate.Should().NotBeNull(
			because: "the adapter must hand the predicate to the executor");
		captured.Predicate(LoginPageBody).Should().BeTrue(
			because: "the predicate must classify Creatio login HTML as an expired session");
	}

	#endregion

	#region Tests: ExecutePostRequest

	[Test]
	[Description("ExecutePostRequest routes through IReauthExecutor with the HTML login-page predicate")]
	public void ExecutePostRequest_ShouldRouteThroughReauthExecutorWithLoginPagePredicate_WhenInvoked() {
		// Arrange
		(CreatioClientAdapter adapter, CapturedExecute captured) = CreateAdapterWithCapture("{\"ok\":true}");

		// Act
		string result = adapter.ExecutePostRequest("/x", "data");

		// Assert
		result.Should().Be("{\"ok\":true}",
			because: "the adapter must return what the reauth executor produced, unchanged");
		captured.Predicate.Should().NotBeNull(
			because: "the adapter must hand the predicate to the executor");
		captured.Predicate(LoginPageBody).Should().BeTrue(
			because: "the predicate must classify Creatio login HTML as an expired session");
	}

	#endregion

	#region Tests: ExecutePostRequest<T>

	[Test]
	[Description("Generic ExecutePostRequest deserializes the executor result and never feeds HTML into the JSON converter")]
	public void ExecutePostRequestGeneric_ShouldDeserializeExecutorResult_WhenExecutorReturnsValidJson() {
		// Arrange — simulate a successful retry: executor handled the reauth and returned JSON.
		IReauthExecutor executor = Substitute.For<IReauthExecutor>();
		executor.Execute(Arg.Any<Func<string>>(), Arg.Any<Func<string, bool>>())
			.Returns("{\"success\":true}");
		CreatioClientAdapter adapter = CreateAdapter(executor);

		// Act
		BaseResponse response = adapter.ExecutePostRequest<BaseResponse>("/x", "data");

		// Assert
		response.Should().NotBeNull(because: "the adapter must deserialize the executor's JSON payload");
		response.Success.Should().BeTrue(
			because: "the deserialized JSON had \"success\":true, so the parsed property must reflect it");
	}

	[Test]
	[Description("Generic ExecutePostRequest hands the login-page predicate to the executor so HTML can be detected before JSON deserialization")]
	public void ExecutePostRequestGeneric_ShouldPassLoginPagePredicateToExecutor_WhenInvoked() {
		// Arrange
		(CreatioClientAdapter adapter, CapturedExecute captured) = CreateAdapterWithCapture("{\"success\":false}");

		// Act
		adapter.ExecutePostRequest<BaseResponse>("/x", "data");

		// Assert
		captured.Predicate.Should().NotBeNull(
			because: "without a predicate the executor cannot detect an expired session before JSON parsing");
		captured.Predicate(LoginPageBody).Should().BeTrue(
			because: "the predicate must classify Creatio login HTML so HTML never reaches the JSON converter");
		captured.Predicate("{\"success\":true}").Should().BeFalse(
			because: "the predicate must not flag valid JSON responses as expired sessions");
	}

	#endregion

	#region Tests: ExecutePatchRequest

	[Test]
	[Description("ExecutePatchRequest routes through IReauthExecutor with the HTML login-page predicate")]
	public void ExecutePatchRequest_ShouldRouteThroughReauthExecutorWithLoginPagePredicate_WhenInvoked() {
		// Arrange
		(CreatioClientAdapter adapter, CapturedExecute captured) = CreateAdapterWithCapture("{}");

		// Act
		string result = adapter.ExecutePatchRequest("/x", "data");

		// Assert
		result.Should().Be("{}",
			because: "the adapter must return what the reauth executor produced, unchanged");
		captured.Predicate.Should().NotBeNull(
			because: "the adapter must hand the predicate to the executor");
		captured.Predicate(LoginPageBody).Should().BeTrue(
			because: "the predicate must classify Creatio login HTML as an expired session");
	}

	#endregion

	#region Tests: End-to-end reauth via real ReauthExecutor

	[Test]
	[Description("Adapter end-to-end: stale cookie returns login page, ReauthExecutor performs Login and retry; final JSON reaches the caller")]
	public void Adapter_ShouldReauthenticateAndRetry_WhenFirstHttpResponseIsLoginPage() {
		// Arrange — wire a real ReauthExecutor whose callback simulates Creatio:
		// first call after expiry returns HTML, subsequent calls after Login return JSON.
		int loginCalls = 0;
		bool authenticated = false;
		string ServerCall() => authenticated ? "{\"success\":true}" : LoginPageBody;
		void Login() {
			Interlocked.Increment(ref loginCalls);
			authenticated = true;
		}
		ReauthExecutor real = new(Login);
		// Build an adapter routed through a thin executor that defers to the real one and
		// hard-codes the wrapped callback so we exercise the full Execute -> isUnauthorized
		// -> Login -> retry path without touching the NuGet CreatioClient.
		IReauthExecutor passthrough = Substitute.For<IReauthExecutor>();
		passthrough.Execute(Arg.Any<Func<string>>(), Arg.Any<Func<string, bool>>())
			.Returns(ci => real.Execute(ServerCall, ci.Arg<Func<string, bool>>()));
		CreatioClientAdapter adapter = CreateAdapter(passthrough);

		// Act
		string result = adapter.ExecutePostRequest("/x", "data");

		// Assert
		result.Should().Be("{\"success\":true}",
			because: "after a single reauth + retry the adapter must surface the final JSON payload");
		loginCalls.Should().Be(1,
			because: "ReauthExecutor must perform exactly one Login when the first response is the login page");
	}

	#endregion

}
