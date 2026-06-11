using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.BrowserSession;

/// <inheritdoc cref="IBrowserSessionService" />
public sealed class BrowserSessionService : IBrowserSessionService {
	private readonly ICreatioAuthClient _authClient;
	private readonly IBrowserSessionCache _cache;
	private readonly IFileSystem _fileSystem;
	private readonly IHttpClientFactory _httpClientFactory;

	/// <summary>Initializes the orchestration service.</summary>
	public BrowserSessionService(ICreatioAuthClient authClient, IBrowserSessionCache cache,
		IFileSystem fileSystem, IHttpClientFactory httpClientFactory) {
		_authClient = authClient;
		_cache = cache;
		_fileSystem = fileSystem;
		_httpClientFactory = httpClientFactory;
	}

	/// <inheritdoc />
	public async Task<string> GetSessionPathAsync(EnvironmentSettings env, string overrideOutputPath = null,
		bool forceRefresh = false, CancellationToken ct = default) {
		ArgumentNullException.ThrowIfNull(env);
		string key = _cache.BuildKey(env);

		if (!forceRefresh && _cache.TryRead(key, out string cachedPath)) {
			if (await IsCachedSessionValidAsync(env, cachedPath, ct).ConfigureAwait(false)) {
				return cachedPath;
			}
			// Expired/invalid cached session: drop it before re-authenticating.
			_cache.Delete(key);
		}

		StorageStateResult result = await _authClient.LoginAsync(env, ct).ConfigureAwait(false);
		string json = StorageStateJson.Serialize(result);
		_cache.Write(key, json, overrideOutputPath);
		return string.IsNullOrWhiteSpace(overrideOutputPath)
			? _cache.GetPath(key)
			: System.IO.Path.GetFullPath(overrideOutputPath);
	}

	/// <inheritdoc />
	public Task ClearSessionAsync(EnvironmentSettings env, string overrideOutputPath = null,
		CancellationToken ct = default) {
		ArgumentNullException.ThrowIfNull(env);
		_cache.Delete(_cache.BuildKey(env));
		if (!string.IsNullOrEmpty(overrideOutputPath)) {
			_fileSystem.DeleteFileIfExists(overrideOutputPath);
		}
		return Task.CompletedTask;
	}

	// Validates a cached session by probing the environment root with the cached cookies. Creatio
	// returns HTTP 200 with the login-page HTML on an expired session (not a 401), so a status-only
	// check is insufficient — reuse the platform-token-based detector.
	private async Task<bool> IsCachedSessionValidAsync(EnvironmentSettings env, string cachedPath, CancellationToken ct) {
		string cookieHeader;
		try {
			cookieHeader = StorageStateJson.ToCookieHeader(_fileSystem.ReadAllText(cachedPath));
		} catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) {
			return false; // corrupt or unreadable cache file → treat as invalid
		}
		if (string.IsNullOrEmpty(cookieHeader) || !Uri.TryCreate(env.Uri, UriKind.Absolute, out _)) {
			return false;
		}
		try {
			HttpClient http = _httpClientFactory.CreateClient(CreatioAuthClient.HttpClientName);
			using var request = new HttpRequestMessage(HttpMethod.Get, env.Uri);
			request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
			using HttpResponseMessage response = await http.SendAsync(request, ct).ConfigureAwait(false);
			if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) {
				return false;
			}
			// On .NET Framework, an expired session commonly triggers a 302 redirect to the login page
			// rather than a 200 with login HTML. With AllowAutoRedirect=false we see the raw 3xx with an
			// empty body; ReauthExecutor.IsSessionExpiredResponse("") returns false, so we must handle
			// the redirect case explicitly here.
			if ((int)response.StatusCode is >= 300 and < 400) {
				return false;
			}
			string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
			return !ReauthExecutor.IsSessionExpiredResponse(body);
		} catch (HttpRequestException) {
			return false;
		} catch (TaskCanceledException) {
			ct.ThrowIfCancellationRequested();
			return false;
		}
	}
}
