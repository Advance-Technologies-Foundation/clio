using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using ILogger = Clio.Common.ILogger;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
internal class ReauthExecutorTests {

	#region Methods: Private

	// .NET Framework kick-out: rendered NuiLogin.aspx page; trips Gate 2 via the `/Login/`
	// substring in the form action.
	private const string NetFrameworkLoginPageBody =
		"<!DOCTYPE html><html><head><title>Login</title></head><body>" +
		"<form action=\"/0/Login/NuiLogin.aspx\"><input/></form></body></html>";

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

	private static ReauthExecutor CreateExecutor(Action login, ILogger logger = null) {
		return new ReauthExecutor(login, logger);
	}

	#endregion

	#region Tests: Execute

	[Test]
	[Description("Execute returns the first call result and does not call Login when the response is not a login page")]
	public void Execute_ShouldReturnResultWithoutLogin_WhenFirstCallReturnsNonLoginPage() {
		// Arrange
		int loginCallCount = 0;
		ReauthExecutor sut = CreateExecutor(() => loginCallCount++);
		int callCount = 0;

		// Act
		string result = sut.Execute(() => {
			callCount++;
			return "{\"success\":true}";
		}, ReauthExecutor.IsSessionExpiredResponse);

		// Assert
		result.Should().Be("{\"success\":true}",
			because: "a non-session-expired response must be returned unchanged");
		callCount.Should().Be(1,
			because: "Execute must not retry when the response is not a session-expired response");
		loginCallCount.Should().Be(0,
			because: "Login must not be invoked for healthy responses");
	}

	[Test]
	[Description("Execute calls Login once and retries when the first call returns the login page")]
	public void Execute_ShouldCallLoginOnceAndRetry_WhenFirstCallReturnsLoginPage() {
		// Arrange
		int loginCallCount = 0;
		ReauthExecutor sut = CreateExecutor(() => loginCallCount++);
		int callCount = 0;
		string[] responses = { LoginPageBody, "{\"ok\":true}" };

		// Act
		string result = sut.Execute(() => responses[callCount++], ReauthExecutor.IsSessionExpiredResponse);

		// Assert
		result.Should().Be("{\"ok\":true}",
			because: "after a single re-auth the retry response must be returned to the caller");
		callCount.Should().Be(2,
			because: "Execute must perform exactly one retry after re-authentication");
		loginCallCount.Should().Be(1,
			because: "Login must be called exactly once per detected expired session");
	}

	[Test]
	[Description("Execute propagates the exception thrown by the Login callback")]
	public void Execute_ShouldPropagateException_WhenLoginThrows() {
		// Arrange
		ReauthExecutor sut = CreateExecutor(() => throw new InvalidOperationException("login failed"));

		// Act
		Action act = () => sut.Execute(() => LoginPageBody, ReauthExecutor.IsSessionExpiredResponse);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "the caller must observe authentication failures rather than have them silently swallowed")
			.WithMessage("login failed");
	}

	[Test]
	[Description("Execute writes a single warning when a re-authentication is performed")]
	public void Execute_ShouldLogSingleWarning_WhenReauthIsPerformed() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		List<string> warnings = [];
		logger.When(l => l.WriteWarning(Arg.Any<string>()))
			.Do(ci => warnings.Add(ci.Arg<string>()));
		ReauthExecutor sut = CreateExecutor(() => { }, logger);
		int callCount = 0;
		string[] responses = { LoginPageBody, "{}" };

		// Act
		sut.Execute(() => responses[callCount++], ReauthExecutor.IsSessionExpiredResponse);

		// Assert — capture+inspect pattern (instead of logger.Received(1)) so the
		// FluentAssertions `because:` rationale is recorded on the assertion itself,
		// per AGENTS.md test-style policy.
		warnings.Should().ContainSingle(
			because: "the reauth path must emit exactly one operator-facing warning so a session refresh is visible without flooding the log on serial kick-outs");
		warnings[0].Should().Contain("re-authenticated",
			because: "the warning text must name the action that was performed so the operator can correlate it with the response delay");
	}

	[Test]
	[Description("Execute deduplicates Login when two threads concurrently observe a login page")]
	public async Task Execute_ShouldDedupeLogin_WhenTwoCallersHitLoginPageConcurrently() {
		// Arrange — drive the timeline with two ManualResetEventSlim gates rather than
		// sleeps: thread A enters the Login callback, signals 'aEnteredLogin', then parks
		// on 'releaseLogin'. While A holds the reauth lock, the test starts thread B; B
		// observes the login page on its first call and blocks on the same lock. Releasing
		// 'releaseLogin' lets A finish Login + bump the version + release the lock, after
		// which B enters the lock, sees the bumped version, skips Login, and retries.
		int loginCallCount = 0;
		ManualResetEventSlim aEnteredLogin = new(false);
		ManualResetEventSlim releaseLogin = new(false);
		ReauthExecutor sut = CreateExecutor(() => {
			aEnteredLogin.Set();
			releaseLogin.Wait(TimeSpan.FromSeconds(5));
			Interlocked.Increment(ref loginCallCount);
		});
		bool serverAuthenticated = false;
		string Call() => Volatile.Read(ref serverAuthenticated) ? "{}" : LoginPageBody;

		// Act
		Task<string> a = Task.Run(() => sut.Execute(Call, ReauthExecutor.IsSessionExpiredResponse));
		aEnteredLogin.Wait(TimeSpan.FromSeconds(2));
		// A is now parked inside Login() while holding the reauth lock with versionAtStart
		// still equal to the executor's current version.
		Task<string> b = Task.Run(() => sut.Execute(Call, ReauthExecutor.IsSessionExpiredResponse));
		// Flip the simulated server state to authenticated BEFORE releasing A, so that the
		// retry call() returns JSON regardless of which thread reaches it first.
		Volatile.Write(ref serverAuthenticated, true);
		releaseLogin.Set();
		await Task.WhenAll(a, b);

		// Assert
		a.Result.Should().Be("{}",
			because: "thread A must surface the post-reauth JSON response");
		b.Result.Should().Be("{}",
			because: "thread B must also surface the post-reauth JSON response");
		loginCallCount.Should().Be(1,
			because: "a concurrent burst of expired-session responses must trigger exactly one Login");
	}

	[Test]
	[Description("Execute re-authenticates on every serial expired-session response — no time-window dedupe that could swallow successive kick-outs")]
	public void Execute_ShouldReauthOnEverySerialFailure_WhenServerKeepsInvalidatingSession() {
		// Arrange — model a server that invalidates the session after every successful
		// response: clientToken == authToken means the cookie is valid; after Login the
		// client realigns with the server token, and the server bumps its token right
		// after returning the JSON so the very next call fails again.
		int loginCount = 0;
		int authToken = 0;
		int clientToken = -1;
		ReauthExecutor sut = CreateExecutor(() => {
			loginCount++;
			clientToken = authToken;
		});
		string Call() {
			bool valid = clientToken == authToken && clientToken >= 0;
			if (!valid) {
				return LoginPageBody;
			}
			authToken++;
			return "{\"ok\":true}";
		}

		// Act — three successive Execute calls, each starting with an invalidated session.
		string r1 = sut.Execute(Call, ReauthExecutor.IsSessionExpiredResponse);
		string r2 = sut.Execute(Call, ReauthExecutor.IsSessionExpiredResponse);
		string r3 = sut.Execute(Call, ReauthExecutor.IsSessionExpiredResponse);

		// Assert
		r1.Should().Be("{\"ok\":true}",
			because: "the first kick-out must reauth and the retry must reach the caller as JSON");
		r2.Should().Be("{\"ok\":true}",
			because: "a second kick-out must also reauth even though it happens right after the first");
		r3.Should().Be("{\"ok\":true}",
			because: "the third successive kick-out must still reauth — no time-based dedupe may swallow it");
		loginCount.Should().Be(3,
			because: "each successive expired-session response must trigger exactly one Login");
	}

	[Test]
	[Description("Execute never invokes Login more than once per call, even when the retry also fails — bounded behavior, no DDoS")]
	public void Execute_ShouldCallLoginAtMostOncePerExecute_WhenRetryAlsoReturnsLoginPage() {
		// Arrange — every underlying call returns the login page, so the retry never succeeds.
		int loginCount = 0;
		int callCount = 0;
		ReauthExecutor sut = CreateExecutor(() => loginCount++);

		// Act
		string result = sut.Execute(() => {
			callCount++;
			return LoginPageBody;
		}, ReauthExecutor.IsSessionExpiredResponse);

		// Assert
		result.Should().Be(LoginPageBody,
			because: "after one retry the executor must surface the session-expired response to the caller, not loop");
		loginCount.Should().Be(1,
			because: "a single Execute call must trigger at most one Login regardless of retry outcome");
		callCount.Should().Be(2,
			because: "underlying call count must be bounded to the initial attempt plus one retry");
	}

	[Test]
	[Description("Execute throws ArgumentNullException when the call delegate is null")]
	public void Execute_ShouldThrowArgumentNullException_WhenCallIsNull() {
		// Arrange
		ReauthExecutor sut = CreateExecutor(() => { });

		// Act
		Action act = () => sut.Execute<string>(null, _ => false);

		// Assert
		act.Should().Throw<ArgumentNullException>(
			because: "Execute cannot operate without a call delegate to invoke");
	}

	[Test]
	[Description("Execute throws ArgumentNullException when the unauthorized predicate is null")]
	public void Execute_ShouldThrowArgumentNullException_WhenPredicateIsNull() {
		// Arrange
		ReauthExecutor sut = CreateExecutor(() => { });

		// Act
		Action act = () => sut.Execute(() => "x", null);

		// Assert
		act.Should().Throw<ArgumentNullException>(
			because: "Execute cannot decide whether to retry without an unauthorized predicate");
	}

	#endregion

	#region Tests: IsSessionExpiredResponse

	[Test]
	[Description("IsSessionExpiredResponse returns false for null, empty, and whitespace-only bodies")]
	public void IsSessionExpiredResponse_ShouldReturnFalse_WhenBodyIsNullEmptyOrWhitespace() {
		// Arrange / Act / Assert
		ReauthExecutor.IsSessionExpiredResponse(null).Should().BeFalse(
			because: "a null body cannot be a session-expired response");
		ReauthExecutor.IsSessionExpiredResponse(string.Empty).Should().BeFalse(
			because: "an empty body cannot be a session-expired response");
		ReauthExecutor.IsSessionExpiredResponse("   ").Should().BeFalse(
			because: "a whitespace-only body has no content and cannot be a session-expired response");
	}

	[TestCase("{\"success\":true}")]
	[TestCase("  {\"success\":true}")]
	[TestCase("[]")]
	[TestCase("  [1,2,3]")]
	[TestCase("\"plain string\"")]
	[TestCase("true")]
	[TestCase("123")]
	[Description("IsSessionExpiredResponse returns false for any JSON payload regardless of leading whitespace")]
	public void IsSessionExpiredResponse_ShouldReturnFalse_WhenBodyIsJsonPayload(string body) {
		// Act / Assert
		ReauthExecutor.IsSessionExpiredResponse(body).Should().BeFalse(
			because: "JSON payloads must never be classified as session-expired responses");
	}

	[Test]
	[Description("IsSessionExpiredResponse returns false for arbitrary HTML that lacks Creatio login markers")]
	public void IsSessionExpiredResponse_ShouldReturnFalse_WhenHtmlContainsNoLoginMarkers() {
		// Arrange
		string body = "<html><body><h1>Not a login page</h1></body></html>";

		// Act / Assert
		ReauthExecutor.IsSessionExpiredResponse(body).Should().BeFalse(
			because: "HTML without Creatio login markers must not be misclassified as an expired session");
	}

	// Realistic .NET Framework login page — the form action lives under /Login/.
	[TestCase("<html><body><form action=\"/0/Login/NuiLogin.aspx\" id=\"frmLogin\"><input/></form></body></html>")]
	// .NET Framework "Object moved" 302 redirect body pointing to NuiLogin under /Login/.
	[TestCase("<html><head><title>Object moved</title></head><body><h2>Object moved to <a href=\"/Login/NuiLogin.aspx?ReturnUrl=%2fapp%2f0%2fDataService%2f\">here</a>.</h2></body></html>")]
	// .NET Core "Object moved" 302 redirect body pointing to /Login/Login.html.
	[TestCase("<html><head><title>Object moved</title></head><body><h2>Object moved to <a href=\"/Login/Login.html?ReturnUrl=%2fapp%2f\">here</a>.</h2></body></html>")]
	// Resource links to /Login/ assets (favicon, CSS) embedded in a rendered login page.
	[TestCase("<html><head><link rel=\"stylesheet\" href=\"/Login/css/site.css\"/></head><body/></html>")]
	// Leading whitespace + BOM-free preamble must not bypass detection.
	[TestCase("  \n\t<html><body><form action=\"/Login/NuiLogin.aspx\"></form></body></html>")]
	// .NET Core auto-followed Login.html shell — the form is rendered client-side, so the
	// body has no `/Login/` literal; the stable signature is the bootstrap loader id.
	[TestCase("<!DOCTYPE html><html lang=\"en\" culture=\"en-US\"><head><title>Creatio</title><script src=\"/core/hash/Terrasoft/amd/bootstrap-loader.js\" data-loadbootstrap=\"bootstrap.login\" data-baseurl=\"http://host\"></script></head><body></body></html>")]
	[Description("IsSessionExpiredResponse returns true for HTML bodies that reference Creatio's auth-routing namespace (Gate 2): /Login/ for the .NET FW rendered page and Object-moved redirects, and \"bootstrap.login\" for the .NET Core login shell")]
	public void IsSessionExpiredResponse_ShouldReturnTrue_WhenBodyContainsAuthRouteToken(string body) {
		// Act / Assert
		ReauthExecutor.IsSessionExpiredResponse(body).Should().BeTrue(
			because: "responses that reference Creatio's auth-routing namespace (/Login/ or \"bootstrap.login\") must trigger re-authentication");
	}

	[Test]
	[Description("IsSessionExpiredResponse matches the auth-route tokens case-insensitively")]
	public void IsSessionExpiredResponse_ShouldReturnTrue_WhenMarkerCasingDiffersFromCanonicalForm() {
		// Arrange — uppercase /LOGIN/ must still classify as auth-route.
		string body = "<HTML><BODY><FORM ACTION=\"/LOGIN/NUILOGIN.ASPX\"></FORM></BODY></HTML>";

		// Act / Assert
		ReauthExecutor.IsSessionExpiredResponse(body).Should().BeTrue(
			because: "case variations in markup must not bypass detection");
	}

	[Test]
	[Description("IsSessionExpiredResponse ignores auth tokens that appear past the bounded scan window")]
	public void IsSessionExpiredResponse_ShouldReturnFalse_WhenMarkerLiesBeyondScanWindow() {
		// Arrange — /Login/ marker is placed after 5 000 chars of filler, past the 4 KB head.
		string filler = new('x', 5000);
		string body = "<html><body>" + filler + "<a href=\"/Login/NuiLogin.aspx\">here</a></body></html>";

		// Act / Assert
		ReauthExecutor.IsSessionExpiredResponse(body).Should().BeFalse(
			because: "scanning is bounded to the head of the body to prevent unbounded work on large payloads");
	}

	[TestCase("<html><head><title>HTTP Error 500.0 - Internal Server Error</title></head><body><h1>HTTP Error 500.0 - Internal Server Error</h1><p>The page cannot be displayed because an internal server error has occurred.</p></body></html>")]
	[TestCase("<html><head><title>502 Bad Gateway</title></head><body><center><h1>502 Bad Gateway</h1></center><hr><center>nginx</center></body></html>")]
	[TestCase("<html><head><title>Web Application Firewall - Request Blocked</title></head><body><p>Your request was blocked by the WAF.</p></body></html>")]
	[Description("IsSessionExpiredResponse returns false for generic 5xx / proxy / WAF HTML pages — those are NOT session-expired and re-authenticating would not fix them, so the original error must surface to the caller unchanged")]
	public void IsSessionExpiredResponse_ShouldReturnFalse_WhenBodyIsGenericServerErrorHtml(string body) {
		// Act / Assert
		ReauthExecutor.IsSessionExpiredResponse(body).Should().BeFalse(
			because: "generic server-error HTML (500, 502, WAF) must not be misclassified as session-expired — re-auth cannot fix it and the caller must see the original error");
	}

	[Test]
	[Description("IsSessionExpiredResponse returns false for JSON that happens to embed a login marker inside a string")]
	public void IsSessionExpiredResponse_ShouldReturnFalse_WhenJsonPayloadEmbedsMarkerAsStringContent() {
		// Arrange
		string body = "{\"description\":\"<input id=\\\"LoginEdit\\\"/>\"}";

		// Act / Assert
		ReauthExecutor.IsSessionExpiredResponse(body).Should().BeFalse(
			because: "early-exit on the leading JSON brace prevents false positives from marker-shaped string content");
	}

	#endregion

}
