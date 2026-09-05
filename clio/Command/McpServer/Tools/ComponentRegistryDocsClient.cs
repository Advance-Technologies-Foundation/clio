using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Which tier of the documentation fallback chain produced the markdown for one
/// <c>content.docs[]</c> path.
/// </summary>
public enum ComponentDocumentationSource {
	/// <summary>No tier could serve the file — the path was invalid, or every tier missed.</summary>
	None,

	/// <summary>
	/// Served from the developer working copy resolved next to the flavor's
	/// <c>*_LOCAL_FILE</c> registry override. Never written to the disk cache.
	/// </summary>
	Local,

	/// <summary>Served from the on-disk docs cache (fresh, or stale when the CDN could not revalidate).</summary>
	FileCache,

	/// <summary>Served from a CDN response (initial fetch or a stale-cache revalidation).</summary>
	Cdn
}

/// <summary>
/// Outcome of a single documentation fetch: the markdown, and the tier that produced it.
/// </summary>
/// <param name="Content">UTF-8 decoded markdown, or <see langword="null"/> when nothing was served.</param>
/// <param name="Source">The tier that served <paramref name="Content"/>.</param>
/// <param name="ExpectedLocalPath">
/// When the flavor's local override is active, the absolute path the file was expected at.
/// Lets the caller name the missing file in an operator-facing warning instead of leaving a
/// local-override miss silent. <see langword="null"/> when no override is active.
/// </param>
public sealed record ComponentDocumentationFetchResult(
	string? Content,
	ComponentDocumentationSource Source,
	string? ExpectedLocalPath = null) {

	/// <summary>Nothing was served and no local override was active.</summary>
	public static ComponentDocumentationFetchResult Missing { get; } =
		new(Content: null, ComponentDocumentationSource.None);
}

/// <summary>
/// Fetches long-form documentation files referenced from a component registry entry
/// (<see cref="ComponentContent.Docs"/>). Three-tier resolution: developer local
/// override → file cache → CDN. There is no embedded fallback for docs — when the
/// chain misses, the call returns a <see cref="ComponentDocumentationSource.None"/>
/// result and the caller (the MCP tool) skips that file and keeps any
/// successfully-fetched siblings.
/// </summary>
public interface IComponentRegistryDocsClient {
	/// <summary>
	/// Returns the UTF-8 decoded markdown for a documentation file together with the tier
	/// that served it, or a <see cref="ComponentDocumentationSource.None"/> result when the
	/// path is invalid, the local override does not carry the file, or the cache and CDN
	/// both miss.
	/// </summary>
	/// <param name="version">Resolved platform version, or <c>"latest"</c>.</param>
	/// <param name="docPath">Registry-provided path (e.g. <c>docs/data-grid.component.md</c>).</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	Task<ComponentDocumentationFetchResult> GetDocAsync(string version, string docPath, CancellationToken cancellationToken = default);
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

	/// <summary>
	/// Upper bound on the synchronous CDN revalidation that runs when a <em>stale</em>
	/// doc is found in the cache. Component documentation drives how the AI agent
	/// writes page schemas, so freshness is preferred over latency (ENG-91135): the
	/// caller waits up to this budget for the current doc, then falls back to the
	/// stale copy only if the CDN cannot serve a fresh one in time. Kept short so a
	/// slow/unreachable CDN adds at most "a few seconds" rather than the full
	/// retry-with-backoff ceiling.
	/// </summary>
	internal static readonly TimeSpan StaleRevalidateBudget = TimeSpan.FromSeconds(5);

	private readonly IHttpClientFactory _httpClientFactory;
	private readonly IComponentRegistryDocsCacheStore _cacheStore;
	private readonly IFileSystem _fileSystem;
	private readonly ILogger<ComponentRegistryDocsClient> _logger;
	private readonly string _cdnBaseUrl;

	public ComponentRegistryDocsClient(
		IHttpClientFactory httpClientFactory,
		IComponentRegistryDocsCacheStore cacheStore,
		IFileSystem fileSystem,
		ILogger<ComponentRegistryDocsClient> logger)
		: this(httpClientFactory, cacheStore, fileSystem, logger, ResolveCdnBaseUrl()) {
	}

	internal ComponentRegistryDocsClient(
		IHttpClientFactory httpClientFactory,
		IComponentRegistryDocsCacheStore cacheStore,
		IFileSystem fileSystem,
		ILogger<ComponentRegistryDocsClient> logger,
		string cdnBaseUrl) {
		_httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
		_cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_cdnBaseUrl = cdnBaseUrl ?? throw new ArgumentNullException(nameof(cdnBaseUrl));
	}

	/// <inheritdoc />
	public async Task<ComponentDocumentationFetchResult> GetDocAsync(string version, string docPath, CancellationToken cancellationToken = default) {
		if (!ComponentRegistryDocsPath.TryNormalise(docPath, out string normalisedPath)) {
			_logger.LogWarning(
				"component-registry-docs reject reason=invalid-path version={Version} path={Path}",
				version, docPath);
			return ComponentDocumentationFetchResult.Missing;
		}

		// Tier 0: developer override. When the flavor that owns this docs namespace has its
		// *_LOCAL_FILE registry override set, the docs tree is resolved next to that file and
		// becomes the ONLY tier: `components[]` and the recipes it points at must come from
		// the same working copy, otherwise the developer validates a documentation edit
		// against published CDN prose and the round-trip only looks successful (issue #1361).
		ComponentDocumentationFetchResult? local = TryReadLocalOverride(version, normalisedPath);
		if (local is not null) {
			return local;
		}

		// Tier 1: fresh cache → return immediately. Stale cache → revalidate
		// synchronously within a bounded budget (stale-if-error): the agent gets the
		// CURRENT doc whenever the CDN can serve it in time, and only falls back to the
		// stale bytes when the CDN is unreachable/too slow. This deliberately trades a
		// few seconds of latency for documentation correctness — an outdated guide
		// silently steers the agent into wrong page schemas (ENG-91135).
		ComponentRegistryDocsCacheReadResult? cached =
			await _cacheStore.TryReadAsync(version, normalisedPath, cancellationToken).ConfigureAwait(false);
		if (cached is not null) {
			if (cached.IsFresh) {
				_logger.LogInformation(
					"component-registry-docs source=cache version={Version} path={Path} stale=false",
					version, normalisedPath);
				return new ComponentDocumentationFetchResult(
					Encoding.UTF8.GetString(cached.Content), ComponentDocumentationSource.FileCache);
			}

			// Stale: try a time-boxed synchronous CDN refresh first.
			byte[]? revalidated = await TryRevalidateWithinBudgetAsync(version, normalisedPath, cancellationToken)
				.ConfigureAwait(false);
			if (revalidated is not null) {
				_logger.LogInformation(
					"component-registry-docs source=cdn-revalidate version={Version} path={Path} stale=true refreshed=true bytes={Bytes}",
					version, normalisedPath, revalidated.Length);
				return new ComponentDocumentationFetchResult(
					Encoding.UTF8.GetString(revalidated), ComponentDocumentationSource.Cdn);
			}

			// CDN unreachable, too slow, or no longer serving the file: serve the stale copy.
			_logger.LogInformation(
				"component-registry-docs source=cache version={Version} path={Path} stale=true refreshed=false reason=cdn-unavailable",
				version, normalisedPath);
			return new ComponentDocumentationFetchResult(
				Encoding.UTF8.GetString(cached.Content), ComponentDocumentationSource.FileCache);
		}

		// Tier 2: CDN fetch, cache on success.
		byte[]? fetched = await TryFetchFromCdnAsync(version, normalisedPath, cancellationToken).ConfigureAwait(false);
		if (fetched is not null) {
			return new ComponentDocumentationFetchResult(
				Encoding.UTF8.GetString(fetched), ComponentDocumentationSource.Cdn);
		}

		_logger.LogInformation(
			"component-registry-docs source=miss version={Version} path={Path}",
			version, normalisedPath);
		return ComponentDocumentationFetchResult.Missing;
	}

	/// <summary>
	/// Tier 0. Resolves <paramref name="normalisedDocPath"/> against the directory of the
	/// <c>*_LOCAL_FILE</c> registry override belonging to the flavor that owns the path's
	/// documentation namespace, mirroring the layout the producer-side generator writes
	/// (registry JSON and its <c>docs/</c> tree into one output directory).
	/// </summary>
	/// <returns>
	/// <see langword="null"/> when no override is active for this namespace, so the caller
	/// continues with the cache/CDN chain. Otherwise a result that is authoritative: the
	/// local markdown, or a <see cref="ComponentDocumentationSource.None"/> result naming
	/// the path that was expected. The override deliberately does NOT fall through — a
	/// silent CDN substitution is the defect being fixed.
	/// </returns>
	private ComponentDocumentationFetchResult? TryReadLocalOverride(string version, string normalisedDocPath) {
		if (!ComponentRegistryDocsPath.TryResolveFlavor(normalisedDocPath, out RegistryFlavor? flavor)) {
			return null;
		}

		string? registryFilePath = Environment.GetEnvironmentVariable(flavor.LocalFileEnvironmentVariable);
		if (string.IsNullOrWhiteSpace(registryFilePath)) {
			return null;
		}

		string? docsRoot = Path.GetDirectoryName(Path.GetFullPath(registryFilePath.Trim()));
		if (string.IsNullOrEmpty(docsRoot)) {
			return null;
		}

		string candidate = Path.GetFullPath(Path.Combine(docsRoot, normalisedDocPath));
		// Defence in depth: TryNormalise already rejects "..", backslashes and rooted paths,
		// but the containment check is repeated at every filesystem boundary per the repo rule.
		if (!candidate.StartsWith(docsRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)) {
			_logger.LogWarning(
				"component-registry-docs reject reason=local-escape flavor={Flavor} version={Version} path={Path} resolved={Resolved}",
				flavor.DisplayName, version, normalisedDocPath, candidate);
			return new ComponentDocumentationFetchResult(
				Content: null, ComponentDocumentationSource.None, candidate);
		}

		if (!_fileSystem.File.Exists(candidate)) {
			_logger.LogWarning(
				"component-registry-docs source=local-miss flavor={Flavor} version={Version} path={Path} expected={Expected}",
				flavor.DisplayName, version, normalisedDocPath, candidate);
			return new ComponentDocumentationFetchResult(
				Content: null, ComponentDocumentationSource.None, candidate);
		}

		// Never written to the docs cache: the override stays a read-only test channel, so an
		// unpublished draft can never leak into the cache a later non-override run reads.
		string content = _fileSystem.File.ReadAllText(candidate);
		_logger.LogInformation(
			"component-registry-docs source=local flavor={Flavor} version={Version} path={Path} file={File} bytes={Bytes}",
			flavor.DisplayName, version, normalisedDocPath, candidate, content.Length);
		return new ComponentDocumentationFetchResult(content, ComponentDocumentationSource.Local, candidate);
	}

	private async Task<byte[]?> TryFetchFromCdnAsync(string version, string normalisedDocPath, CancellationToken cancellationToken) {
		// HttpClient.Timeout is configured once in BindingsModule via
		// AddHttpClient(HttpClientName).ConfigureHttpClient(...) — same shared
		// timeout the registry client uses (no per-call mutation; review #1).
		HttpClient http = _httpClientFactory.CreateClient(HttpClientName);
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
					_cdnBaseUrl,
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

	/// <summary>
	/// Runs a synchronous CDN fetch capped by <see cref="StaleRevalidateBudget"/> via a
	/// linked <see cref="CancellationTokenSource"/>. Returns the fresh payload on success,
	/// or <see langword="null"/> when the budget elapses, the CDN is unreachable, or the
	/// file is no longer served — the caller then hands back the stale copy. A genuine
	/// caller-initiated cancellation (distinguished from the internal budget timeout)
	/// propagates instead of being swallowed.
	/// </summary>
	private async Task<byte[]?> TryRevalidateWithinBudgetAsync(string version, string normalisedDocPath, CancellationToken cancellationToken) {
		using CancellationTokenSource budgetCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		budgetCts.CancelAfter(StaleRevalidateBudget);
		try {
			return await TryFetchFromCdnAsync(version, normalisedDocPath, budgetCts.Token).ConfigureAwait(false);
		} catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested) {
			// The budget elapsed (not a caller cancellation) before the CDN responded.
			_logger.LogInformation(ex,
				"component-registry-docs revalidate-timeout version={Version} path={Path} budgetMs={BudgetMs}",
				version, normalisedDocPath, (int)StaleRevalidateBudget.TotalMilliseconds);
			return null;
		}
	}

	private static string ResolveCdnBaseUrl() {
		string? envOverride = Environment.GetEnvironmentVariable(ComponentRegistryClient.CdnBaseUrlEnvironmentVariable);
		return string.IsNullOrWhiteSpace(envOverride) ? ComponentRegistryClient.DefaultCdnBaseUrl : envOverride;
	}

	// Builds {base}{version}/{docPath} via Uri composition. docPath has been validated
	// to start with the docs namespace ("docs/" or "mobile-docs/") and contain only safe
	// characters, so it can be appended directly as part of the relative URL.
	private string BuildCdnUrl(string version, string normalisedDocPath) {
		Uri baseUri = new(_cdnBaseUrl, UriKind.Absolute);
		Uri relativeUri = new($"{version}/{normalisedDocPath}", UriKind.Relative);
		return new Uri(baseUri, relativeUri).AbsoluteUri;
	}
}
