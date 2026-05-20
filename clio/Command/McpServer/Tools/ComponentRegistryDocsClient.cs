using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Fetches long-form documentation files referenced from a component registry entry
/// (<see cref="ComponentContent.Docs"/>). Two-tier fallback: file cache → CDN. There
/// is no embedded fallback for docs — when both tiers miss, the call returns
/// <see langword="null"/> and the caller (the MCP tool) skips that file and keeps
/// any successfully-fetched siblings.
/// </summary>
public interface IComponentRegistryDocsClient {
	/// <summary>
	/// Returns the UTF-8 decoded markdown for a documentation file, or <see langword="null"/>
	/// when the path is invalid, the cache misses, and the CDN cannot serve it.
	/// </summary>
	/// <param name="version">Resolved platform version, or <c>"latest"</c>.</param>
	/// <param name="docPath">Registry-provided path (e.g. <c>docs/data-grid.component.md</c>).</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	Task<string?> GetDocAsync(string version, string docPath, CancellationToken cancellationToken = default);
}

/// <summary>
/// HTTP- and disk-backed implementation. Reuses the registry HTTP client (Polly retry
/// policies + connection pool) and the docs cache store. CDN base URL and the
/// <c>CLIO_COMPONENT_REGISTRY_CDN_BASE_URL</c> override are shared with
/// <see cref="ComponentRegistryClient"/>.
/// </summary>
public sealed class ComponentRegistryDocsClient : IComponentRegistryDocsClient {
	internal const string HttpClientName = ComponentRegistryClient.HttpClientName;
	internal const int CdnFetchAttempts = ComponentRegistryClient.CdnFetchAttempts;
	internal static readonly TimeSpan CdnFetchTimeout = ComponentRegistryClient.CdnFetchTimeout;

	private readonly IHttpClientFactory _httpClientFactory;
	private readonly IComponentRegistryDocsCacheStore _cacheStore;
	private readonly ILogger<ComponentRegistryDocsClient> _logger;
	private readonly string _cdnBaseUrl;

	public ComponentRegistryDocsClient(
		IHttpClientFactory httpClientFactory,
		IComponentRegistryDocsCacheStore cacheStore,
		ILogger<ComponentRegistryDocsClient> logger)
		: this(httpClientFactory, cacheStore, logger, ResolveCdnBaseUrl()) {
	}

	internal ComponentRegistryDocsClient(
		IHttpClientFactory httpClientFactory,
		IComponentRegistryDocsCacheStore cacheStore,
		ILogger<ComponentRegistryDocsClient> logger,
		string cdnBaseUrl) {
		_httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
		_cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_cdnBaseUrl = cdnBaseUrl ?? throw new ArgumentNullException(nameof(cdnBaseUrl));
	}

	/// <inheritdoc />
	public async Task<string?> GetDocAsync(string version, string docPath, CancellationToken cancellationToken = default) {
		if (!ComponentRegistryDocsPath.TryNormalise(docPath, out string normalisedPath)) {
			_logger.LogWarning(
				"component-registry-docs reject reason=invalid-path version={Version} path={Path}",
				version, docPath);
			return null;
		}

		// Tier 1: fresh cache → return immediately. Stale cache → still return, but
		// schedule the network refresh after the call (no per-request blocking).
		ComponentRegistryDocsCacheReadResult? cached =
			await _cacheStore.TryReadAsync(version, normalisedPath, cancellationToken).ConfigureAwait(false);
		if (cached is not null) {
			if (cached.IsFresh) {
				_logger.LogInformation(
					"component-registry-docs source=cache version={Version} path={Path} stale=false",
					version, normalisedPath);
				return Encoding.UTF8.GetString(cached.Content);
			}

			// Stale-while-revalidate: serve the stale bytes now, refresh in the background.
			ScheduleBackgroundRefresh(version, normalisedPath);
			_logger.LogInformation(
				"component-registry-docs source=cache version={Version} path={Path} stale=true bgRefresh=scheduled",
				version, normalisedPath);
			return Encoding.UTF8.GetString(cached.Content);
		}

		// Tier 2: CDN fetch, cache on success.
		byte[]? fetched = await TryFetchFromCdnAsync(version, normalisedPath, cancellationToken).ConfigureAwait(false);
		if (fetched is not null) {
			return Encoding.UTF8.GetString(fetched);
		}

		_logger.LogInformation(
			"component-registry-docs source=miss version={Version} path={Path}",
			version, normalisedPath);
		return null;
	}

	private async Task<byte[]?> TryFetchFromCdnAsync(string version, string normalisedDocPath, CancellationToken cancellationToken) {
		HttpClient http = _httpClientFactory.CreateClient(HttpClientName);
		http.Timeout = CdnFetchTimeout;
		string url = BuildCdnUrl(version, normalisedDocPath);

		for (int attempt = 1; attempt <= CdnFetchAttempts; attempt++) {
			FetchAttemptResult result = await TryFetchOnceAsync(http, url, version, normalisedDocPath, attempt, cancellationToken).ConfigureAwait(false);
			if (result.Payload is not null) {
				return result.Payload;
			}
			if (!result.ShouldRetry) {
				return null;
			}
			if (attempt == CdnFetchAttempts) {
				return null;
			}
			if (!await DelayBackoffAsync(attempt, cancellationToken).ConfigureAwait(false)) {
				return null;
			}
		}
		return null;
	}

	private async Task<FetchAttemptResult> TryFetchOnceAsync(
		HttpClient http,
		string url,
		string version,
		string normalisedDocPath,
		int attempt,
		CancellationToken cancellationToken) {
		try {
			using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
			if (response.IsSuccessStatusCode) {
				byte[] payload = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
				await _cacheStore.WriteAsync(
					version,
					normalisedDocPath,
					payload,
					response.Headers.ETag,
					response.Content.Headers.LastModified,
					cancellationToken).ConfigureAwait(false);
				_logger.LogInformation(
					"component-registry-docs source=cdn version={Version} path={Path} status={Status} attempt={Attempt} bytes={Bytes}",
					version, normalisedDocPath, (int)response.StatusCode, attempt, payload.Length);
				return FetchAttemptResult.Success(payload);
			}

			if (response.StatusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError) {
				_logger.LogInformation(
					"component-registry-docs cdn-skip version={Version} path={Path} status={Status}",
					version, normalisedDocPath, (int)response.StatusCode);
				return FetchAttemptResult.PermanentFailure;
			}

			_logger.LogInformation(
				"component-registry-docs cdn-retry version={Version} path={Path} status={Status} attempt={Attempt}",
				version, normalisedDocPath, (int)response.StatusCode, attempt);
			return FetchAttemptResult.TransientFailure;
		} catch (HttpRequestException ex) {
			_logger.LogInformation(ex,
				"component-registry-docs cdn-retry version={Version} path={Path} attempt={Attempt} error={Error}",
				version, normalisedDocPath, attempt, ex.Message);
			return FetchAttemptResult.TransientFailure;
		} catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested) {
			_logger.LogInformation(ex,
				"component-registry-docs cdn-retry version={Version} path={Path} attempt={Attempt} reason=timeout",
				version, normalisedDocPath, attempt);
			return FetchAttemptResult.TransientFailure;
		}
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

	private readonly record struct FetchAttemptResult(byte[]? Payload, bool ShouldRetry) {
		public static FetchAttemptResult Success(byte[] payload) => new(payload, ShouldRetry: false);
		public static FetchAttemptResult PermanentFailure { get; } = new(Payload: null, ShouldRetry: false);
		public static FetchAttemptResult TransientFailure { get; } = new(Payload: null, ShouldRetry: true);
	}

	private void ScheduleBackgroundRefresh(string version, string normalisedDocPath) {
		// Fire-and-forget. Per-request background refresh is acceptable here because
		// docs are fetched lazily (one request → at most a handful of docs) so there is
		// no thundering-herd risk to gate against.
		_ = Task.Run(async () => {
			try {
				using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
				await TryFetchFromCdnAsync(version, normalisedDocPath, cts.Token).ConfigureAwait(false);
			} catch (Exception ex) {
				_logger.LogInformation(ex,
					"component-registry-docs background-refresh-failed version={Version} path={Path}",
					version, normalisedDocPath);
			}
		});
	}

	private static string ResolveCdnBaseUrl() {
		string? envOverride = Environment.GetEnvironmentVariable(ComponentRegistryClient.CdnBaseUrlEnvironmentVariable);
		return string.IsNullOrWhiteSpace(envOverride) ? ComponentRegistryClient.DefaultCdnBaseUrl : envOverride;
	}

	// Builds {base}{version}/{docPath} via Uri composition. docPath has been validated
	// to start with "docs/" and contain only safe characters, so it can be appended
	// directly as part of the relative URL.
	private string BuildCdnUrl(string version, string normalisedDocPath) {
		Uri baseUri = new(_cdnBaseUrl, UriKind.Absolute);
		Uri relativeUri = new($"{version}/{normalisedDocPath}", UriKind.Relative);
		return new Uri(baseUri, relativeUri).AbsoluteUri;
	}
}
