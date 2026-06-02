using System;
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

	private const string LoginPageBody =
		"<!DOCTYPE html><html><head><title>Login</title></head><body>" +
		"<form action=\"/Login/NuiLogin.aspx\"><input id=\"LoginEdit\" name=\"UserName\"/></form></body></html>";

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
		}, ReauthExecutor.IsHtmlLoginPage);

		// Assert
		result.Should().Be("{\"success\":true}",
			because: "a non-login-page response must be returned unchanged");
		callCount.Should().Be(1,
			because: "Execute must not retry when the response is not a login page");
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
		string result = sut.Execute(() => responses[callCount++], ReauthExecutor.IsHtmlLoginPage);

		// Assert
		result.Should().Be("{\"ok\":true}",
			because: "after a single re-auth the retry response must be returned to the caller");
		callCount.Should().Be(2,
			because: "Execute must perform exactly one retry after re-authentication");
		loginCallCount.Should().Be(1,
			because: "Login must be called exactly once per detected expired session");
	}

	[Test]
	[Description("Execute returns the retry response as-is when the retry still produces a login page")]
	public void Execute_ShouldReturnRetryResponseAsIs_WhenRetryAlsoReturnsLoginPage() {
		// Arrange
		int loginCallCount = 0;
		ReauthExecutor sut = CreateExecutor(() => loginCallCount++);
		int callCount = 0;

		// Act
		string result = sut.Execute(() => {
			callCount++;
			return LoginPageBody;
		}, ReauthExecutor.IsHtmlLoginPage);

		// Assert
		result.Should().Be(LoginPageBody,
			because: "if re-auth fails, Execute must propagate the second response unchanged to let the caller decide");
		callCount.Should().Be(2,
			because: "Execute must perform exactly one retry, regardless of the retry's outcome");
		loginCallCount.Should().Be(1,
			because: "Login must not be re-invoked after a single failed retry");
	}

	[Test]
	[Description("Execute propagates the exception thrown by the Login callback")]
	public void Execute_ShouldPropagateException_WhenLoginThrows() {
		// Arrange
		ReauthExecutor sut = CreateExecutor(() => throw new InvalidOperationException("login failed"));

		// Act
		Action act = () => sut.Execute(() => LoginPageBody, ReauthExecutor.IsHtmlLoginPage);

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
		ReauthExecutor sut = CreateExecutor(() => { }, logger);
		int callCount = 0;
		string[] responses = { LoginPageBody, "{}" };

		// Act
		sut.Execute(() => responses[callCount++], ReauthExecutor.IsHtmlLoginPage);

		// Assert
		logger.Received(1).WriteWarning(Arg.Is<string>(s => s.Contains("re-authenticated")));
	}

	[Test]
	[Description("Execute deduplicates Login when two threads concurrently observe a login page")]
	public async Task Execute_ShouldDedupeLogin_WhenTwoCallersHitLoginPageConcurrently() {
		// Arrange — gate Login() of the first thread until both threads have entered the
		// reauth path, then release them so both proceed to retry.
		int loginCallCount = 0;
		ManualResetEventSlim release = new(false);
		int threadsAwaiting = 0;
		ReauthExecutor sut = CreateExecutor(() => {
			if (Interlocked.Increment(ref threadsAwaiting) == 1) {
				release.Wait(TimeSpan.FromSeconds(2));
			}
			Interlocked.Increment(ref loginCallCount);
		});
		bool serverAuthenticated = false;
		string Call() => Volatile.Read(ref serverAuthenticated) ? "{}" : LoginPageBody;

		// Act
		Task<string> a = Task.Run(() => sut.Execute(Call, ReauthExecutor.IsHtmlLoginPage));
		Task<string> b = Task.Run(() => {
			Thread.Sleep(10);
			return sut.Execute(Call, ReauthExecutor.IsHtmlLoginPage);
		});
		await Task.Delay(50);
		Volatile.Write(ref serverAuthenticated, true);
		release.Set();
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
		string r1 = sut.Execute(Call, ReauthExecutor.IsHtmlLoginPage);
		string r2 = sut.Execute(Call, ReauthExecutor.IsHtmlLoginPage);
		string r3 = sut.Execute(Call, ReauthExecutor.IsHtmlLoginPage);

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
		}, ReauthExecutor.IsHtmlLoginPage);

		// Assert
		result.Should().Be(LoginPageBody,
			because: "after one retry the executor must surface the login page to the caller, not loop");
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

	#region Tests: IsHtmlLoginPage

	[Test]
	[Description("IsHtmlLoginPage returns false for null, empty, and whitespace-only bodies")]
	public void IsHtmlLoginPage_ShouldReturnFalse_WhenBodyIsNullEmptyOrWhitespace() {
		// Arrange / Act / Assert
		ReauthExecutor.IsHtmlLoginPage(null).Should().BeFalse(
			because: "a null body cannot be a login page");
		ReauthExecutor.IsHtmlLoginPage(string.Empty).Should().BeFalse(
			because: "an empty body cannot be a login page");
		ReauthExecutor.IsHtmlLoginPage("   ").Should().BeFalse(
			because: "a whitespace-only body has no content and cannot be a login page");
	}

	[TestCase("{\"success\":true}")]
	[TestCase("  {\"success\":true}")]
	[TestCase("[]")]
	[TestCase("  [1,2,3]")]
	[TestCase("\"plain string\"")]
	[TestCase("true")]
	[TestCase("123")]
	[Description("IsHtmlLoginPage returns false for any JSON payload regardless of leading whitespace")]
	public void IsHtmlLoginPage_ShouldReturnFalse_WhenBodyIsJsonPayload(string body) {
		// Act / Assert
		ReauthExecutor.IsHtmlLoginPage(body).Should().BeFalse(
			because: "JSON payloads must never be classified as login pages");
	}

	[Test]
	[Description("IsHtmlLoginPage returns false for arbitrary HTML that lacks Creatio login markers")]
	public void IsHtmlLoginPage_ShouldReturnFalse_WhenHtmlContainsNoLoginMarkers() {
		// Arrange
		string body = "<html><body><h1>Not a login page</h1></body></html>";

		// Act / Assert
		ReauthExecutor.IsHtmlLoginPage(body).Should().BeFalse(
			because: "HTML without Creatio login markers must not be misclassified as an expired session");
	}

	[TestCase("<html><body><form><input id=\"LoginEdit\"/></form></body></html>")]
	[TestCase("<html><body><form><input name=\"UserName\"/></form></body></html>")]
	[TestCase("<form action=\"/Login/NuiLogin.aspx\"></form>")]
	[TestCase("<form action=\"/Login/Login.aspx\"></form>")]
	[TestCase("<html><head><title>Login</title></head><body></body></html>")]
	[TestCase("  \n\t<html><body><form><input id=\"LoginEdit\"/></form></body></html>")]
	[Description("IsHtmlLoginPage returns true when the body contains at least one Creatio login marker")]
	public void IsHtmlLoginPage_ShouldReturnTrue_WhenBodyContainsLoginMarker(string body) {
		// Act / Assert
		ReauthExecutor.IsHtmlLoginPage(body).Should().BeTrue(
			because: "responses with Creatio login markers must trigger re-authentication");
	}

	[Test]
	[Description("IsHtmlLoginPage matches login markers case-insensitively")]
	public void IsHtmlLoginPage_ShouldReturnTrue_WhenMarkerCasingDiffersFromCanonicalForm() {
		// Arrange
		string body = "<HTML><BODY><FORM><INPUT ID=\"LOGINEDIT\"/></FORM></BODY></HTML>";

		// Act / Assert
		ReauthExecutor.IsHtmlLoginPage(body).Should().BeTrue(
			because: "case variations in markup must not bypass detection");
	}

	[Test]
	[Description("IsHtmlLoginPage ignores markers that appear past the bounded scan window")]
	public void IsHtmlLoginPage_ShouldReturnFalse_WhenMarkerLiesBeyondScanWindow() {
		// Arrange — login marker is placed after 5 000 chars of filler, past the 4 KB head.
		string filler = new('x', 5000);
		string body = "<html><body>" + filler + "<input id=\"LoginEdit\"/></body></html>";

		// Act / Assert
		ReauthExecutor.IsHtmlLoginPage(body).Should().BeFalse(
			because: "scanning is bounded to the head of the body to prevent unbounded work on large payloads");
	}

	[Test]
	[Description("IsHtmlLoginPage returns false for JSON that happens to embed a login marker inside a string")]
	public void IsHtmlLoginPage_ShouldReturnFalse_WhenJsonPayloadEmbedsMarkerAsStringContent() {
		// Arrange
		string body = "{\"description\":\"<input id=\\\"LoginEdit\\\"/>\"}";

		// Act / Assert
		ReauthExecutor.IsHtmlLoginPage(body).Should().BeFalse(
			because: "early-exit on the leading JSON brace prevents false positives from marker-shaped string content");
	}

	#endregion

}
