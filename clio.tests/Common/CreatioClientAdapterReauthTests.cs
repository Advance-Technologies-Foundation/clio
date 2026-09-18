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

	// .NET Framework kick-out: rendered NuiLogin.aspx page; trips Gate 2 via the `/Login/`
	// substring in the form action.
	private const string NetFrameworkLoginPageBody =
		"<!DOCTYPE html><html><head><title>Login</title></head>" +
		"<body><form action=\"/0/Login/NuiLogin.aspx\">" +
		"<input/></form></body></html>";

	// .NET Core kick-out: auto-followed Login.html JS-bootstrap shell whose `<title>` is the
	// generic "Creatio" and which carries no `/Login/` literal in its body. Trips Gate 2
	// via the `"bootstrap.login"` loader-id substring.
	private const string NetCoreLoginShellBody =
		"<!DOCTYPE html><html lang=\"en\" culture=\"en-US\"><head><title>Creatio</title>" +
		"<script src=\"/core/hash/Terrasoft/amd/bootstrap-loader.js\" data-loadbootstrap=\"bootstrap.login\"" +
		" data-baseurl=\"http://host\"></script></head><body></body></html>";

	// Default representative kick-out body for tests that just need *any* session-expired
	// response — the .NET Framework shape is the original ENG-90393 trigger and the more
	// readable of the two.
	private const string LoginPageBody = NetFrameworkLoginPageBody;

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
		executor.Execute(Arg.Any<Func<string>>(), Arg.Any<Func<string, bool>>(), Arg.Any<bool>())
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

	#region Tests: ExecutePutRequest

	[Test]
	[Description("ExecutePutRequest routes through IReauthExecutor with the HTML login-page predicate")]
	public void ExecutePutRequest_ShouldRouteThroughReauthExecutorWithLoginPagePredicate_WhenInvoked() {
		// Arrange
		(CreatioClientAdapter adapter, CapturedExecute captured) = CreateAdapterWithCapture("{}");

		// Act
		string result = adapter.ExecutePutRequest("/x", "data");

		// Assert
		result.Should().Be("{}",
			because: "the adapter must return what the reauth executor produced, unchanged");
		captured.Predicate.Should().NotBeNull(
			because: "the adapter must hand the predicate to the executor");
		captured.Predicate(LoginPageBody).Should().BeTrue(
			because: "the predicate must classify Creatio login HTML as an expired session");
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
		executor.Execute(Arg.Any<Func<string>>(), Arg.Any<Func<string, bool>>(), Arg.Any<bool>())
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
	[Description("Generic ExecutePostRequest throws a clear exception (not JsonException) when the retry response is still a session-expired HTML page")]
	public void ExecutePostRequestGeneric_ShouldThrowInvalidOperation_WhenRetryStillReturnsSessionExpiredHtml() {
		// Arrange — simulate the unrecoverable case: ReauthExecutor's retry also produced
		// the login HTML page (e.g. the Login() call itself failed to refresh the session).
		// Without the explicit check, JsonConverter.DeserializeObject<T> would throw the
		// opaque JsonException ("Invalid response format") that triggered ENG-90393 in the
		// first place.
		IReauthExecutor executor = Substitute.For<IReauthExecutor>();
		executor.Execute(Arg.Any<Func<string>>(), Arg.Any<Func<string, bool>>(), Arg.Any<bool>())
			.Returns(LoginPageBody);
		CreatioClientAdapter adapter = CreateAdapter(executor);

		// Act
		Action act = () => adapter.ExecutePostRequest<BaseResponse>("/x", "data");

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "the unrecoverable session-expired case must surface as a clear auth error, not as an opaque JSON parsing failure that hides the root cause from the operator")
			.WithMessage("*session expired*and*re-authentication*");
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

	#region Tests: CallConfigurationService + Upload* routed through reauth

	[Test]
	[Description("CallConfigurationService routes through IReauthExecutor with the session-expired predicate — covers the long-running path most likely to hit session expiry")]
	public void CallConfigurationService_ShouldRouteThroughReauthExecutorWithSessionExpiredPredicate_WhenInvoked() {
		// Arrange
		(CreatioClientAdapter adapter, CapturedExecute captured) = CreateAdapterWithCapture("{\"ok\":true}");

		// Act
		string result = adapter.CallConfigurationService("Svc", "Method", "{}");

		// Assert
		result.Should().Be("{\"ok\":true}",
			because: "the adapter must return what the reauth executor produced, unchanged");
		captured.Predicate.Should().NotBeNull(
			because: "without a predicate the long-running CallConfigurationService cannot recover from a mid-call session expiry");
		captured.Predicate(LoginPageBody).Should().BeTrue(
			because: "the predicate must classify Creatio login HTML as an expired session");
	}

	[Test]
	[Description("UploadFile routes through IReauthExecutor with the session-expired predicate")]
	public void UploadFile_ShouldRouteThroughReauthExecutorWithSessionExpiredPredicate_WhenInvoked() {
		// Arrange
		(CreatioClientAdapter adapter, CapturedExecute captured) = CreateAdapterWithCapture("{}");

		// Act
		string result = adapter.UploadFile("/x", "/tmp/file");

		// Assert
		result.Should().Be("{}",
			because: "the adapter must return what the reauth executor produced, unchanged");
		captured.Predicate(LoginPageBody).Should().BeTrue(
			because: "the predicate must classify Creatio login HTML as an expired session");
	}

	[Test]
	[Description("UploadAlmFile routes through IReauthExecutor with the session-expired predicate")]
	public void UploadAlmFile_ShouldRouteThroughReauthExecutorWithSessionExpiredPredicate_WhenInvoked() {
		// Arrange
		(CreatioClientAdapter adapter, CapturedExecute captured) = CreateAdapterWithCapture("{}");

		// Act
		string result = adapter.UploadAlmFile("/x", "/tmp/file");

		// Assert
		result.Should().Be("{}",
			because: "the adapter must return what the reauth executor produced, unchanged");
		captured.Predicate(LoginPageBody).Should().BeTrue(
			because: "the predicate must classify Creatio login HTML as an expired session");
	}

	[Test]
	[Description("UploadAlmFileByChunk routes through IReauthExecutor with the session-expired predicate")]
	public void UploadAlmFileByChunk_ShouldRouteThroughReauthExecutorWithSessionExpiredPredicate_WhenInvoked() {
		// Arrange
		(CreatioClientAdapter adapter, CapturedExecute captured) = CreateAdapterWithCapture("{}");

		// Act
		string result = adapter.UploadAlmFileByChunk("/x", "/tmp/file");

		// Assert
		result.Should().Be("{}",
			because: "the adapter must return what the reauth executor produced, unchanged");
		captured.Predicate(LoginPageBody).Should().BeTrue(
			because: "the predicate must classify Creatio login HTML as an expired session");
	}

	#endregion

	#region Tests: .NET Core shell detection via adapter

	[Test]
	[Description("Adapter routes .NET Core Login.html JS-bootstrap shell through reauth — that body has no /Login/ literal, only the bootstrap.login loader id, so this pins Gate 2's netcore arm independently of the netfw arm")]
	public void ExecutePostRequest_ShouldClassifyNetCoreLoginShellAsSessionExpired_WhenExecutorReturnsBootstrapShell() {
		// Arrange — same wiring as the other routing tests but the canned body is the
		// netcore shell. The captured predicate must classify it as session-expired.
		(CreatioClientAdapter adapter, CapturedExecute captured) = CreateAdapterWithCapture("{\"ok\":true}");

		// Act
		adapter.ExecutePostRequest("/x", "data");

		// Assert
		captured.Predicate.Should().NotBeNull(
			because: "the adapter must hand a predicate to the executor for unauthorized detection");
		captured.Predicate(NetCoreLoginShellBody).Should().BeTrue(
			because: ".NET Core kick-out renders a JS-bootstrap shell with no /Login/ literal; the predicate must catch it via the bootstrap.login loader id");
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
		passthrough.Execute(Arg.Any<Func<string>>(), Arg.Any<Func<string, bool>>(), Arg.Any<bool>())
			.Returns(ci => real.Execute(ServerCall, ci.Arg<Func<string, bool>>(), ci.ArgAt<bool>(2)));
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

	#region Tests: Transport delegation and the no-replay rule for writes

	// The transport seam (ICreatioClientTransport) is what makes these assertions possible at all:
	// before it, the fixture had to resolve its Lazy<CreatioClient> to null, so the callback handed to
	// the reauth executor could never be invoked and the delegation itself went unverified.
	private static ICreatioClientTransport CreateTransport(string response) {
		ICreatioClientTransport transport = Substitute.For<ICreatioClientTransport>();
		transport.ExecutePutRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<int>()).Returns(response);
		transport.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<int>()).Returns(response);
		transport.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(response);
		return transport;
	}

	[Test]
	[Description("The callback ExecutePutRequest hands to the reauth executor issues exactly the PUT it was asked for, with every argument preserved")]
	public void ExecutePutRequest_ShouldDelegateToTransportPutWithAllArguments_WhenCapturedCallbackIsInvoked() {
		// Arrange
		ICreatioClientTransport transport = CreateTransport("{\"put\":true}");
		CapturedExecute captured = new();
		IReauthExecutor executor = Substitute.For<IReauthExecutor>();
		executor.Execute(Arg.Any<Func<string>>(), Arg.Any<Func<string, bool>>(), Arg.Any<bool>())
			.Returns(ci => {
				captured.Call = ci.Arg<Func<string>>();
				return "{}";
			});
		CreatioClientAdapter adapter = new(transport, executor);

		// Act
		adapter.ExecutePutRequest("/x", "body", 7_000, 3, 5);
		string captureResult = captured.Call();

		// Assert
		captureResult.Should().Be("{\"put\":true}",
			because: "the captured callback must return what the transport produced, proving it is the PUT and not another verb");
		transport.Received(1).ExecutePutRequest("/x", "body", 7_000, 3, 5);
		// because: url, body, timeout, attempts and delay must all survive the hop into the callback -
		// a dropped or reordered argument used to be invisible, since the callback was never invoked
		transport.DidNotReceive().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
			Arg.Any<int>(), Arg.Any<int>());
		transport.DidNotReceive().ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
			Arg.Any<int>(), Arg.Any<int>());
		transport.DidNotReceive().ExecuteDeleteRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
			Arg.Any<int>(), Arg.Any<int>());
		transport.DidNotReceive().ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<int>());
		// because: every other verb is excluded explicitly - "it returned the PUT sentinel" alone would
		// still pass if the adapter had also issued a second request through another verb
	}

	[Test]
	[Description("An expired-session response to a PUT re-authenticates but issues the PUT exactly once")]
	public void ExecutePutRequest_ShouldIssueExactlyOneUnderlyingCall_WhenSessionExpiredResponseIsReturned() {
		// Arrange - the transport always answers with the login page, so a replay would be visible.
		ICreatioClientTransport transport = CreateTransport(LoginPageBody);
		CreatioClientAdapter adapter = new(transport, reauthExecutor: null);

		// Act
		string result = adapter.ExecutePutRequest("/x", "body");

		// Assert
		transport.Received(1).ExecutePutRequest("/x", "body", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		transport.Received(1).Login();
		// because: exactly one underlying PUT is the whole point - a replay here commits the write a
		// second time; the single Login proves the session was still refreshed for the next call
		result.Should().Be(LoginPageBody,
			because: "the unreplayed original response must reach the caller so it can report the expired session");
	}

	[Test]
	[Description("An expired-session response to a declared-write POST re-authenticates but issues the POST exactly once")]
	public void ExecuteNonReplayablePostRequest_ShouldIssueExactlyOneUnderlyingCall_WhenSessionExpiredResponseIsReturned() {
		// Arrange
		ICreatioClientTransport transport = CreateTransport(LoginPageBody);
		CreatioClientAdapter adapter = new(transport, reauthExecutor: null);

		// Act
		adapter.ExecuteNonReplayablePostRequest("/x", "body");

		// Assert
		transport.Received(1).ExecutePostRequest("/x", "body", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		transport.Received(1).Login();
		// because: a POST the caller declared a write must behave exactly like PUT, even though the
		// plain POST below still replays
	}

	[Test]
	[Description("A GET still replays after re-authentication - the no-replay rule must not disable session recovery for reads")]
	public void ExecuteGetRequest_ShouldReplayAfterReauth_WhenSessionExpiredResponseIsReturned() {
		// Arrange
		ICreatioClientTransport transport = CreateTransport(LoginPageBody);
		CreatioClientAdapter adapter = new(transport, reauthExecutor: null);

		// Act
		adapter.ExecuteGetRequest("/x");

		// Assert
		transport.Received(2).ExecuteGetRequest("/x", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		transport.Received(1).Login();
		// because: the read path must still recover from an expired session - two GETs around one Login
		// is the ENG-90393 behaviour this change must not remove
	}

	#endregion

}
