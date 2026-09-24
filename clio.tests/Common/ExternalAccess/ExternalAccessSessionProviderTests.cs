using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Clio;
using Clio.Common;
using Clio.Common.BrowserSession;
using Clio.Common.ExternalAccess;
using Creatio.Client;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.ExternalAccess;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
internal sealed class ExternalAccessSessionProviderTests {

	#region Nested: Recording handler

	// Records the request and plays back a scripted response, adding the scripted cookies to the
	// container the provider handed it - which is exactly what HttpClientHandler does for real.
	private sealed class ScriptedHandler : HttpMessageHandler {
		private readonly CookieContainer _cookies;
		private readonly HttpStatusCode _status;
		private readonly string _body;
		private readonly IReadOnlyList<Cookie> _issued;

		public ScriptedHandler(CookieContainer cookies, HttpStatusCode status, string body,
			IReadOnlyList<Cookie> issued) {
			_cookies = cookies;
			_status = status;
			_body = body;
			_issued = issued;
		}

		public HttpRequestMessage LastRequest { get; private set; }

		public string LastRequestBody { get; private set; }

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
			CancellationToken cancellationToken) {
			LastRequest = request;
			LastRequestBody = request.Content is null
				? null
				: await request.Content.ReadAsStringAsync(cancellationToken);
			foreach (Cookie cookie in _issued) {
				_cookies.Add(request.RequestUri, cookie);
			}
			return new HttpResponseMessage(_status) {
				Content = new StringContent(_body, Encoding.UTF8, "application/json")
			};
		}
	}

	// Unlike ScriptedHandler, this one is shared across every request the provider makes and records
	// all of them, so a test can tell an aliveness probe from an exchange instead of counting handler
	// constructions - which the probe and the exchange both trigger.
	private sealed class RecordingHandler : HttpMessageHandler {
		private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

		public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

		public List<(HttpMethod Method, string Uri)> Requests { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
			CancellationToken cancellationToken) {
			Requests.Add((request.Method, request.RequestUri.ToString()));
			return Task.FromResult(_respond(request));
		}

		public bool PostedTheToken => Requests.Any(r => r.Method == HttpMethod.Post
			&& r.Uri.EndsWith(ExternalAccessSessionProvider.OAuthTokenLoginPath, StringComparison.Ordinal));
	}

	#endregion

	#region Methods: Private

	private static Cookie AuthCookie() =>
		new(ExternalAccessSessionProvider.AuthCookieName, "session-value", "/", "target.creatio.com");

	private static EnvironmentSettings Environment() =>
		new() { Uri = "https://target.creatio.com", IsNetCore = false };

	#endregion

	[Test]
	[Description("Exchange posts the token to the site-root OAuthTokenLogin route with a bare integer body")]
	public void Exchange_ShouldPostBearerTokenToSiteRootRoute_WhenTokenIsSupplied() {
		// Arrange
		ScriptedHandler handler = null;
		ExternalAccessSessionProvider sut = new(cookies => handler = new ScriptedHandler(cookies,
			HttpStatusCode.OK, "{\"Code\":0,\"Message\":\"\"}", new[] { AuthCookie() }));

		// Act
		IReadOnlyList<CreatioSessionCookie> result = sut.Exchange(Environment(), "jwt-value");

		// Assert
		handler.LastRequest.RequestUri.ToString().Should()
			.Be("https://target.creatio.com/ServiceModel/AuthService.svc/OAuthTokenLogin",
				because: "the authentication service is served from the site root on both hosts — a '0/' "
					+ "web-app prefix would address a url that does not exist on .NET Framework");
		handler.LastRequest.Method.Should().Be(HttpMethod.Post,
			because: "OAuthTokenLogin is a POST endpoint");
		handler.LastRequest.Headers.Authorization.Scheme.Should().Be("Bearer",
			because: "the platform's own login page sends the external-access token as a bearer credential");
		handler.LastRequest.Headers.Authorization.Parameter.Should().Be("jwt-value",
			because: "the token must reach the site verbatim");
		int.TryParse(handler.LastRequestBody, out int _).Should().BeTrue(
			because: "the service contract is BodyStyle.Bare over a single int time-zone offset, so the "
				+ "body is a bare JSON number rather than an object");
		result.Should().ContainSingle(cookie => cookie.Name == ExternalAccessSessionProvider.AuthCookieName,
			because: "the issued session is what the caller needs out of the exchange");
	}

	[Test]
	[Description("Exchange strips a Bearer prefix supplied by the caller instead of sending it twice")]
	public void Exchange_ShouldStripBearerPrefix_WhenTokenAlreadyCarriesIt() {
		// Arrange
		ScriptedHandler handler = null;
		ExternalAccessSessionProvider sut = new(cookies => handler = new ScriptedHandler(cookies,
			HttpStatusCode.OK, "{\"Code\":0}", new[] { AuthCookie() }));

		// Act
		sut.Exchange(Environment(), "Bearer jwt-value");

		// Assert
		handler.LastRequest.Headers.Authorization.Parameter.Should().Be("jwt-value",
			because: "a token copied out of a header already carries the scheme, and 'Bearer Bearer x' is rejected");
	}

	[Test]
	[Description("Exchange fails when the site reports a refusal inside a successful HTTP response")]
	public void Exchange_ShouldThrow_WhenResponseCarriesNonZeroCode() {
		// Arrange
		ExternalAccessSessionProvider sut = new(cookies => new ScriptedHandler(cookies, HttpStatusCode.OK,
			"{\"Code\":1,\"Message\":\"External access is invalid. Access is inactive\"}", Array.Empty<Cookie>()));

		// Act
		Action act = () => sut.Exchange(Environment(), "jwt-value");

		// Assert
		act.Should().Throw<ExternalAccessLoginException>()
			.WithMessage("*Access is inactive*",
				because: "the platform reports a refused grant as a non-zero Code inside HTTP 200, so reading "
					+ "only the status code would treat an expired grant as a successful login");
	}

	[Test]
	[Description("Exchange fails when the response reports success but issues no authentication cookie")]
	public void Exchange_ShouldThrow_WhenNoAuthCookieIsIssued() {
		// Arrange
		ExternalAccessSessionProvider sut = new(cookies => new ScriptedHandler(cookies, HttpStatusCode.OK,
			"{\"Code\":0}", new[] { new Cookie("BPMCSRF", "csrf", "/", "target.creatio.com") }));

		// Act
		Action act = () => sut.Exchange(Environment(), "jwt-value");

		// Assert
		act.Should().Throw<ExternalAccessLoginException>()
			.WithMessage("*no authentication cookie*",
				because: "creatio.client treats a client as authenticated only when the container holds "
					+ ExternalAccessSessionProvider.AuthCookieName + ", so a session without it is unusable");
	}

	[Test]
	[Description("Exchange fails when the site answers with a non-success status code")]
	public void Exchange_ShouldThrow_WhenStatusCodeIsNotSuccess() {
		// Arrange
		ExternalAccessSessionProvider sut = new(cookies => new ScriptedHandler(cookies,
			HttpStatusCode.Unauthorized, string.Empty, Array.Empty<Cookie>()));

		// Act
		Action act = () => sut.Exchange(Environment(), "jwt-value");

		// Assert
		act.Should().Throw<ExternalAccessLoginException>()
			.WithMessage("*was refused*",
				because: "a rejected token has to surface as an authentication failure naming the site");
	}

	[Test]
	[Description("Exchange refuses an environment without a url instead of building an invalid request")]
	public void Exchange_ShouldThrow_WhenEnvironmentUriIsMissing() {
		// Arrange
		ExternalAccessSessionProvider sut = new(cookies => new ScriptedHandler(cookies, HttpStatusCode.OK,
			"{\"Code\":0}", Array.Empty<Cookie>()));

		// Act
		Action act = () => sut.Exchange(new EnvironmentSettings(), "jwt-value");

		// Assert
		act.Should().Throw<ExternalAccessLoginException>()
			.WithMessage("*url is missing*",
				because: "the caller has to be told which input is absent rather than get a url-format error");
	}

	[Test]
	[Description("An exchanged session survives ImportSessionCookies on a credential-less CreatioClient")]
	public void ImportSessionCookies_ShouldCarryTheExchangedSession_OnACredentiallessClient() {
		// Arrange — the client the factory builds for this mode: no login, no password, no token.
		ExternalAccessSessionProvider provider = new(cookies => new ScriptedHandler(cookies,
			HttpStatusCode.OK, "{\"Code\":0}", new[] { AuthCookie() }));
		IReadOnlyList<CreatioSessionCookie> session = provider.Exchange(Environment(), "jwt-value");
		using CreatioClient client = new("https://target.creatio.com", userName: null, userPassword: null,
			useUntrustedSsl: false, isNetCore: false);

		// Act
		client.ImportSessionCookies(session);

		// Assert
		client.ExportSessionCookies().Should()
			.Contain(cookie => cookie.Name == ExternalAccessSessionProvider.AuthCookieName,
				because: "creatio.client decides a client is already authenticated by finding "
					+ ExternalAccessSessionProvider.AuthCookieName + " in its own cookie container; if the "
					+ "import did not land there the client would attempt a forms login with no credentials "
					+ "and the exchange would look like a bad token");
	}

	#region Methods: Private (cache)

	// A token whose middle segment names the grant, which is what keys the cache entry.
	private static string GrantToken(string accessId) {
		string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(
			"{\"prop:ResourceId\":\"" + accessId + "\"}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		return "header." + payload + ".signature";
	}

	#endregion

	[Test]
	[Description("GetSession caches the exchanged session under a key that carries the grant id")]
	public void GetSession_ShouldCacheExchangedSession_WhenGrantIsReadable() {
		// Arrange
		IBrowserSessionCache cache = Substitute.For<IBrowserSessionCache>();
		cache.BuildKey(Arg.Any<EnvironmentSettings>()).Returns("env-key");
		cache.TryRead(Arg.Any<string>(), out Arg.Any<string>()).Returns(false);
		Clio.Common.IFileSystem fileSystem = Substitute.For<Clio.Common.IFileSystem>();
		ExternalAccessSessionProvider sut = new(
			cookies => new ScriptedHandler(cookies, HttpStatusCode.OK, "{\"Code\":0}", new[] { AuthCookie() }),
			cache, fileSystem);

		// Act
		sut.GetSession(Environment(), GrantToken("62821f5d-8af8-492a-8a82-03b575a94e69"));

		// Assert
		cache.Received(1).Write(
			Arg.Is<string>(key => key == "env-key_ea_62821f5d8af8492a8a8203b575a94e69"),
			Arg.Is<string>(json => json.Contains(ExternalAccessSessionProvider.AuthCookieName)),
			Arg.Any<string>());
	}

	[Test]
	[Description("GetSession does not cache when the token carries no readable grant id")]
	public void GetSession_ShouldNotCache_WhenGrantIsUnreadable() {
		// Arrange
		IBrowserSessionCache cache = Substitute.For<IBrowserSessionCache>();
		Clio.Common.IFileSystem fileSystem = Substitute.For<Clio.Common.IFileSystem>();
		ExternalAccessSessionProvider sut = new(
			cookies => new ScriptedHandler(cookies, HttpStatusCode.OK, "{\"Code\":0}", new[] { AuthCookie() }),
			cache, fileSystem);

		// Act
		sut.GetSession(Environment(), "opaque-not-a-jwt");

		// Assert
		cache.DidNotReceive().Write(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("GetSession discards a cached session the site no longer accepts and exchanges again")]
	public void GetSession_ShouldDropDeadCachedSession_AndExchangeAgain() {
		// Arrange — the cached file holds an auth cookie, but the probe gets the login page back.
		IBrowserSessionCache cache = Substitute.For<IBrowserSessionCache>();
		cache.BuildKey(Arg.Any<EnvironmentSettings>()).Returns("env-key");
		cache.TryRead(Arg.Any<string>(), out Arg.Any<string>())
			.Returns(call => { call[1] = "/cached/session.json"; return true; });
		Clio.Common.IFileSystem fileSystem = Substitute.For<Clio.Common.IFileSystem>();
		fileSystem.ReadAllText("/cached/session.json").Returns(
			"{\"cookies\":[{\"name\":\".ASPXAUTH\",\"value\":\"stale\",\"domain\":\"target.creatio.com\","
			+ "\"path\":\"/\",\"httpOnly\":true,\"secure\":true,\"sameSite\":\"Lax\",\"expires\":-1}],\"origins\":[]}");
		int exchanges = 0;
		List<RecordingHandler> handlers = [];
		ExternalAccessSessionProvider sut = new(
			_ => {
				exchanges++;
				RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized) {
					Content = new StringContent("{}", Encoding.UTF8, "application/json")
				});
				handlers.Add(handler);
				return handler;
			},
			cache, fileSystem);

		// Act
		Action act = () => sut.GetSession(Environment(), GrantToken("62821f5d-8af8-492a-8a82-03b575a94e69"));

		// Assert
		act.Should().Throw<ExternalAccessLoginException>(
			because: "the cached session was rejected and the exchange that followed was rejected too, so "
				+ "there is no session to hand back");
		cache.Received(1).Delete("env-key_ea_62821f5d8af8492a8a8203b575a94e69");
		exchanges.Should().BeGreaterThan(1,
			because: "a dead cached session must not be reused — the probe and the exchange are two "
				+ "separate requests, so the provider went on to try the token");
		handlers.Should().Contain(h => h.PostedTheToken,
			because: "counting handler constructions cannot tell a probe from an exchange — only a POST to "
				+ ExternalAccessSessionProvider.OAuthTokenLoginPath + " proves the token was actually tried");
	}

	[Test]
	[Description("Session reuse is why the feature works at all: the token lives about 120 seconds and the session much longer, so a cached entry whose aliveness probe passes must be handed back WITHOUT a second exchange. This also exercises the StorageStateJson round trip, including the Expires -1 <-> DateTime.MinValue conversion, which nothing else asserts.")]
	public void GetSession_ShouldReuseTheCachedSession_WhenTheProbeSucceeds() {
		// Arrange — the cached file holds a live auth cookie and the shell probe answers normally.
		IBrowserSessionCache cache = Substitute.For<IBrowserSessionCache>();
		cache.BuildKey(Arg.Any<EnvironmentSettings>()).Returns("env-key");
		cache.TryRead(Arg.Any<string>(), out Arg.Any<string>())
			.Returns(call => { call[1] = "/cached/session.json"; return true; });
		Clio.Common.IFileSystem fileSystem = Substitute.For<Clio.Common.IFileSystem>();
		fileSystem.ReadAllText("/cached/session.json").Returns(
			"{\"cookies\":[{\"name\":\".ASPXAUTH\",\"value\":\"cached-session\","
			+ "\"domain\":\"target.creatio.com\",\"path\":\"/\",\"httpOnly\":true,\"secure\":true,"
			+ "\"sameSite\":\"Lax\",\"expires\":-1}],\"origins\":[]}");
		List<RecordingHandler> handlers = [];
		ExternalAccessSessionProvider sut = new(
			_ => {
				RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK) {
					Content = new StringContent("<html>Shell</html>", Encoding.UTF8, "text/html")
				});
				handlers.Add(handler);
				return handler;
			},
			cache, fileSystem);

		// Act
		IReadOnlyList<CreatioSessionCookie> session =
			sut.GetSession(Environment(), GrantToken("62821f5d-8af8-492a-8a82-03b575a94e69"));

		// Assert
		session.Should().ContainSingle(cookie => cookie.Name == ExternalAccessSessionProvider.AuthCookieName
			&& cookie.Value == "cached-session",
			because: "the cached cookies are what the caller reuses — a mapping bug here would silently "
				+ "demand a freshly minted token for every command");
		handlers.Should().NotContain(h => h.PostedTheToken,
			because: "a live cached session must cost no exchange at all; the token is long gone by then");
		cache.DidNotReceive().Delete(Arg.Any<string>());
		cache.DidNotReceive().Write(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
	}
}
