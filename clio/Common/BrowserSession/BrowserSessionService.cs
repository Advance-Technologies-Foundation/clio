using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Creatio.Client;

namespace Clio.Common.BrowserSession;

/// <inheritdoc cref="IBrowserSessionService" />
public sealed class BrowserSessionService : IBrowserSessionService {
	private const int RequestTimeout = 30_000;
	private readonly IApplicationClientFactory _applicationClientFactory;
	private readonly IBrowserSessionCache _cache;
	private readonly IFileSystem _fileSystem;
	// This field exists only to honor the obsolete public constructor for binary-compatible callers.
#pragma warning disable CS0618
	private readonly ICreatioAuthClient _legacyAuthClient;
#pragma warning restore CS0618

	/// <summary>Initializes the orchestration service.</summary>
	public BrowserSessionService(IApplicationClientFactory applicationClientFactory,
		IBrowserSessionCache cache, IFileSystem fileSystem) {
		_applicationClientFactory = applicationClientFactory;
		_cache = cache;
		_fileSystem = fileSystem;
	}

	/// <summary>Retains the historical constructor while routing authentication through CreatioClient.</summary>
	[Obsolete("Use the overload that accepts IApplicationClientFactory.")]
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "S1133:Deprecated code should be removed",
		Justification = "This public constructor is retained intentionally for binary compatibility.")]
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Architecture", "CLIO001:Resolve behavior through DI",
		Justification = "The compatibility constructor must retain its historical public signature.")]
	public BrowserSessionService(ICreatioAuthClient authClient, IBrowserSessionCache cache,
		IFileSystem fileSystem, IHttpClientFactory httpClientFactory)
		: this(new ApplicationClientFactory(new NoReauthExecutor()), cache, fileSystem) {
		ArgumentNullException.ThrowIfNull(authClient);
		ArgumentNullException.ThrowIfNull(httpClientFactory);
		_legacyAuthClient = authClient;
	}

	/// <inheritdoc />
	public async Task<string> GetSessionPathAsync(EnvironmentSettings env, string overrideOutputPath = null,
		bool forceRefresh = false, CancellationToken ct = default) {
		ArgumentNullException.ThrowIfNull(env);
		EnsureFormsCredentials(env);
		string key = _cache.BuildKey(env);

		if (!forceRefresh && _cache.TryRead(key, out string cachedPath)) {
			StorageStateResult cachedSession = await TryReuseCachedSessionAsync(env, cachedPath, ct)
				.ConfigureAwait(false);
			if (cachedSession is not null) {
				string refreshedJson = StorageStateJson.Serialize(cachedSession);
				_cache.Write(key, refreshedJson, overrideOutputPath);
				return string.IsNullOrWhiteSpace(overrideOutputPath)
					? cachedPath
					: System.IO.Path.GetFullPath(overrideOutputPath);
			}
			_cache.Delete(key);
		}

		StorageStateResult result = await LoginAndExportSessionAsync(env, ct).ConfigureAwait(false);
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

	private async Task<StorageStateResult> LoginAndExportSessionAsync(EnvironmentSettings env,
		CancellationToken cancellationToken) {
		if (_legacyAuthClient is not null) {
			return await _legacyAuthClient.LoginAsync(env, cancellationToken).ConfigureAwait(false);
		}
		using IOwnedApplicationClient client = _applicationClientFactory.CreateFormsEnvironmentClient(env);
		try {
			using HttpResponseMessage response = await client.LoginAsync(RequestTimeout, cancellationToken)
				.ConfigureAwait(false);
			if (!response.IsSuccessStatusCode) {
				throw CreatioAuthenticationException.InvalidCredentials(env.Uri);
			}
			return ExportSession(client, env.Uri);
		} catch (UnauthorizedAccessException) {
			throw CreatioAuthenticationException.InvalidCredentials(env.Uri);
		} catch (HttpRequestException) {
			throw CreatioAuthenticationException.Connectivity(env.Uri);
		} catch (OperationCanceledException) {
			cancellationToken.ThrowIfCancellationRequested();
			throw CreatioAuthenticationException.Connectivity(env.Uri);
		}
	}

	private async Task<StorageStateResult> TryReuseCachedSessionAsync(EnvironmentSettings env,
		string cachedPath, CancellationToken cancellationToken) {
		IReadOnlyList<BrowserCookie> cachedCookies;
		try {
			cachedCookies = StorageStateJson.ParseCookies(_fileSystem.ReadAllText(cachedPath));
		} catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) {
			return null;
		}
		if (!cachedCookies.Any(cookie => cookie.Name.Equals(".ASPXAUTH", StringComparison.OrdinalIgnoreCase)
				&& HasLiveExpiry(cookie.Expires))
			|| !Uri.TryCreate(env.Uri, UriKind.Absolute, out Uri environmentUri)) {
			return null;
		}

		try {
			using IOwnedApplicationClient client = _applicationClientFactory.CreateFormsEnvironmentClient(env);
			client.ImportSessionCookies(cachedCookies.Select(cookie => ToSessionCookie(cookie, environmentUri)));
			using HttpResponseMessage response = await client.ExecuteGetRequestAsync(
				AuthenticatedBrowserLauncher.BuildShellUrl(env), RequestTimeout,
				cancellationToken: cancellationToken)
				.ConfigureAwait(false);
			if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
				|| (int)response.StatusCode is >= 300 and < 400) {
				return null;
			}
			string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			return ReauthExecutor.IsSessionExpiredResponse(body) ? null : ExportSession(client, env.Uri);
		} catch (UnauthorizedAccessException) {
			throw CreatioAuthenticationException.InvalidCredentials(env.Uri);
		} catch (HttpRequestException) {
			return null;
		} catch (OperationCanceledException) {
			cancellationToken.ThrowIfCancellationRequested();
			return null;
		} catch (Exception ex) when (ex is ArgumentException or CookieException or FormatException) {
			return null;
		}
	}

	private static bool HasLiveExpiry(double expiry) {
		if (!double.IsFinite(expiry)) {
			return false;
		}
		if (expiry < 0) {
			return true;
		}
		return expiry <= DateTimeOffset.MaxValue.ToUnixTimeSeconds()
			&& expiry > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
	}

	private static StorageStateResult ExportSession(ICreatioApplicationClient client, string environmentUri) {
		IReadOnlyList<CreatioSessionCookie> cookies = client.ExportSessionCookies();
		if (!cookies.Any(cookie => cookie.Name.Equals(".ASPXAUTH", StringComparison.OrdinalIgnoreCase))) {
			throw CreatioAuthenticationException.NoCookies(environmentUri);
		}
		return new StorageStateResult(cookies.Select(ToBrowserCookie).ToList());
	}

	private static CreatioSessionCookie ToSessionCookie(BrowserCookie cookie, Uri environmentUri) => new(
		cookie.Name,
		cookie.Value,
		string.IsNullOrEmpty(cookie.Domain) ? environmentUri.Host : cookie.Domain,
		string.IsNullOrEmpty(cookie.Path) ? "/" : cookie.Path,
		cookie.HttpOnly,
		cookie.Secure,
		cookie.SameSite,
		cookie.Expires < 0
			? DateTime.MinValue
			: DateTimeOffset.FromUnixTimeSeconds((long)cookie.Expires).UtcDateTime);

	private static BrowserCookie ToBrowserCookie(CreatioSessionCookie cookie) => new(
		cookie.Name,
		cookie.Value,
		cookie.Domain,
		string.IsNullOrEmpty(cookie.Path) ? "/" : cookie.Path,
		cookie.HttpOnly,
		cookie.Secure,
		cookie.SameSite,
		cookie.Expires == DateTime.MinValue
			? -1
			: new DateTimeOffset(cookie.Expires.ToUniversalTime()).ToUnixTimeSeconds());

	private static void EnsureFormsCredentials(EnvironmentSettings env) {
		if (string.IsNullOrEmpty(env.Login) || string.IsNullOrEmpty(env.Password)) {
			throw CreatioAuthenticationException.MissingFormsCredentials(env.Uri);
		}
	}
}
