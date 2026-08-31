using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.BrowserSession;
using Creatio.Client;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.BrowserSession;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class BrowserSessionServiceTests {
	private const string Key = "dev-creatio-com_0123456789abcdef";
	private static readonly string CachedPath =
		System.IO.Path.Combine("home", ".clio", "sessions", Key + ".storageState.json");

	private IApplicationClientFactory _applicationClientFactory;
	private IOwnedApplicationClient _client;
	private IBrowserSessionCache _cache;
	private IFileSystem _fileSystem;
	private BrowserSessionService _sut;

	[SetUp]
	public void SetUp() {
		_applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		_client = Substitute.For<IOwnedApplicationClient>();
		_applicationClientFactory.CreateFormsEnvironmentClient(Arg.Any<EnvironmentSettings>(), false)
			.Returns(_client);
		_client.ExportSessionCookies().Returns([
			new CreatioSessionCookie(".ASPXAUTH", "session-token", "dev.creatio.com", "/", true,
				false, "Strict", DateTime.MinValue),
			new CreatioSessionCookie("CRT_CSRF", "csrf-token", "dev.creatio.com", "/", false,
				true, "None", DateTime.MinValue)
		]);
		_client.LoginAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromResult(Response(HttpStatusCode.OK, "{\"Code\":0}")));
		_client.ExecuteGetRequestAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromResult(Response(HttpStatusCode.OK, "<html><body>Home</body></html>")));

		_cache = Substitute.For<IBrowserSessionCache>();
		_fileSystem = Substitute.For<IFileSystem>();
		_cache.BuildKey(Arg.Any<EnvironmentSettings>()).Returns(Key);
		_cache.GetPath(Key).Returns(CachedPath);
		_fileSystem.ReadAllText(CachedPath).Returns(StorageStateJson.Serialize(new StorageStateResult([
			new BrowserCookie(".ASPXAUTH", "cached", "dev.creatio.com", "/", true, false, "Lax", -1)
		])));
		_sut = new BrowserSessionService(_applicationClientFactory, _cache, _fileSystem);
	}

	[TearDown]
	public void TearDown() {
		_client.Dispose();
		_applicationClientFactory.ClearReceivedCalls();
		_client.ClearReceivedCalls();
		_cache.ClearReceivedCalls();
	}

	private static EnvironmentSettings Env() =>
		new() { Uri = "https://dev.creatio.com", Login = "u", Password = "p" };

	private static HttpResponseMessage Response(HttpStatusCode statusCode, string body) =>
		new(statusCode) { Content = new StringContent(body) };

	private void StubCacheHit() =>
		_cache.TryRead(Key, out Arg.Any<string>()).Returns(call => { call[1] = CachedPath; return true; });

	private void StubCacheMiss() =>
		_cache.TryRead(Key, out Arg.Any<string>()).Returns(call => { call[1] = null; return false; });

	[Test]
	[Description("A valid cached browser session is imported into CreatioClient, refreshed, and reused.")]
	public async Task GetSessionPathAsync_ShouldReuseImportedCookies_WhenCachedSessionIsValid() {
		// Arrange
		StubCacheHit();

		// Act
		string path = await _sut.GetSessionPathAsync(Env());

		// Assert
		path.Should().Be(CachedPath, because: "the validated cache path remains the browser handoff artifact");
		_client.Received(1).ImportSessionCookies(Arg.Is<IEnumerable<CreatioSessionCookie>>(cookies =>
			cookies != null && System.Linq.Enumerable.Any(cookies, cookie =>
				cookie.Name == ".ASPXAUTH" && cookie.Value == "cached" && cookie.Domain == "dev.creatio.com"
				&& cookie.Path == "/" && cookie.HttpOnly && !cookie.Secure && cookie.SameSite == "Lax"
				&& cookie.Expires == DateTime.MinValue)));
		await _client.Received(1).ExecuteGetRequestAsync(Arg.Any<string>(), 30_000, 1, 1,
			Arg.Any<CancellationToken>());
		await _client.DidNotReceive().LoginAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
		_cache.Received(1).Write(Key, Arg.Is<string>(json => json.Contains("\"sameSite\":\"Strict\"",
			StringComparison.Ordinal) && json.Contains("\"sameSite\":\"None\"", StringComparison.Ordinal)), null);
		((IDisposable)_client).Received(1).Dispose();
	}

	[Test]
	[Description("A cache miss logs in through CreatioClient and exports its cookies to browser storage.")]
	public async Task GetSessionPathAsync_ShouldLoginAndExportCookies_WhenCacheMiss() {
		// Arrange
		StubCacheMiss();

		// Act
		string path = await _sut.GetSessionPathAsync(Env());

		// Assert
		await _client.Received(1).LoginAsync(30_000, Arg.Any<CancellationToken>());
		_client.Received(1).ExportSessionCookies();
		_cache.Received(1).Write(Key,
			Arg.Is<string>(json => json.Contains("session-token", StringComparison.Ordinal)), null);
		path.Should().Be(CachedPath, because: "the newly written default cache path is returned");
		((IDisposable)_client).Received(1).Dispose();
	}

	[TestCase(HttpStatusCode.Unauthorized, "")]
	[TestCase(HttpStatusCode.Found, "")]
	[TestCase(HttpStatusCode.OK, "<html><a href=\"/Login/Login.html\">login</a></html>")]
	[Description("An expired cached session is removed and replaced through CreatioClient login.")]
	public async Task GetSessionPathAsync_ShouldReauthenticate_WhenCachedSessionIsExpired(
		HttpStatusCode statusCode, string body) {
		// Arrange
		StubCacheHit();
		_client.ExecuteGetRequestAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(Response(statusCode, body)));

		// Act
		_ = await _sut.GetSessionPathAsync(Env());

		// Assert
		_cache.Received(1).Delete(Key);
		await _client.Received(1).LoginAsync(30_000, Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("Force refresh bypasses cache validation and performs one CreatioClient login.")]
	public async Task GetSessionPathAsync_ShouldSkipCacheRead_WhenForceRefreshIsTrue() {
		// Act
		_ = await _sut.GetSessionPathAsync(Env(), forceRefresh: true);

		// Assert
		_cache.DidNotReceive().TryRead(Arg.Any<string>(), out Arg.Any<string>());
		await _client.Received(1).LoginAsync(30_000, Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("An override output path is written even when the imported cache is still valid.")]
	public async Task GetSessionPathAsync_ShouldWriteOverridePath_WhenCacheIsValid() {
		// Arrange
		const string overridePath = "C:/temp/session.storageState.json";
		StubCacheHit();

		// Act
		string path = await _sut.GetSessionPathAsync(Env(), overridePath);

		// Assert
		path.Should().Be(System.IO.Path.GetFullPath(overridePath),
			because: "the caller requested an explicit handoff file");
		_cache.Received(1).Write(Key, Arg.Any<string>(), overridePath);
		await _client.DidNotReceive().LoginAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("OAuth-only environments fail closed before a Creatio client or cache is used.")]
	public async Task GetSessionPathAsync_ShouldFailClosed_WhenFormsCredentialsAreMissing() {
		// Arrange
		EnvironmentSettings environment = new() { Uri = "https://dev.creatio.com", ClientId = "client" };

		// Act
		Func<Task> act = () => _sut.GetSessionPathAsync(environment);

		// Assert
		await act.Should().ThrowAsync<CreatioAuthenticationException>(
			because: "an OAuth token cannot be converted into browser session cookies");
		_applicationClientFactory.DidNotReceive().CreateFormsEnvironmentClient(
			Arg.Any<EnvironmentSettings>(), Arg.Any<bool>());
		_cache.DidNotReceive().BuildKey(Arg.Any<EnvironmentSettings>());
	}

	[Test]
	[Description("Browser session always requests the strict-TLS forms client when mixed credentials are configured.")]
	public async Task GetSessionPathAsync_ShouldSelectFormsClient_WhenEnvironmentAlsoContainsOAuthCredentials() {
		// Arrange
		StubCacheMiss();
		EnvironmentSettings environment = Env();
		environment.ClientId = "oauth-client";
		environment.ClientSecret = "oauth-secret";

		// Act
		_ = await _sut.GetSessionPathAsync(environment);

		// Assert
		_applicationClientFactory.Received(1).CreateFormsEnvironmentClient(environment, false);
		_applicationClientFactory.DidNotReceive().CreateEnvironmentClient(Arg.Any<EnvironmentSettings>());
	}

	[Test]
	[Description("A rejected automatic reauthentication is surfaced without a second explicit login attempt.")]
	public async Task GetSessionPathAsync_ShouldNotLoginTwice_WhenCachedSessionReauthenticationIsRejected() {
		// Arrange
		StubCacheHit();
		_client.ExecuteGetRequestAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<CancellationToken>()).Returns<Task<HttpResponseMessage>>(_ =>
				throw new UnauthorizedAccessException("secret rejection"));

		// Act
		Func<Task> act = () => _sut.GetSessionPathAsync(Env());

		// Assert
		(await act.Should().ThrowAsync<CreatioAuthenticationException>()).Which.Message.Should()
			.NotContain("secret", because: "credential failures must remain sanitized");
		await _client.DidNotReceive().LoginAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("A malformed cached HTTP cookie is removed and replaced by a fresh session.")]
	public async Task GetSessionPathAsync_ShouldRefresh_WhenCachedCookieCannotBeImported() {
		// Arrange
		StubCacheHit();
		_client.When(client => client.ImportSessionCookies(Arg.Any<IEnumerable<CreatioSessionCookie>>()))
			.Do(_ => throw new CookieException("invalid cookie"));

		// Act
		_ = await _sut.GetSessionPathAsync(Env());

		// Assert
		_cache.Received(1).Delete(Key);
		await _client.Received(1).LoginAsync(30_000, Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("An uncancelled operation-canceled login is translated to the established connectivity error.")]
	public async Task GetSessionPathAsync_ShouldMapOperationCanceledException_WhenLoginTimesOut() {
		// Arrange
		StubCacheMiss();
		_client.LoginAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns<Task<HttpResponseMessage>>(_ => throw new OperationCanceledException());

		// Act
		Func<Task> act = () => _sut.GetSessionPathAsync(Env());

		// Assert
		await act.Should().ThrowAsync<CreatioAuthenticationException>(
			because: "transport timeouts keep the browser-session connectivity contract");
	}

	[Test]
	[Description("A successful login without the forms authentication cookie is rejected.")]
	public async Task GetSessionPathAsync_ShouldReject_WhenLoginExportsOnlyNonAuthCookies() {
		// Arrange
		StubCacheMiss();
		_client.ExportSessionCookies().Returns([
			new CreatioSessionCookie("CRT_CSRF", "csrf", "dev.creatio.com", "/", false, true,
				"Strict", DateTime.MinValue)
		]);

		// Act
		Func<Task> act = () => _sut.GetSessionPathAsync(Env());

		// Assert
		await act.Should().ThrowAsync<CreatioAuthenticationException>(
			because: "an affinity or CSRF cookie alone cannot authenticate a browser session");
	}

	[Test]
	[Description("A rejected CreatioClient login is translated to the existing sanitized browser-session error.")]
	public async Task GetSessionPathAsync_ShouldReturnSanitizedFailure_WhenLoginIsRejected() {
		// Arrange
		StubCacheMiss();
		_client.LoginAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns<Task<HttpResponseMessage>>(_ => throw new UnauthorizedAccessException("contains-secret"));

		// Act
		Func<Task> act = () => _sut.GetSessionPathAsync(Env());

		// Assert
		(await act.Should().ThrowAsync<CreatioAuthenticationException>()).Which.Message.Should()
			.NotContain("contains-secret", because: "authentication details must never reach logs or MCP output");
	}

	[Test]
	[Description("ClearSessionAsync deletes both the keyed cache and an explicit output file.")]
	public async Task ClearSessionAsync_ShouldDeleteCacheAndOverride_WhenOutputPathIsProvided() {
		// Arrange
		const string overridePath = "C:/temp/session.storageState.json";

		// Act
		await _sut.ClearSessionAsync(Env(), overridePath);

		// Assert
		_cache.Received(1).Delete(Key);
		_fileSystem.Received(1).DeleteFileIfExists(overridePath);
	}
}
