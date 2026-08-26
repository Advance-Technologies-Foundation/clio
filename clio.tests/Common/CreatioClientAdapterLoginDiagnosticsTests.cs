using System;
using System.Net;
using Clio.Common;
using Creatio.Client;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// Pins that <see cref="CreatioClientAdapter"/> actually routes through <see cref="ILoginDiagnostics"/>.
/// Without these, the GitHub #1106 instrumentation could be dropped from any wiring point by a later
/// refactor with no test signal, because every other auth test substitutes <see cref="IApplicationClient"/>
/// and replaces the adapter entirely.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
internal class CreatioClientAdapterLoginDiagnosticsTests {

	#region Methods: Private

	// The exact shape Creatio.Client throws when the login response carries "Code":1.
	private const string LoginRejectionMessage = "Unauthorized svc_user for https://host";

	private const string LoginPageBody =
		"<!DOCTYPE html><html><head><title>Login</title></head>" +
		"<body><form action=\"/0/Login/NuiLogin.aspx\"><input/></form></body></html>";

	// The Lazy is intentionally never resolved: the substituted diagnostics never invokes the wrapped
	// callback, so Client is never dereferenced inside the adapter.
	private static CreatioClientAdapter CreateAdapter(ILoginDiagnostics diagnostics,
		IReauthExecutor reauthExecutor = null) =>
		new(new Lazy<CreatioClient>(() => null), reauthExecutor, diagnostics);

	// Its own scoreboard, never LoginAttemptScoreboard.Shared: a test that reached the process-wide
	// singleton would be order-dependent under parallel execution.
	private static LoginDiagnostics CreateRecorder() => new(new LoginDiagnostics.LoginAttemptScoreboard());

	private static IReauthExecutor CreatePassthroughExecutor() {
		IReauthExecutor executor = Substitute.For<IReauthExecutor>();
		executor.Execute(Arg.Any<Func<string>>(), Arg.Any<Func<string, bool>>())
			.Returns(ci => ci.Arg<Func<string>>()());
		return executor;
	}

	#endregion

	#region Tests: Request wiring

	[TestCase("ExecutePostRequest")]
	[TestCase("CallConfigurationService")]
	[Description("The request methods record every call through ILoginDiagnostics.TrackRequest")]
	public void RequestMethod_ShouldRecordThroughLoginDiagnostics_WhenInvoked(string methodName) {
		// Arrange
		ILoginDiagnostics diagnostics = Substitute.For<ILoginDiagnostics>();
		diagnostics.TrackRequest(Arg.Any<Func<string>>()).Returns("{}");
		CreatioClientAdapter adapter = CreateAdapter(diagnostics, CreatePassthroughExecutor());

		// Act
		string result = methodName == "ExecutePostRequest"
			? adapter.ExecutePostRequest("/x", "data")
			: adapter.CallConfigurationService("Svc", "Method", "data");

		// Assert
		result.Should().Be("{}",
			because: "the recorder is transparent on success and must not alter the response");
		diagnostics.Received(1).TrackRequest(Arg.Any<Func<string>>());
	}

	[Test]
	[Description("DownloadFile records through the void TrackRequest overload even though it bypasses the reauth executor")]
	public void DownloadFile_ShouldRecordThroughLoginDiagnostics_WhenInvoked() {
		// Arrange
		ILoginDiagnostics diagnostics = Substitute.For<ILoginDiagnostics>();
		CreatioClientAdapter adapter = CreateAdapter(diagnostics);

		// Act
		adapter.DownloadFile("https://host/file", "/tmp/file", "data");

		// Assert
		Action recorded = () => diagnostics.Received(1).TrackRequest(Arg.Any<Action>());
		recorded.Should().NotThrow(
			because: "DownloadFile bypasses the reauth executor, so its recording has to be wired separately "
				+ "and is the easiest one for a later refactor to drop unnoticed");
	}

	#endregion

	#region Tests: Login wiring

	[Test]
	[Description("An explicit Login is recorded as LoginAttemptKind.Initial")]
	public void Login_ShouldRecordInitialAttempt_WhenCalledExplicitly() {
		// Arrange
		ILoginDiagnostics diagnostics = Substitute.For<ILoginDiagnostics>();
		CreatioClientAdapter adapter = CreateAdapter(diagnostics);

		// Act
		adapter.Login();

		// Assert
		Action recorded = () => diagnostics.Received(1).Track(Arg.Any<Action>(), LoginAttemptKind.Initial);
		recorded.Should().NotThrow(
			because: "an explicit login must be distinguishable in CI output from the two automatic ones");
	}

	[Test]
	[Description("The default reauth executor's re-login is recorded as LoginAttemptKind.Reauthentication")]
	public void Reauth_ShouldRecordReauthenticationAttempt_WhenSessionExpiredResponseTriggersRelogin() {
		// Arrange — no reauth executor, so the adapter builds the real one whose login closure is the
		// only place the Reauthentication kind is produced.
		ILoginDiagnostics diagnostics = Substitute.For<ILoginDiagnostics>();
		diagnostics.TrackRequest(Arg.Any<Func<string>>()).Returns(LoginPageBody);
		CreatioClientAdapter adapter = CreateAdapter(diagnostics);

		// Act — the canned login page keeps tripping the session-expired predicate; whether the executor
		// eventually gives up by returning or by throwing is not what this test pins.
		try {
			adapter.ExecutePostRequest("/x", "data");
		} catch (Exception) {
			// Intentionally ignored: only the recorded re-login matters here.
		}

		// Assert
		Action recorded = () =>
			diagnostics.Received().Track(Arg.Any<Action>(), LoginAttemptKind.Reauthentication);
		recorded.Should().NotThrow(
			because: "the re-login closure lives inside the default reauth executor, so nothing else in the "
				+ "suite can reach it");
	}

	#endregion

	#region Tests: Surfaced exception type

	[Test]
	[Description("A login rejection surfaced by the adapter stays an UnauthorizedAccessException so clio's auth classifiers keep matching")]
	public void Login_ShouldSurfaceUnauthorizedAccessException_WhenCredentialsAreRejected() {
		// Arrange — a real recorder plus a client whose construction throws the rejection shape.
		CreatioClientAdapter adapter = new(
			new Lazy<CreatioClient>(() => throw new UnauthorizedAccessException(LoginRejectionMessage)),
			Substitute.For<IReauthExecutor>(), CreateRecorder());

		// Act
		Action act = () => adapter.Login();

		// Assert
		act.Should().Throw<UnauthorizedAccessException>(
			because: "ServerReadinessWaiter, GetCreatioInfoCommand, SchemaNamePrefixTool and "
				+ "SysSettingsCommand.CategorizeError all classify a refused credential by this type")
			.Which.Message.Should().Contain("clio-login",
				because: "the diagnostic context is the deliverable and must reach the caller in the message");
	}

	[Test]
	[Description("A non-auth login failure surfaced by the adapter keeps its own type so typed callers keep working")]
	public void Login_ShouldSurfaceOriginalType_WhenFailureIsNotACredentialRejection() {
		// Arrange
		WebException original = new("connect failed", WebExceptionStatus.ConnectFailure);
		CreatioClientAdapter adapter = new(new Lazy<CreatioClient>(() => throw original),
			Substitute.For<IReauthExecutor>(), CreateRecorder());

		// Act
		Action act = () => adapter.Login();

		// Assert
		act.Should().Throw<WebException>(
			because: "RemoteCommand.Login returns exit code 1 from catch (WebException) and "
				+ "BaseDataContextCommand renders its 404 diagnostic there; substituting the type would turn "
				+ "a returned exit code into an escaping exception")
			.Which.Should().BeSameAs(original,
				because: "an untouched failure must reach the caller as the identical instance");
	}

	#endregion

}
