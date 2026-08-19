using System;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
internal class LoginDiagnosticsTests {

	#region Constants: Private

	// The exact shape Creatio.Client throws when the login response carries "Code":1 — the failure
	// GitHub #1106 tracks.
	private const string LoginRejectionMessage = "Unauthorized svc_user for https://host";

	#endregion

	#region Methods: Private

	private static LoginDiagnostics.LoginAttemptScoreboard CreateScoreboard() => new();

	private static LoginDiagnostics CreateSut(LoginDiagnostics.LoginAttemptScoreboard scoreboard) =>
		new(scoreboard);

	private static string FieldValue(string message, string fieldName) {
		Match match = Regex.Match(message, $@"\b{Regex.Escape(fieldName)}=([^\s\]]+)");
		match.Success.Should().BeTrue(
			because: $"the diagnostic context must carry a '{fieldName}' field. Actual message: {message}");
		return match.Groups[1].Value;
	}

	#endregion

	#region Tests: Track

	[Test]
	[Description("Track invokes the login callback exactly once and returns transparently when it succeeds")]
	public void Track_ShouldInvokeLoginOnceAndNotThrow_WhenLoginSucceeds() {
		// Arrange
		LoginDiagnostics sut = CreateSut(CreateScoreboard());
		int loginCallCount = 0;

		// Act
		Action act = () => sut.Track(() => loginCallCount++, LoginAttemptKind.Initial);

		// Assert
		act.Should().NotThrow(
			because: "a successful login must stay transparent — the recorder only decorates failures");
		loginCallCount.Should().Be(1,
			because: "the recorder must invoke the wrapped login callback exactly once, with no retry of its own");
	}

	[Test]
	[Description("Track releases the login gauge after a successful login so the counter cannot leak")]
	public void Track_ShouldReleaseLoginGauge_WhenLoginSucceeds() {
		// Arrange
		LoginDiagnostics.LoginAttemptScoreboard scoreboard = CreateScoreboard();
		LoginDiagnostics sut = CreateSut(scoreboard);

		// Act
		sut.Track(() => { }, LoginAttemptKind.Initial);

		// Assert
		scoreboard.LoginsInFlight.Should().Be(0,
			because: "a finished login must leave no in-flight residue, otherwise every later failure would "
				+ "report an inflated concurrency figure and the #1106 evidence would be worthless");
	}

	[Test]
	[Description("Track releases the login gauge after a failed login so the counter cannot leak")]
	public void Track_ShouldReleaseLoginGauge_WhenLoginThrows() {
		// Arrange
		LoginDiagnostics.LoginAttemptScoreboard scoreboard = CreateScoreboard();
		LoginDiagnostics sut = CreateSut(scoreboard);

		// Act
		Action act = () => sut.Track(
			() => throw new UnauthorizedAccessException(LoginRejectionMessage), LoginAttemptKind.Initial);

		// Assert
		act.Should().Throw<CreatioLoginFailedException>(
			because: "a failed login must surface as the decorated exception");
		scoreboard.LoginsInFlight.Should().Be(0,
			because: "the gauge must be released on the failure path too, not only on success");
	}

	[Test]
	[Description("Track throws ArgumentNullException when the login callback is null")]
	public void Track_ShouldThrowArgumentNullException_WhenLoginIsNull() {
		// Arrange
		LoginDiagnostics sut = CreateSut(CreateScoreboard());

		// Act
		Action act = () => sut.Track(null, LoginAttemptKind.Initial);

		// Assert
		act.Should().Throw<ArgumentNullException>(
			because: "a null login callback is a programming error and must fail loudly at the call site");
	}

	[Test]
	[Description("Track decorates any failure of a clio-driven login, not only the Code:1 rejection shape")]
	public void Track_ShouldDecorateAnyFailure_WhenClioDrivesTheLogin() {
		// Arrange
		LoginDiagnostics sut = CreateSut(CreateScoreboard());

		// Act
		Action act = () => sut.Track(() => throw new TimeoutException("login timed out"),
			LoginAttemptKind.Reauthentication);

		// Assert
		act.Should().Throw<CreatioLoginFailedException>(
			because: "on the explicit paths every exception is unambiguously a login failure, so all of them "
				+ "deserve the context — unlike the request path, where only the rejection shape is a login")
			.Which.Message.Should().Contain("original-type=TimeoutException",
				because: "the original failure type must stay visible after the decoration");
	}

	#endregion

	#region Tests: TrackRequest

	[Test]
	[Description("TrackRequest returns the request result unchanged when the request succeeds")]
	public void TrackRequest_ShouldReturnResultUnchanged_WhenRequestSucceeds() {
		// Arrange
		LoginDiagnostics.LoginAttemptScoreboard scoreboard = CreateScoreboard();
		LoginDiagnostics sut = CreateSut(scoreboard);

		// Act
		string result = sut.TrackRequest(() => "{\"success\":true}");

		// Assert
		result.Should().Be("{\"success\":true}",
			because: "the wrapper must be transparent on the happy path — it observes, it does not transform");
		scoreboard.RequestsInFlight.Should().Be(0,
			because: "a finished request must release its gauge slot");
	}

	[Test]
	[Description("TrackRequest decorates the implicit login rejection raised from inside a request")]
	public void TrackRequest_ShouldDecorateAsImplicit_WhenImplicitLoginIsRejected() {
		// Arrange
		LoginDiagnostics sut = CreateSut(CreateScoreboard());

		// Act
		CreatioLoginFailedException failure = Assert.Throws<CreatioLoginFailedException>(
			() => sut.TrackRequest<string>(() => throw new UnauthorizedAccessException(LoginRejectionMessage)));

		// Assert
		failure.Message.Should().StartWith(LoginRejectionMessage,
			because: "the original server message is the primary signal and must not be replaced");
		FieldValue(failure.Message, "kind").Should().Be("implicit",
			because: "this is the login the NuGet client performs inside the first request of a fresh client — "
				+ "the dominant path in the MCP surface, where every tool call builds its own client");
		FieldValue(failure.Message, "client-request").Should().Be("1",
			because: "the implicit login happens on the client's first request, so a request ordinal of one "
				+ "confirms the failure really is the implicit login rather than a later request");
		failure.Message.Should().NotContain("client-login=",
			because: "an implicit login is counted as a request, so labelling it a login ordinal would claim a "
				+ "login count clio never observed");
	}

	[Test]
	[Description("TrackRequest leaves an ordinary request failure completely untouched")]
	public void TrackRequest_ShouldRethrowUntouched_WhenFailureIsNotALoginRejection() {
		// Arrange
		LoginDiagnostics sut = CreateSut(CreateScoreboard());
		InvalidOperationException original = new("Select query failed.");

		// Act
		Action act = () => sut.TrackRequest<string>(() => throw original);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "wrapping the request path must not change how ordinary request failures surface")
			.Which.Should().BeSameAs(original,
				because: "the very same exception instance must propagate, with no decoration and no re-wrapping");
	}

	[Test]
	[Description("TrackRequest leaves an unrelated UnauthorizedAccessException untouched")]
	public void TrackRequest_ShouldRethrowUntouched_WhenUnauthorizedExceptionIsNotTheLoginShape() {
		// Arrange
		LoginDiagnostics sut = CreateSut(CreateScoreboard());
		UnauthorizedAccessException original = new("Access to the path '/tmp/x' is denied.");

		// Act
		Action act = () => sut.TrackRequest<string>(() => throw original);

		// Assert
		act.Should().Throw<UnauthorizedAccessException>(
			because: "matching on the exception type alone would capture unrelated access denials, so the "
				+ "message prefix has to gate the decoration")
			.Which.Should().BeSameAs(original,
				because: "a non-login access denial must propagate as the very same exception instance");
	}

	[Test]
	[Description("TrackRequest releases the request gauge after a failure so the counter cannot leak")]
	public void TrackRequest_ShouldReleaseRequestGauge_WhenRequestThrows() {
		// Arrange
		LoginDiagnostics.LoginAttemptScoreboard scoreboard = CreateScoreboard();
		LoginDiagnostics sut = CreateSut(scoreboard);

		// Act
		Action act = () => sut.TrackRequest<string>(() => throw new InvalidOperationException("boom"));

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "the failure must still propagate");
		scoreboard.RequestsInFlight.Should().Be(0,
			because: "the request gauge must be released even when the request failed undecorated");
	}

	[Test]
	[Description("TrackRequest throws ArgumentNullException when the request callback is null")]
	public void TrackRequest_ShouldThrowArgumentNullException_WhenRequestIsNull() {
		// Arrange
		LoginDiagnostics sut = CreateSut(CreateScoreboard());

		// Act
		Action act = () => sut.TrackRequest<string>(null);

		// Assert
		act.Should().Throw<ArgumentNullException>(
			because: "a null request callback is a programming error and must fail loudly at the call site");
	}

	#endregion

	#region Tests: Diagnostic context

	[Test]
	[Description("A failed login keeps the original message and appends the full diagnostic context")]
	public void Track_ShouldKeepOriginalMessageAndAppendContext_WhenLoginThrows() {
		// Arrange
		LoginDiagnostics sut = CreateSut(CreateScoreboard());

		// Act
		CreatioLoginFailedException failure = Assert.Throws<CreatioLoginFailedException>(
			() => sut.Track(() => throw new UnauthorizedAccessException(LoginRejectionMessage),
				LoginAttemptKind.Reauthentication));

		// Assert
		failure.Message.Should().StartWith(LoginRejectionMessage,
			because: "the original server message is the primary signal and must not be replaced");
		failure.Message.Should().Contain("clio-login",
			because: "the appended block needs a stable marker so it is greppable in CI output");
		FieldValue(failure.Message, "kind").Should().Be("reauth",
			because: "the attempt kind tells which of the three login paths was rejected");
		FieldValue(failure.Message, "client").Should().NotBeNullOrWhiteSpace(
			because: "a per-client correlation token is what lets several concurrent tool calls be told apart");
		FieldValue(failure.Message, "client-login").Should().Be("1",
			because: "this is the first login attempt made by this client");
		FieldValue(failure.Message, "process-login").Should().Be("1",
			because: "this is the first login recorded on the scoreboard");
		FieldValue(failure.Message, "started-at").Should().NotBeNullOrWhiteSpace(
			because: "an absolute UTC start time is required to correlate attempts across concurrent calls");
		FieldValue(failure.Message, "elapsed-ms").Should().MatchRegex("^[0-9]+$",
			because: "the login duration must be an invariant-culture integer so CI parsing is stable");
		FieldValue(failure.Message, "since-client-created-ms").Should().MatchRegex("^[0-9]+$",
			because: "the age of the client separates a stale-session re-login from a fresh client's first login");
		FieldValue(failure.Message, "original-type").Should().Be(nameof(UnauthorizedAccessException),
			because: "the original exception type must survive even though it is not kept as an inner exception");
	}

	[Test]
	[Description("The initial attempt kind is rendered as 'initial' in the diagnostic context")]
	public void Track_ShouldRenderInitialKind_WhenLoginIsCallerInitiated() {
		// Arrange
		LoginDiagnostics sut = CreateSut(CreateScoreboard());

		// Act
		CreatioLoginFailedException failure = Assert.Throws<CreatioLoginFailedException>(
			() => sut.Track(() => throw new InvalidOperationException("boom"), LoginAttemptKind.Initial));

		// Assert
		FieldValue(failure.Message, "kind").Should().Be("initial",
			because: "an explicitly requested login must be distinguishable from the two automatic ones");
	}

	[Test]
	[Description("The transport status of a WebException is folded into the context so 401 stays distinguishable from a connect failure")]
	public void Track_ShouldFoldTransportStatus_WhenLoginThrowsWebException() {
		// Arrange
		LoginDiagnostics sut = CreateSut(CreateScoreboard());
		WebException webException = new("connect failed", WebExceptionStatus.ConnectFailure);

		// Act
		CreatioLoginFailedException failure = Assert.Throws<CreatioLoginFailedException>(
			() => sut.Track(() => throw new InvalidOperationException("login failed", webException),
				LoginAttemptKind.Initial));

		// Assert
		FieldValue(failure.Message, "transport").Should().Be(nameof(WebExceptionStatus.ConnectFailure),
			because: "this exception carries no inner exception, so the transport signal the readable-message "
				+ "extension would normally add from an inner WebException has to be folded in here instead");
	}

	[Test]
	[Description("No transport field is emitted when the failure has no WebException anywhere in its chain")]
	public void Track_ShouldOmitTransportField_WhenFailureCarriesNoWebException() {
		// Arrange
		LoginDiagnostics sut = CreateSut(CreateScoreboard());

		// Act
		CreatioLoginFailedException failure = Assert.Throws<CreatioLoginFailedException>(
			() => sut.Track(() => throw new UnauthorizedAccessException(LoginRejectionMessage),
				LoginAttemptKind.Initial));

		// Assert
		failure.Message.Should().NotContain("transport=",
			because: "an absent transport field must mean 'no HTTP-level signal available', not 'unknown status'");
	}

	[Test]
	[Description("Attempt counters advance per client and per scoreboard across separate client instances")]
	public void Track_ShouldAdvanceClientAndProcessCounters_WhenSeveralClientsShareOneScoreboard() {
		// Arrange
		LoginDiagnostics.LoginAttemptScoreboard scoreboard = CreateScoreboard();
		LoginDiagnostics firstClient = CreateSut(scoreboard);
		LoginDiagnostics secondClient = CreateSut(scoreboard);
		firstClient.Track(() => { }, LoginAttemptKind.Initial);

		// Act
		CreatioLoginFailedException firstClientFailure = Assert.Throws<CreatioLoginFailedException>(
			() => firstClient.Track(() => throw new InvalidOperationException("boom"),
				LoginAttemptKind.Reauthentication));
		CreatioLoginFailedException secondClientFailure = Assert.Throws<CreatioLoginFailedException>(
			() => secondClient.Track(() => throw new InvalidOperationException("boom"),
				LoginAttemptKind.Initial));

		// Assert
		FieldValue(firstClientFailure.Message, "client-login").Should().Be("2",
			because: "the client-scoped counter must show this is the second login this client attempted");
		FieldValue(firstClientFailure.Message, "process-login").Should().Be("2",
			because: "the scoreboard-scoped counter must show two logins have been made in the process");
		FieldValue(secondClientFailure.Message, "client-login").Should().Be("1",
			because: "a freshly created client starts its own login count at one");
		FieldValue(secondClientFailure.Message, "process-login").Should().Be("3",
			because: "the scoreboard counter is shared, so a second client continues the process-wide sequence");
		FieldValue(firstClientFailure.Message, "client").Should()
			.NotBe(FieldValue(secondClientFailure.Message, "client"),
				because: "two independently constructed clients must be distinguishable in the log");
	}

	#endregion

	#region Tests: Concurrency evidence

	[Test]
	[Description("The login gauge reports the concurrent clio-driven logins a failing login competed with")]
	public void Track_ShouldReportConcurrentLogins_WhenTwoClientsLoginSimultaneously() {
		// Arrange — two independently constructed clients, exactly like two concurrent MCP tool calls.
		LoginDiagnostics.LoginAttemptScoreboard scoreboard = CreateScoreboard();
		LoginDiagnostics failingClient = CreateSut(scoreboard);
		LoginDiagnostics blockingClient = CreateSut(scoreboard);
		using ManualResetEventSlim blockingStarted = new(false);
		using ManualResetEventSlim failureObserved = new(false);
		Thread blockingThread = new(() => blockingClient.Track(
			() => {
				blockingStarted.Set();
				failureObserved.Wait(TimeSpan.FromSeconds(10));
			},
			LoginAttemptKind.Initial));

		// Act
		blockingThread.Start();
		blockingStarted.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue(
			because: "the blocking login must be in flight before the failing one starts, or the test proves nothing");
		CreatioLoginFailedException failure = Assert.Throws<CreatioLoginFailedException>(
			() => failingClient.Track(() => throw new UnauthorizedAccessException(LoginRejectionMessage),
				LoginAttemptKind.Reauthentication));
		failureObserved.Set();
		blockingThread.Join(TimeSpan.FromSeconds(10)).Should().BeTrue(
			because: "the blocking login must finish so the test does not leak a thread");

		// Assert
		FieldValue(failure.Message, "in-flight-logins").Should().Be("2/2",
			because: "this is the field that settles GitHub #1106 for the clio-driven paths: it proves whether a "
				+ "rejected login overlapped other logins for the same credentials or was the only one in flight");
		FieldValue(failure.Message, "in-flight-requests").Should().Be("0/0",
			because: "keeping the two gauges apart means a login figure is never inflated by unrelated requests");
	}

	[Test]
	[Description("The request gauge reports the concurrent requests a rejected implicit login competed with")]
	public void TrackRequest_ShouldReportConcurrentRequests_WhenTwoClientsRequestSimultaneously() {
		// Arrange — the shape of the concurrent create-app-section scenario: separate clients, one process.
		LoginDiagnostics.LoginAttemptScoreboard scoreboard = CreateScoreboard();
		LoginDiagnostics failingClient = CreateSut(scoreboard);
		LoginDiagnostics blockingClient = CreateSut(scoreboard);
		using ManualResetEventSlim blockingStarted = new(false);
		using ManualResetEventSlim failureObserved = new(false);
		Thread blockingThread = new(() => blockingClient.TrackRequest(() => {
			blockingStarted.Set();
			failureObserved.Wait(TimeSpan.FromSeconds(10));
			return "ok";
		}));

		// Act
		blockingThread.Start();
		blockingStarted.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue(
			because: "the blocking request must be in flight before the failing one starts, or the test proves nothing");
		CreatioLoginFailedException failure = Assert.Throws<CreatioLoginFailedException>(
			() => failingClient.TrackRequest<string>(
				() => throw new UnauthorizedAccessException(LoginRejectionMessage)));
		failureObserved.Set();
		blockingThread.Join(TimeSpan.FromSeconds(10)).Should().BeTrue(
			because: "the blocking request must finish so the test does not leak a thread");

		// Assert
		FieldValue(failure.Message, "in-flight-requests").Should().Be("2/2",
			because: "an implicit login happens at the very start of its request, so the concurrent-request count "
				+ "is the observable proxy for concurrent implicit logins — the #1106 question for this path");
	}

	#endregion

	#region Tests: Message survival

	[Test]
	[Description("The decorated exception is inner-less so both clio message surfaces keep the diagnostic context")]
	public void Track_ShouldProduceInnerLessException_SoBothMessageSurfacesKeepTheContext() {
		// Arrange
		LoginDiagnostics sut = CreateSut(CreateScoreboard());

		// Act
		CreatioLoginFailedException failure = Assert.Throws<CreatioLoginFailedException>(
			() => sut.Track(() => throw new UnauthorizedAccessException(LoginRejectionMessage),
				LoginAttemptKind.Reauthentication));

		// Assert
		failure.InnerException.Should().BeNull(
			because: "keeping the original as an inner exception would make GetBaseException() and "
				+ "SurfacedExceptionMessage.Resolve() both walk past the decorated message and discard it");
		failure.GetBaseException().Message.Should().Be(failure.Message,
			because: "the application-section commands report the root cause via GetBaseException().Message, so the "
				+ "diagnostic context must survive that reduction");
		SurfacedExceptionMessage.Resolve(failure).Should().Be(failure.Message,
			because: "the MCP boundary surfaces the inner-most message, so the diagnostic context must survive it too");
	}

	[Test]
	[Description("The original exception and the diagnostic context are preserved in Exception.Data")]
	public void Track_ShouldPreserveOriginalExceptionAndContextInData_WhenLoginThrows() {
		// Arrange
		LoginDiagnostics sut = CreateSut(CreateScoreboard());

		// Act
		CreatioLoginFailedException failure = Assert.Throws<CreatioLoginFailedException>(
			() => sut.Track(() => throw new UnauthorizedAccessException(LoginRejectionMessage),
				LoginAttemptKind.Initial));

		// Assert
		failure.Data[CreatioLoginFailedException.OriginalExceptionDataKey].Should().BeOfType<string>()
			.Which.Should().Contain(nameof(UnauthorizedAccessException),
				because: "dropping the inner exception must not lose the original type and stack trace for debugging");
		failure.Data[CreatioLoginFailedException.DiagnosticContextDataKey].Should().BeOfType<string>()
			.Which.Should().StartWith("clio-login",
				because: "the context must also be readable structurally, without parsing it back out of the message");
	}

	#endregion
}
