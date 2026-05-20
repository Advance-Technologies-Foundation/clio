using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Orchestrates the layered fallback chain that backs <c>get-component-info</c>:
/// CDN → file cache (<c>~/.clio/cache/component-registry/</c>) → embedded snapshot
/// in <c>clio.dll</c>. AI requests never block on the network: stale cache is returned
/// synchronously while a background refresh runs.
/// </summary>
public interface IComponentRegistryClient {
	/// <summary>
	/// Returns a registry payload for the requested version. The result identifies
	/// which tier of the fallback chain produced the bytes.
	/// </summary>
	Task<ComponentRegistryFetchResult> GetAsync(string requestedVersion, CancellationToken cancellationToken = default);

	/// <summary>
	/// Forces a synchronous refresh of the cache for the given version directly from the CDN,
	/// bypassing the TTL. Used by the <c>clio component-registry refresh</c> CLI verb.
	/// </summary>
	Task<bool> RefreshAsync(string version, CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of a registry fetch. The caller owns the stream and must dispose it.
/// </summary>
/// <param name="Content">Payload stream positioned at the start.</param>
/// <param name="ResolvedVersion">
/// The version that ultimately produced the bytes — may differ from the requested version
/// when the chain fell back to <c>latest.json</c> or to the embedded snapshot.
/// </param>
/// <param name="Source">Which tier of the fallback chain served the bytes.</param>
public sealed record ComponentRegistryFetchResult(
	Stream Content,
	string ResolvedVersion,
	ComponentRegistrySource Source);

public enum ComponentRegistrySource {
	/// <summary>Returned bytes came from a fresh CDN response.</summary>
	Cdn,
	/// <summary>Returned bytes came from the on-disk cache (fresh or stale).</summary>
	FileCache,
	/// <summary>
	/// Returned bytes came from a developer-supplied file pointed to by
	/// <c>CLIO_COMPONENT_REGISTRY_LOCAL_FILE</c>, or from a static file shipped
	/// with the install (e.g. the mobile component catalog). Never cached.
	/// </summary>
	Local
}

/// <summary>
/// HTTP-backed implementation of <see cref="IComponentRegistryClient"/>.
/// </summary>
public sealed class ComponentRegistryClient : IComponentRegistryClient {
	internal const string HttpClientName = "component-registry";

	// The default CDN base URL is a deliberate product constant — it identifies the
	// public academy.creatio.com endpoint that ships the curated catalog. It is
	// overridable at runtime via CdnBaseUrlEnvironmentVariable for dev/staging.
	[SuppressMessage("Major Code Smell", "S1075:URIs should not be hardcoded",
		Justification = "This is the product's well-known CDN endpoint, not a configurable resource path. Overridable at runtime via " + CdnBaseUrlEnvironmentVariable + ".")]
	internal const string DefaultCdnBaseUrl = "https://academy.creatio.com/api/mcp/";

	internal const string CdnRegistryFileName = "ComponentRegistry.json";
	internal const string CdnBaseUrlEnvironmentVariable = "CLIO_COMPONENT_REGISTRY_CDN_BASE_URL";
	internal const string LocalFileEnvironmentVariable = "CLIO_COMPONENT_REGISTRY_LOCAL_FILE";
	internal const string LatestVersion = "latest";
	internal const int CdnFetchAttempts = 3;
	internal static readonly TimeSpan CdnFetchTimeout = TimeSpan.FromSeconds(30);

	private static readonly SemaphoreSlim BackgroundRefreshGate = new(initialCount: 1, maxCount: 1);

	private readonly IHttpClientFactory _httpClientFactory;
	private readonly IComponentRegistryCacheStore _cacheStore;
	private readonly IFileSystem _fileSystem;
	private readonly ILogger<ComponentRegistryClient> _logger;
	private readonly string _cdnBaseUrl;

	public ComponentRegistryClient(
		IHttpClientFactory httpClientFactory,
		IComponentRegistryCacheStore cacheStore,
		IFileSystem fileSystem,
		ILogger<ComponentRegistryClient> logger)
		: this(httpClientFactory, cacheStore, fileSystem, logger, ResolveCdnBaseUrl()) {
	}

	internal ComponentRegistryClient(
		IHttpClientFactory httpClientFactory,
		IComponentRegistryCacheStore cacheStore,
		IFileSystem fileSystem,
		ILogger<ComponentRegistryClient> logger,
		string cdnBaseUrl) {
		_httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
		_cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_cdnBaseUrl = string.IsNullOrWhiteSpace(cdnBaseUrl) ? DefaultCdnBaseUrl : cdnBaseUrl;
		if (!_cdnBaseUrl.EndsWith('/')) {
			_cdnBaseUrl += "/";
		}
	}

	/// <inheritdoc />
	public async Task<ComponentRegistryFetchResult> GetAsync(string requestedVersion, CancellationToken cancellationToken = default) {
		if (string.IsNullOrWhiteSpace(requestedVersion)) {
			requestedVersion = LatestVersion;
		}

		// Tier 0: developer override via CLIO_COMPONENT_REGISTRY_LOCAL_FILE. Read every call so
		// editing the file is visible to a long-running `clio mcp serve` without restart. Never
		// written to cache — the override stays a read-only test channel.
		Stream? local = TryOpenLocalOverride(requestedVersion);
		if (local is not null) {
			return new ComponentRegistryFetchResult(local, requestedVersion, ComponentRegistrySource.Local);
		}

		// Tier 1: cache hit for the requested version. Stale cache is served immediately
		// (stale-while-revalidate); the background refresh closes the freshness gap without
		// blocking the AI call.
		ComponentRegistryCacheReadResult? cached = await _cacheStore
			.TryReadAsync(requestedVersion, cancellationToken)
			.ConfigureAwait(false);
		if (cached is { IsFresh: true }) {
			_logger.LogInformation(
				"component-registry source=cache version={Version} stale=false expiresAt={ExpiresAt:o}",
				requestedVersion, cached.ExpiresAt);
			return new ComponentRegistryFetchResult(cached.Content, requestedVersion, ComponentRegistrySource.FileCache);
		}

		if (cached is not null) {
			_logger.LogInformation(
				"component-registry source=cache version={Version} stale=true expiresAt={ExpiresAt:o}",
				requestedVersion, cached.ExpiresAt);
			ScheduleBackgroundRefresh(requestedVersion);
			return new ComponentRegistryFetchResult(cached.Content, requestedVersion, ComponentRegistrySource.FileCache);
		}

		// Tier 2: synchronous CDN fetch for the requested version.
		Stream? cdnStream = await TryFetchFromCdnAsync(requestedVersion, cancellationToken).ConfigureAwait(false);
		if (cdnStream is not null) {
			return new ComponentRegistryFetchResult(cdnStream, requestedVersion, ComponentRegistrySource.Cdn);
		}

		// Tier 3: cache fallback for "latest" — covers cold-start with no network for the
		// specific version but with a previously cached "latest" sitting on disk.
		if (!string.Equals(requestedVersion, LatestVersion, StringComparison.OrdinalIgnoreCase)) {
			ComponentRegistryCacheReadResult? cachedLatest = await _cacheStore
				.TryReadAsync(LatestVersion, cancellationToken)
				.ConfigureAwait(false);
			if (cachedLatest is not null) {
				_logger.LogInformation(
					"component-registry source=cache version=latest fallbackFrom={Requested} stale={Stale}",
					requestedVersion, !cachedLatest.IsFresh);
				if (!cachedLatest.IsFresh) {
					ScheduleBackgroundRefresh(LatestVersion);
				}
				return new ComponentRegistryFetchResult(cachedLatest.Content, LatestVersion, ComponentRegistrySource.FileCache);
			}

			Stream? cdnLatestStream = await TryFetchFromCdnAsync(LatestVersion, cancellationToken).ConfigureAwait(false);
			if (cdnLatestStream is not null) {
				return new ComponentRegistryFetchResult(cdnLatestStream, LatestVersion, ComponentRegistrySource.Cdn);
			}
		}

		// Tier exhausted: both the requested version and the latest alias miss in both
		// the file cache and on the CDN. There is no in-DLL snapshot to fall back to,
		// so surface a clear error. ComponentInfoTool's catch-all turns this into a
		// graceful MCP response that points the operator at the local-override env var.
		_logger.LogWarning(
			"component-registry source=unavailable fallbackFrom={Requested} cdn={CdnBaseUrl}",
			requestedVersion, _cdnBaseUrl);
		throw new ComponentRegistryUnavailableException(requestedVersion, _cdnBaseUrl);
	}

	/// <inheritdoc />
	public async Task<bool> RefreshAsync(string version, CancellationToken cancellationToken = default) {
		if (string.IsNullOrWhiteSpace(version)) {
			version = LatestVersion;
		}

		Stream? fetched = await TryFetchFromCdnAsync(version, cancellationToken).ConfigureAwait(false);
		if (fetched is null) {
			return false;
		}

		await fetched.DisposeAsync().ConfigureAwait(false);
		return true;
	}

	private async Task<Stream?> TryFetchFromCdnAsync(string version, CancellationToken cancellationToken) {
		HttpClient http = _httpClientFactory.CreateClient(HttpClientName);
		http.Timeout = CdnFetchTimeout;
		// CDN URL layout: {base}{version}/ComponentRegistry.json — the version is a directory
		// containing the fixed-name registry file (matches the layout in the static-files-mcp
		// GitLab repo that the academy edge mirrors every 5 minutes).
		string url = BuildCdnUrl(version);

		for (int attempt = 1; attempt <= CdnFetchAttempts; attempt++) {
			FetchAttemptResult result = await TryFetchOnceAsync(http, url, version, attempt, cancellationToken).ConfigureAwait(false);
			if (result.Stream is not null) {
				return result.Stream;
			}
			if (!result.ShouldRetry) {
				return null;
			}
			if (attempt < CdnFetchAttempts && !await DelayBackoffAsync(attempt, cancellationToken).ConfigureAwait(false)) {
				return null;
			}
		}

		return null;
	}

	private async Task<FetchAttemptResult> TryFetchOnceAsync(HttpClient http, string url, string version, int attempt, CancellationToken cancellationToken) {
		try {
			using HttpResponseMessage response = await http
				.GetAsync(url, HttpCompletionOption.ResponseContentRead, cancellationToken)
				.ConfigureAwait(false);

			if ((int)response.StatusCode is >= 200 and < 300) {
				Stream payloadStream = await CacheAndReturnStreamAsync(response, version, attempt, cancellationToken).ConfigureAwait(false);
				return FetchAttemptResult.Success(payloadStream);
			}

			// 4xx is a permanent failure — version missing on CDN, malformed URL, etc.
			// Do not retry; the caller will fall through to the next fallback tier.
			if (response.StatusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError) {
				_logger.LogInformation(
					"component-registry cdn-skip version={Version} status={Status}",
					version, (int)response.StatusCode);
				return FetchAttemptResult.PermanentFailure;
			}

			_logger.LogInformation(
				"component-registry cdn-retry version={Version} status={Status} attempt={Attempt}",
				version, (int)response.StatusCode, attempt);
			return FetchAttemptResult.TransientFailure;
		} catch (HttpRequestException ex) {
			_logger.LogInformation(ex,
				"component-registry cdn-retry version={Version} attempt={Attempt} error={Error}",
				version, attempt, ex.Message);
			return FetchAttemptResult.TransientFailure;
		} catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested) {
			_logger.LogInformation(ex,
				"component-registry cdn-retry version={Version} attempt={Attempt} reason=timeout",
				version, attempt);
			return FetchAttemptResult.TransientFailure;
		}
	}

	private async Task<Stream> CacheAndReturnStreamAsync(HttpResponseMessage response, string version, int attempt, CancellationToken cancellationToken) {
		byte[] payload = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
		await _cacheStore.WriteAsync(
			version,
			payload,
			response.Headers.ETag,
			response.Content.Headers.LastModified,
			cancellationToken).ConfigureAwait(false);
		_logger.LogInformation(
			"component-registry source=cdn version={Version} status={Status} attempt={Attempt} bytes={Bytes}",
			version, (int)response.StatusCode, attempt, payload.Length);
		return new MemoryStream(payload, writable: false);
	}

	private static async Task<bool> DelayBackoffAsync(int attempt, CancellationToken cancellationToken) {
		TimeSpan backoff = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
		try {
			await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
			return true;
		} catch (TaskCanceledException) {
			return false;
		}
	}

	private readonly record struct FetchAttemptResult(Stream? Stream, bool ShouldRetry) {
		public static FetchAttemptResult Success(Stream stream) => new(stream, ShouldRetry: false);
		public static FetchAttemptResult PermanentFailure { get; } = new(Stream: null, ShouldRetry: false);
		public static FetchAttemptResult TransientFailure { get; } = new(Stream: null, ShouldRetry: true);
	}

	private void ScheduleBackgroundRefresh(string version) {
		// Fire-and-forget. We gate concurrency with a single semaphore so a flurry of stale
		// reads does not produce a thundering herd of background CDN fetches.
		_ = Task.Run(async () => {
			if (!await BackgroundRefreshGate.WaitAsync(0).ConfigureAwait(false)) {
				return;
			}
			try {
				using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
				await TryFetchFromCdnAsync(version, cts.Token).ConfigureAwait(false);
			} catch (Exception ex) {
				_logger.LogInformation(ex,
					"component-registry background-refresh-failed version={Version}", version);
			} finally {
				BackgroundRefreshGate.Release();
			}
		});
	}

	private Stream? TryOpenLocalOverride(string requestedVersion) {
		string? path = Environment.GetEnvironmentVariable(LocalFileEnvironmentVariable);
		if (string.IsNullOrWhiteSpace(path)) {
			return null;
		}

		// Fail-fast: a non-empty env var means the developer explicitly opted into reading
		// from disk. If the file is missing or unreadable, surface the problem instead of
		// silently serving CDN data — that masks override mistakes and produces stale,
		// confusing results in long-running `clio mcp serve` sessions.
		if (!_fileSystem.File.Exists(path)) {
			_logger.LogWarning(
				"component-registry local-override-missing version={Version} path={Path}",
				requestedVersion, path);
			throw new FileNotFoundException(
				$"Component registry override file does not exist: '{path}'. " +
				$"Either fix the path or unset {LocalFileEnvironmentVariable} to fall back to the CDN/cache chain.",
				path);
		}

		Stream stream = _fileSystem.File.OpenRead(path);
		_logger.LogInformation(
			"component-registry source=local version={Version} path={Path}",
			requestedVersion, path);
		return stream;
	}

	private static string ResolveCdnBaseUrl() {
		string? envOverride = Environment.GetEnvironmentVariable(CdnBaseUrlEnvironmentVariable);
		return string.IsNullOrWhiteSpace(envOverride) ? DefaultCdnBaseUrl : envOverride;
	}

	// Builds {base}{version}/ComponentRegistry.json through Uri composition.
	// The relative path is a single interpolated string passed to Uri's relative-path
	// constructor, so the slash is part of a URL-protocol path token rather than a
	// free-floating concatenation separator (RFC 3986 path delimiter, always '/').
	private string BuildCdnUrl(string version) {
		Uri baseUri = new(_cdnBaseUrl, UriKind.Absolute);
		Uri relativeUri = new($"{version}/{CdnRegistryFileName}", UriKind.Relative);
		return new Uri(baseUri, relativeUri).AbsoluteUri;
	}
}

/// <summary>
/// Thrown when <see cref="ComponentRegistryClient.GetAsync"/> exhausts every tier in the
/// fallback chain — neither the file cache nor the CDN can serve the requested version
/// or the <c>latest</c> alias, and no <c>CLIO_COMPONENT_REGISTRY_LOCAL_FILE</c> override
/// is set. <see cref="ComponentInfoTool"/>'s catch-all turns this into a graceful MCP
/// response whose <c>error</c> field carries the same diagnostic, so AI agents see a
/// clear actionable message instead of a hanging call.
/// </summary>
public sealed class ComponentRegistryUnavailableException : InvalidOperationException {
	public ComponentRegistryUnavailableException(string requestedVersion, string cdnBaseUrl)
		: base(BuildMessage(requestedVersion, cdnBaseUrl)) {
		RequestedVersion = requestedVersion;
		CdnBaseUrl = cdnBaseUrl;
	}

	/// <summary>The version that was originally requested when the chain ran out of tiers.</summary>
	public string RequestedVersion { get; }

	/// <summary>The CDN base URL that the client failed to reach.</summary>
	public string CdnBaseUrl { get; }

	private static string BuildMessage(string requestedVersion, string cdnBaseUrl) =>
		$"Component registry version '{requestedVersion}' is unavailable: file cache is empty and the CDN at '{cdnBaseUrl}' could not be reached. " +
		$"Set {ComponentRegistryClient.LocalFileEnvironmentVariable} to a local registry JSON for offline development, or retry once connectivity is restored.";
}
