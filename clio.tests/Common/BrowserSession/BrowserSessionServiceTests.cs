using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.BrowserSession;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.BrowserSession;

/// <summary>
/// Story 4 (browser-session-handoff): the service reuses a valid cached session, re-authenticates
/// when the cache is missing/expired, honors force-refresh, and detects the 200-login-page expiry.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class BrowserSessionServiceTests {

	private sealed class StubHandler : HttpMessageHandler {
		private readonly Func<HttpResponseMessage> _responder;
		public StubHandler(Func<HttpResponseMessage> responder) => _responder = responder;
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
			Task.FromResult(_responder());
	}

	private const string Key = "dev-creatio-com_0123456789abcdef";
	private static readonly string CachedPath =
		System.IO.Path.Combine("home", ".clio", "sessions", Key + ".storageState.json");

	private ICreatioAuthClient _auth;
	private IBrowserSessionCache _cache;
	private IFileSystem _fileSystem;
	private IHttpClientFactory _httpClientFactory;
	private BrowserSessionService _sut;

	[SetUp]
	public void SetUp() {
		_auth = Substitute.For<ICreatioAuthClient>();
		_cache = Substitute.For<IBrowserSessionCache>();
		_fileSystem = Substitute.For<IFileSystem>();
		_httpClientFactory = Substitute.For<IHttpClientFactory>();
		_cache.BuildKey(Arg.Any<EnvironmentSettings>()).Returns(Key);
		_cache.GetPath(Key).Returns(CachedPath);
		_fileSystem.ReadAllText(CachedPath).Returns(
			StorageStateJson.Serialize(new StorageStateResult([
				new BrowserCookie(".ASPXAUTH", "v", "dev.creatio.com", "/", true, false, "Lax", -1)
			])));
		_sut = new BrowserSessionService(_auth, _cache, _fileSystem, _httpClientFactory);
	}

	[TearDown]
	public void TearDown() {
		_auth.ClearReceivedCalls();
		_cache.ClearReceivedCalls();
	}

	private static EnvironmentSettings Env() =>
		new() { Uri = "https://dev.creatio.com", Login = "u", Password = "p" };

	private void StubValidationResponse(HttpStatusCode status, string body) {
		var handler = new StubHandler(() => new HttpResponseMessage(status) { Content = new StringContent(body) });
		_httpClientFactory.CreateClient(CreatioAuthClient.HttpClientName).Returns(_ => new HttpClient(handler));
	}

	private void StubCacheHit() =>
		_cache.TryRead(Key, out Arg.Any<string>()).Returns(ci => { ci[1] = CachedPath; return true; });

	private void StubCacheMiss() =>
		_cache.TryRead(Key, out Arg.Any<string>()).Returns(ci => { ci[1] = null; return false; });

	[Test]
	[Description("On a cache hit whose validation probe shows a live session, the cached path is returned and no login occurs.")]
	public void GetSessionPathAsync_ShouldReturnCachedPath_WhenCacheHitAndSessionValid() {
		// Arrange
		StubCacheHit();
		StubValidationResponse(HttpStatusCode.OK, "<html><body>Home</body></html>"); // no /Login/ → valid

		// Act
		string path = _sut.GetSessionPathAsync(Env()).GetAwaiter().GetResult();

		// Assert
		path.Should().Be(CachedPath, "because a valid cached session is reused verbatim");
		_auth.DidNotReceive().LoginAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("On a cache miss the service authenticates once and writes the fresh session to the cache.")]
	public void GetSessionPathAsync_ShouldLoginAndCache_WhenCacheMiss() {
		// Arrange
		StubCacheMiss();
		_auth.LoginAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<CancellationToken>())
			.Returns(new StorageStateResult([new BrowserCookie(".ASPXAUTH", "v", "d", "/", true, false, "Lax", -1)]));

		// Act
		string path = _sut.GetSessionPathAsync(Env()).GetAwaiter().GetResult();

		// Assert
		_auth.Received(1).LoginAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<CancellationToken>());
		_cache.Received(1).Write(Key, Arg.Any<string>(), null);
		path.Should().Be(CachedPath, "because the freshly cached session path is returned");
	}

	[Test]
	[Description("A cached session that returns the 200 login-page HTML is detected as expired: it is deleted and a fresh login occurs.")]
	public void GetSessionPathAsync_ShouldReauthenticate_WhenCachedSessionReturnsLoginPage() {
		// Arrange
		StubCacheHit();
		StubValidationResponse(HttpStatusCode.OK, "<html><a href=\"/Login/Login.html\">login</a></html>"); // expired
		_auth.LoginAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<CancellationToken>())
			.Returns(new StorageStateResult([new BrowserCookie(".ASPXAUTH", "v2", "d", "/", true, false, "Lax", -1)]));

		// Act
		_ = _sut.GetSessionPathAsync(Env()).GetAwaiter().GetResult();

		// Assert
		_cache.Received(1).Delete(Key);
		_auth.Received(1).LoginAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("A cached session that returns HTTP 401 is treated as expired: deleted and re-authenticated.")]
	public void GetSessionPathAsync_ShouldReauthenticate_WhenValidationReturns401() {
		// Arrange
		StubCacheHit();
		StubValidationResponse(HttpStatusCode.Unauthorized, "");
		_auth.LoginAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<CancellationToken>())
			.Returns(new StorageStateResult([new BrowserCookie(".ASPXAUTH", "v2", "d", "/", true, false, "Lax", -1)]));

		// Act
		_ = _sut.GetSessionPathAsync(Env()).GetAwaiter().GetResult();

		// Assert
		_cache.Received(1).Delete(Key);
		_auth.Received(1).LoginAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("force-refresh bypasses the cache entirely: no cache read, always re-authenticate.")]
	public void GetSessionPathAsync_ShouldLoginAndSkipCacheRead_WhenForceRefresh() {
		// Arrange
		_auth.LoginAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<CancellationToken>())
			.Returns(new StorageStateResult([new BrowserCookie(".ASPXAUTH", "v", "d", "/", true, false, "Lax", -1)]));

		// Act
		_ = _sut.GetSessionPathAsync(Env(), forceRefresh: true).GetAwaiter().GetResult();

		// Assert
		_cache.DidNotReceive().TryRead(Arg.Any<string>(), out Arg.Any<string>());
		_auth.Received(1).LoginAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("ClearSessionAsync deletes the cache entry for the environment's key.")]
	public void ClearSessionAsync_ShouldDeleteCacheEntry_WhenInvoked() {
		// Act
		_sut.ClearSessionAsync(Env()).GetAwaiter().GetResult();

		// Assert
		_cache.Received(1).Delete(Key);
	}

	[Test]
	[Description("ClearSessionAsync also deletes the override-path file when one is supplied, so a credential written via --output-path is fully revoked.")]
	public void ClearSessionAsync_ShouldDeleteOverrideFile_WhenOutputPathIsProvided() {
		// Arrange
		const string overridePath = "/tmp/session.storageState.json";

		// Act
		_sut.ClearSessionAsync(Env(), overridePath).GetAwaiter().GetResult();

		// Assert
		_cache.Received(1).Delete(Key);
		_fileSystem.Received(1).DeleteFileIfExists(overridePath);
	}

	[Test]
	[Description("A cached session that returns HTTP 302 is treated as expired: .NET Framework redirects to the login page rather than serving login HTML, and with AllowAutoRedirect=false the body is empty which would otherwise fool IsSessionExpiredResponse into returning false.")]
	public void GetSessionPathAsync_ShouldReauthenticate_WhenValidationReturns302() {
		// Arrange
		StubCacheHit();
		// Simulate NetFW 302 redirect (no body — empty string would fool IsSessionExpiredResponse).
		var handler = new StubHandler(() => new HttpResponseMessage(HttpStatusCode.Found));
		_httpClientFactory.CreateClient(CreatioAuthClient.HttpClientName).Returns(_ => new HttpClient(handler));
		_auth.LoginAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<CancellationToken>())
			.Returns(new StorageStateResult([new BrowserCookie(".ASPXAUTH", "v2", "d", "/", true, false, "Lax", -1)]));

		// Act
		_ = _sut.GetSessionPathAsync(Env()).GetAwaiter().GetResult();

		// Assert
		_cache.Received(1).Delete(Key);
		_auth.Received(1).LoginAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("An authentication failure from the auth client propagates to the caller.")]
	public void GetSessionPathAsync_ShouldPropagate_WhenLoginThrows() {
		// Arrange
		StubCacheMiss();
		_auth.LoginAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<CancellationToken>())
			.Returns<StorageStateResult>(_ => throw CreatioAuthenticationException.InvalidCredentials("https://dev.creatio.com"));

		// Act
		Action act = () => _sut.GetSessionPathAsync(Env()).GetAwaiter().GetResult();

		// Assert
		act.Should().Throw<CreatioAuthenticationException>(
			"because the service does not swallow authentication failures");
	}
}
