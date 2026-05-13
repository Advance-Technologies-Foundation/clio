using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

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
	/// <summary>Returned bytes came from the embedded resource baked into clio.dll.</summary>
	Embedded
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
	internal const string DefaultCdnBaseUrl = "https://academy.creatio.com/api/component-registry/";

	internal const string CdnBaseUrlEnvironmentVariable = "CLIO_COMPONENT_REGISTRY_CDN_BASE_URL";
	internal const string LatestVersion = "latest";
	internal const int CdnFetchAttempts = 3;
	internal static readonly TimeSpan CdnFetchTimeout = TimeSpan.FromSeconds(30);

	private static readonly SemaphoreSlim BackgroundRefreshGate = new(initialCount: 1, maxCount: 1);

	private readonly IHttpClientFactory _httpClientFactory;
	private readonly IComponentRegistryCacheStore _cacheStore;
	private readonly IEmbeddedRegistryReader _embeddedReader;
	private readonly ILogger<ComponentRegistryClient> _logger;
	private readonly string _cdnBaseUrl;

	public ComponentRegistryClient(
		IHttpClientFactory httpClientFactory,
		IComponentRegistryCacheStore cacheStore,
		IEmbeddedRegistryReader embeddedReader,
		ILogger<ComponentRegistryClient> logger)
		: this(httpClientFactory, cacheStore, embeddedReader, logger, ResolveCdnBaseUrl()) {
	}

	internal ComponentRegistryClient(
		IHttpClientFactory httpClientFactory,
		IComponentRegistryCacheStore cacheStore,
		IEmbeddedRegistryReader embeddedReader,
		ILogger<ComponentRegistryClient> logger,
		string cdnBaseUrl) {
		_httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
		_cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
		_embeddedReader = embeddedReader ?? throw new ArgumentNullException(nameof(embeddedReader));
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

		// Tier 4: embedded snapshot baked into clio.dll. Always available unless the build is broken.
		_logger.LogWarning(
			"component-registry source=embedded fallbackFrom={Requested} embeddedVersion={EmbeddedVersion}",
			requestedVersion, _embeddedReader.EmbeddedVersion);
		return new ComponentRegistryFetchResult(
			_embeddedReader.OpenRegistryStream(),
			_embeddedReader.EmbeddedVersion,
			ComponentRegistrySource.Embedded);
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
		string url = _cdnBaseUrl + version + ".json";

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

	private static string ResolveCdnBaseUrl() {
		string? envOverride = Environment.GetEnvironmentVariable(CdnBaseUrlEnvironmentVariable);
		return string.IsNullOrWhiteSpace(envOverride) ? DefaultCdnBaseUrl : envOverride;
	}
}
