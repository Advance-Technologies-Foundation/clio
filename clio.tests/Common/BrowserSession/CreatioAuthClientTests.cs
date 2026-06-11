using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.BrowserSession;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.BrowserSession;

/// <summary>
/// Story 2 (browser-session-handoff): forms-auth login via a dedicated HTTP client, IsNetCore-aware
/// login URL, cookie harvesting, fail-closed for OAuth-only/incomplete envs, sanitized exceptions.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class CreatioAuthClientTests {

	private sealed class StubHandler : HttpMessageHandler {
		private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
		public Uri CapturedUri { get; private set; }
		public string CapturedBody { get; private set; }
		public bool WasCalled { get; private set; }

		public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
			WasCalled = true;
			CapturedUri = request.RequestUri;
			CapturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
			return _responder(request);
		}
	}

	private static HttpResponseMessage Ok(string body, params string[] setCookies) {
		var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
		foreach (string cookie in setCookies) {
			response.Headers.TryAddWithoutValidation("Set-Cookie", cookie);
		}
		return response;
	}

	private static (CreatioAuthClient sut, StubHandler handler, ILogger logger) BuildSut(
		Func<HttpRequestMessage, HttpResponseMessage> responder) {
		var handler = new StubHandler(responder);
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(CreatioAuthClient.HttpClientName).Returns(_ => new HttpClient(handler));
		var logger = Substitute.For<ILogger>();
		var sut = new CreatioAuthClient(factory, logger);
		return (sut, handler, logger);
	}

	private static EnvironmentSettings Env(string uri, bool isNetCore,
		string login = "Supervisor", string password = "Supervisor", string clientId = null) =>
		new() { Uri = uri, IsNetCore = isNetCore, Login = login, Password = password, ClientId = clientId };

	[Test]
	[Description("On a NetCore environment the login POST targets {Uri}/ServiceModel/AuthService.svc/Login (no 0/ prefix).")]
	public void LoginAsync_ShouldPostToUnprefixedUrl_WhenNetCoreEnvironment() {
		// Arrange
		(CreatioAuthClient sut, StubHandler handler, _) = BuildSut(_ => Ok("{\"Code\":0}", ".ASPXAUTH=a; path=/"));

		// Act
		_ = sut.LoginAsync(Env("https://dev.creatio.com", isNetCore: true)).GetAwaiter().GetResult();

		// Assert
		handler.CapturedUri.ToString().Should().Be("https://dev.creatio.com/ServiceModel/AuthService.svc/Login",
			"because NetCore environments serve AuthService at the site root with no 0/ alias");
	}

	[Test]
	[Description("On a NetFW environment the login POST also targets the site root {Uri}/ServiceModel/AuthService.svc/Login (NO 0/ prefix). Live-confirmed 2026-06-10: the 0/-prefixed login path returns 401; the root path returns 200 + Set-Cookie.")]
	public void LoginAsync_ShouldPostToSiteRootUrl_WhenNetFwEnvironment() {
		// Arrange
		(CreatioAuthClient sut, StubHandler handler, _) = BuildSut(_ => Ok("{\"Code\":0}", ".ASPXAUTH=a; path=/"));

		// Act
		_ = sut.LoginAsync(Env("https://prod.creatio.com", isNetCore: false)).GetAwaiter().GetResult();

		// Assert
		handler.CapturedUri.ToString().Should().Be("https://prod.creatio.com/ServiceModel/AuthService.svc/Login",
			"because AuthService.svc/Login is served at the site root on BOTH hosts — the 0/ alias is only for the Shell/data services");
	}

	[Test]
	[Description("A successful login (Code 0) harvests the Set-Cookie response headers into the storageState result.")]
	public void LoginAsync_ShouldHarvestCookies_WhenLoginSucceeds() {
		// Arrange
		(CreatioAuthClient sut, _, _) = BuildSut(_ => Ok("{\"Code\":0}",
			".ASPXAUTH=auth-token; domain=dev.creatio.com; path=/; HttpOnly",
			"BPMCSRF=csrf-token; path=/"));

		// Act
		StorageStateResult result = sut.LoginAsync(Env("https://dev.creatio.com", isNetCore: true)).GetAwaiter().GetResult();

		// Assert
		result.Cookies.Should().HaveCount(2, "because both Set-Cookie headers are harvested");
		result.Cookies.Should().Contain(c => c.Name == ".ASPXAUTH" && c.Value == "auth-token" && c.HttpOnly,
			"because the auth cookie is parsed with its attributes");
		result.Cookies.Should().Contain(c => c.Name == "BPMCSRF" && c.Value == "csrf-token",
			"because the CSRF cookie is harvested too");
	}

	[Test]
	[Description("An OAuth-only environment (no Login/Password) fails closed without making any HTTP request.")]
	public void LoginAsync_ShouldFailClosedAndNotRequestRemote_WhenOAuthOnly() {
		// Arrange
		(CreatioAuthClient sut, StubHandler handler, _) = BuildSut(_ => Ok("{\"Code\":0}"));
		EnvironmentSettings env = Env("https://dev.creatio.com", isNetCore: true, login: null, password: null, clientId: "client");

		// Act
		Action act = () => sut.LoginAsync(env).GetAwaiter().GetResult();

		// Assert
		act.Should().Throw<CreatioAuthenticationException>(
			"because there is no OAuth token→cookie path (Story-11 NO-GO); OAuth-only envs are unsupported");
		handler.WasCalled.Should().BeFalse("because no request must be attempted for an unsupported environment");
	}

	[Test]
	[Description("An environment with a login but no password fails closed without a request.")]
	public void LoginAsync_ShouldFailClosed_WhenLoginWithoutPassword() {
		// Arrange
		(CreatioAuthClient sut, StubHandler handler, _) = BuildSut(_ => Ok("{\"Code\":0}"));
		EnvironmentSettings env = Env("https://dev.creatio.com", isNetCore: true, password: null);

		// Act
		Action act = () => sut.LoginAsync(env).GetAwaiter().GetResult();

		// Assert
		act.Should().Throw<CreatioAuthenticationException>("because incomplete forms-auth credentials cannot authenticate");
		handler.WasCalled.Should().BeFalse("because no request is attempted with incomplete credentials");
	}

	[Test]
	[Description("A failed login (Code != 0) throws the canonical invalid-credentials error.")]
	public void LoginAsync_ShouldThrowInvalidCredentials_WhenAuthCodeNonZero() {
		// Arrange
		(CreatioAuthClient sut, _, _) = BuildSut(_ => Ok("{\"Code\":1,\"Message\":\"bad\"}"));

		// Act
		Action act = () => sut.LoginAsync(Env("https://dev.creatio.com", isNetCore: true)).GetAwaiter().GetResult();

		// Assert
		act.Should().Throw<CreatioAuthenticationException>()
			.WithMessage("*check username and password*", "because a non-zero auth code means the credentials were rejected");
	}

	[Test]
	[Description("A network failure surfaces a sanitized connectivity error, distinct from an auth rejection.")]
	public void LoginAsync_ShouldThrowConnectivity_WhenTransportFails() {
		// Arrange
		(CreatioAuthClient sut, _, _) = BuildSut(_ => throw new HttpRequestException("connection refused"));

		// Act
		Action act = () => sut.LoginAsync(Env("https://dev.creatio.com", isNetCore: true)).GetAwaiter().GetResult();

		// Assert
		act.Should().Throw<CreatioAuthenticationException>()
			.WithMessage("*could not reach Creatio*", "because a transport failure is reported distinctly from bad credentials");
	}

	[Test]
	[Description("Cookie values never reach any log sink; only cookie names may be logged.")]
	public void LoginAsync_ShouldNotLogCookieValues_WhenLoginSucceeds() {
		// Arrange
		const string sentinel = "SECRET-ASPXAUTH-VALUE-XYZ";
		(CreatioAuthClient sut, _, ILogger logger) = BuildSut(_ => Ok("{\"Code\":0}", $".ASPXAUTH={sentinel}; path=/"));

		// Act
		StorageStateResult loginResult = sut.LoginAsync(Env("https://dev.creatio.com", isNetCore: true)).GetAwaiter().GetResult();

		// Assert
		loginResult.Should().NotBeNull("because a successful login must produce a storage-state result");
		logger.DidNotReceive().WriteDebug(Arg.Is<string>(s => s.Contains(sentinel)));
		logger.DidNotReceive().WriteInfo(Arg.Is<string>(s => s.Contains(sentinel)));
		logger.DidNotReceive().WriteWarning(Arg.Is<string>(s => s.Contains(sentinel)));
		logger.DidNotReceive().WriteError(Arg.Is<string>(s => s.Contains(sentinel)));
	}

	[Test]
	[Description("A thrown auth exception's full ToString() contains no password or cookie material — safe under --debug.")]
	public void LoginAsync_ShouldNotLeakSecretsInException_WhenAuthFails() {
		// Arrange
		const string secretPassword = "TopSecretPassword123";
		(CreatioAuthClient sut, _, _) = BuildSut(_ => Ok("{\"Code\":1}", ".ASPXAUTH=leaky-cookie-value"));
		EnvironmentSettings env = Env("https://dev.creatio.com", isNetCore: true, password: secretPassword);

		// Act
		CreatioAuthenticationException ex = Assert.Throws<CreatioAuthenticationException>(
			() => sut.LoginAsync(env).GetAwaiter().GetResult());

		// Assert
		ex.ToString().Should().NotContain(secretPassword, "because the password must never appear in an exception");
		ex.ToString().Should().NotContain("leaky-cookie-value", "because cookie material must never appear in an exception");
	}

	[Test]
	[Description("StorageStateResult serialises to valid Playwright storageState JSON (cookies array + empty origins).")]
	public void Serialize_ShouldProduceValidPlaywrightStorageState_WhenCookiesPresent() {
		// Arrange
		var result = new StorageStateResult([
			new BrowserCookie(".ASPXAUTH", "v1", "dev.creatio.com", "/", HttpOnly: true, Secure: false, "Lax", -1)
		]);

		// Act
		string json = StorageStateJson.Serialize(result);

		// Assert
		using JsonDocument doc = JsonDocument.Parse(json);
		JsonElement cookie = doc.RootElement.GetProperty("cookies")[0];
		cookie.GetProperty("name").GetString().Should().Be(".ASPXAUTH", "because the Playwright shape uses camelCase 'name'");
		cookie.GetProperty("value").GetString().Should().Be("v1", "because the cookie value is preserved");
		cookie.GetProperty("httpOnly").GetBoolean().Should().BeTrue("because the httpOnly flag is carried through");
		cookie.GetProperty("sameSite").GetString().Should().Be("Lax", "because Playwright requires a sameSite value");
		doc.RootElement.TryGetProperty("origins", out JsonElement origins).Should().BeTrue("because storageState requires an origins array");
		origins.GetArrayLength().Should().Be(0, "because Creatio forms-auth has no localStorage origins");
	}
}
