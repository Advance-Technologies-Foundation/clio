using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Microsoft.Extensions.Logging;
using IFileSystem = System.IO.Abstractions.IFileSystem;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Orchestrates the layered fallback chain that backs <c>get-component-info</c>:
/// CDN → file cache (<c><clio-home>/cache/component-registry/</c>) → embedded snapshot
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
	/// Returned bytes came from a developer-supplied file pointed to by the
	/// flavor's local-override env var. Never cached.
	/// </summary>
	Local
}

/// <summary>
/// Profile that distinguishes the web and mobile component-registry pipelines. The
/// two consumers share the same cache+CDN infrastructure (same envelope shape, same
/// 5min TTL, same retry/backoff, same UnmappedExtensions guard), but they read
/// different files from the CDN and each has its own developer local-override env var.
/// </summary>
/// <param name="DisplayName">Stable identifier used in log lines (e.g. <c>"web"</c>, <c>"mobile"</c>).</param>
/// <param name="CdnRegistryFileName">Bare filename served by the academy CDN; e.g. <c>"ComponentRegistry.json"</c>.</param>
/// <param name="LocalFileEnvironmentVariable">Name of the env var that forces a local override of the entire chain.</param>
/// <param name="CacheSubdirectoryName">
/// Optional sub-folder under <c><clio-home>/cache/component-registry/</c> isolating this
/// flavor's payloads. Empty for the web flavor (back-compat with cache files written
/// before the mobile flavor existed).
/// </param>
public sealed record RegistryFlavor(
	string DisplayName,
	string CdnRegistryFileName,
	string LocalFileEnvironmentVariable,
	string CacheSubdirectoryName) {

	/// <summary>Web flavor: the original component registry — <c>academy/api/mcp/{version}/ComponentRegistry.json</c>.</summary>
	public static readonly RegistryFlavor Web = new(
		DisplayName: "web",
		CdnRegistryFileName: "ComponentRegistry.json",
		LocalFileEnvironmentVariable: "CLIO_COMPONENT_REGISTRY_LOCAL_FILE",
		CacheSubdirectoryName: string.Empty);

	/// <summary>
	/// Mobile flavor: same wrapped-envelope contract, separate file —
	/// <c>academy/api/mcp/{version}/MobileComponentRegistry.json</c>. Cache lives in a
	/// dedicated subfolder so web and mobile payloads cannot collide on the same
	/// <c>latest.json</c> key.
	/// </summary>
	public static readonly RegistryFlavor Mobile = new(
		DisplayName: "mobile",
		CdnRegistryFileName: "MobileComponentRegistry.json",
		LocalFileEnvironmentVariable: "CLIO_MOBILE_COMPONENT_REGISTRY_LOCAL_FILE",
		CacheSubdirectoryName: "mobile");

	/// <summary>
	/// Requests flavor: the Freedom UI request catalog (<c>crt.*Request</c> types wired
	/// through <c>RequestBindingConfig</c> outputs) —
	/// <c>academy/api/mcp/{version}/RequestRegistry.json</c>. The envelope differs from
	/// the component registries (<c>requests[]</c> + <c>references.baseParameters</c>),
	/// so only the byte-transport chain is shared; parsing lives in
	/// <c>RequestInfoCatalog</c>, not <c>ComponentInfoCatalog</c>. OOTB button-action
	/// requests initiative (ENG-93187).
	/// </summary>
	public static readonly RegistryFlavor Requests = new(
		DisplayName: "requests",
		CdnRegistryFileName: "RequestRegistry.json",
		LocalFileEnvironmentVariable: "CLIO_REQUEST_REGISTRY_LOCAL_FILE",
		CacheSubdirectoryName: "requests");
	
	/// <summary>
	/// Web→mobile page-conversion-rules flavor: same wrapped fallback chain, separate file —
	/// <c>academy/api/mcp/{version}/WebToMobilePageConversionRules.json</c>. Cache lives under a
	/// dedicated <c>web-to-mobile-page-conversion-rules/</c> subfolder. The CDN file is not
	/// published yet, so today the catalog falls back to a bundled rules file; publishing the CDN
	/// file later switches the source with no code change. A future classic→freedom converter gets
	/// its own flavor and file.
	/// </summary>
	public static readonly RegistryFlavor WebToMobilePageConversionRules = new(
		DisplayName: "web-to-mobile-page-conversion-rules",
		CdnRegistryFileName: "WebToMobilePageConversionRules.json",
		LocalFileEnvironmentVariable: "CLIO_WEB_TO_MOBILE_PAGE_CONVERSION_RULES_LOCAL_FILE",
		CacheSubdirectoryName: "web-to-mobile-page-conversion-rules");
	
	/// <summary>
	/// Mobile requests flavor: same wrapped-envelope contract as <see cref="Requests"/>, separate
	/// file — <c>academy/api/mcp/{version}/MobileRequestRegistry.json</c>. Mirrors how
	/// <see cref="Mobile"/> relates to the web component registry: the mobile request catalog is a
	/// distinct registry, scoped to only the <c>crt.*Request</c> types actually available on Freedom
	/// UI mobile. Cache lives in a dedicated <c>mobile-requests/</c> subfolder so web and mobile
	/// request payloads cannot collide on the same <c>latest.json</c> key, and the operator override
	/// reads <c>CLIO_MOBILE_REQUEST_REGISTRY_LOCAL_FILE</c>. The producer-side CDN asset ships after
	/// this clio plumbing (mobile OOTB requests initiative); until then the mobile flavor degrades to
	/// the same graceful <see cref="ComponentRegistryUnavailableException"/> as any unpublished
	/// version, and offline iteration goes through the local-override env var.
	/// </summary>
	public static readonly RegistryFlavor MobileRequests = new(
		DisplayName: "mobile-requests",
		CdnRegistryFileName: "MobileRequestRegistry.json",
		LocalFileEnvironmentVariable: "CLIO_MOBILE_REQUEST_REGISTRY_LOCAL_FILE",
		CacheSubdirectoryName: "mobile-requests");
}

/// <summary>
/// HTTP-backed implementation of <see cref="IComponentRegistryClient"/>. The same
/// type backs both the web and mobile flavors via the <see cref="RegistryFlavor"/>
/// constructor parameter.
/// </summary>
public class ComponentRegistryClient : IComponentRegistryClient {
	internal const string HttpClientName = "component-registry";

	// The default CDN base URL is a deliberate product constant — it identifies the
	// public academy.creatio.com endpoint that ships the curated catalog. It is
	// overridable at runtime via CdnBaseUrlEnvironmentVariable for dev/staging.
	[SuppressMessage("Major Code Smell", "S1075:URIs should not be hardcoded",
		Justification = "This is the product's well-known CDN endpoint, not a configurable resource path. Overridable at runtime via " + CdnBaseUrlEnvironmentVariable + ".")]
	internal const string DefaultCdnBaseUrl = "https://academy.creatio.com/api/mcp/";

	/// <summary>Back-compat alias of <see cref="RegistryFlavor.Web"/>.<c>CdnRegistryFileName</c>.</summary>
	internal const string CdnRegistryFileName = "ComponentRegistry.json";
	internal const string CdnBaseUrlEnvironmentVariable = "CLIO_COMPONENT_REGISTRY_CDN_BASE_URL";
	/// <summary>Back-compat alias of <see cref="RegistryFlavor.Web"/>.<c>LocalFileEnvironmentVariable</c>.</summary>
	internal const string LocalFileEnvironmentVariable = "CLIO_COMPONENT_REGISTRY_LOCAL_FILE";
	internal const string LatestVersion = "latest";
	internal const int CdnFetchAttempts = 3;
	internal static readonly TimeSpan CdnFetchTimeout = TimeSpan.FromSeconds(30);

	// Per-(flavor, version) semaphores: a single process-wide gate would serialise
	// web + mobile background refreshes (and refreshes for `latest` vs a pinned
	// GA-version) artificially — review #2 on PR #599. Keyed lookup avoids that
	// while still de-duplicating concurrent refreshes for the same flavor+version.
	private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim>
		BackgroundRefreshGates = new(StringComparer.Ordinal);

	// Tracks in-flight fire-and-forget background refresh tasks so that
	// DrainAsync() can await them before the process exits. Without this, the
	// Task.Run task may be killed by the runtime before it can write the new
	// cache file to disk (background threads are terminated when all foreground
	// threads exit, and the MCP server main thread exits promptly on SIGTERM).
	private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task>
		PendingRefreshTasks = new(StringComparer.Ordinal);

	private SemaphoreSlim GetBackgroundRefreshGate(string version) {
		string key = $"{_flavor.DisplayName}|{version}";
		return BackgroundRefreshGates.GetOrAdd(key, _ => new SemaphoreSlim(initialCount: 1, maxCount: 1));
	}

	private readonly IHttpClientFactory _httpClientFactory;
	private readonly IComponentRegistryCacheStore _cacheStore;
	private readonly IFileSystem _fileSystem;
	// Stored as the non-generic ILogger so the subclass (MobileComponentRegistryClient)
	// can accept its own ILogger<MobileComponentRegistryClient> and forward it through
	// the base ctor without violating Sonar S6672 ("logger should use enclosing type"
	// on the subclass). DI resolves ILogger<T> instances at construction time; the
	// generic category is preserved through the upcast.
	private readonly ILogger _logger;
	private readonly string _cdnBaseUrl;
	private readonly RegistryFlavor _flavor;

	public ComponentRegistryClient(
		IHttpClientFactory httpClientFactory,
		IComponentRegistryCacheStore cacheStore,
		IFileSystem fileSystem,
		ILogger<ComponentRegistryClient> logger)
		: this(httpClientFactory, cacheStore, fileSystem, (ILogger)logger, ResolveCdnBaseUrl(), RegistryFlavor.Web) {
	}

	public ComponentRegistryClient(
		IHttpClientFactory httpClientFactory,
		IComponentRegistryCacheStore cacheStore,
		IFileSystem fileSystem,
		ILogger<ComponentRegistryClient> logger,
		RegistryFlavor flavor)
		: this(httpClientFactory, cacheStore, fileSystem, (ILogger)logger, ResolveCdnBaseUrl(), flavor) {
	}

	/// <summary>
	/// Constructor used by subclasses (e.g. <see cref="MobileComponentRegistryClient"/>)
	/// that carry their own typed <see cref="ILogger{TCategoryName}"/>. The logger is
	/// upcast to the non-generic <see cref="ILogger"/> field — category info is
	/// preserved through the runtime instance.
	/// </summary>
	protected ComponentRegistryClient(
		IHttpClientFactory httpClientFactory,
		IComponentRegistryCacheStore cacheStore,
		IFileSystem fileSystem,
		ILogger logger,
		RegistryFlavor flavor)
		: this(httpClientFactory, cacheStore, fileSystem, logger, ResolveCdnBaseUrl(), flavor) {
	}

	internal ComponentRegistryClient(
		IHttpClientFactory httpClientFactory,
		IComponentRegistryCacheStore cacheStore,
		IFileSystem fileSystem,
		ILogger<ComponentRegistryClient> logger,
		string cdnBaseUrl)
		: this(httpClientFactory, cacheStore, fileSystem, (ILogger)logger, cdnBaseUrl, RegistryFlavor.Web) {
	}

	internal ComponentRegistryClient(
		IHttpClientFactory httpClientFactory,
		IComponentRegistryCacheStore cacheStore,
		IFileSystem fileSystem,
		ILogger logger,
		string cdnBaseUrl,
		RegistryFlavor flavor) {
		_httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
		_cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_flavor = flavor ?? throw new ArgumentNullException(nameof(flavor));
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

		// Tier exhausted: cache + CDN both miss, no local override.
		// Surface a clear error — ComponentInfoTool's catch-all turns it into a graceful
		// MCP response that points the operator at the flavor's local-override env var.
		_logger.LogWarning(
			"component-registry source=unavailable flavor={Flavor} fallbackFrom={Requested} cdn={CdnBaseUrl}",
			_flavor.DisplayName, requestedVersion, _cdnBaseUrl);
		throw new ComponentRegistryUnavailableException(requestedVersion, _cdnBaseUrl, _flavor.LocalFileEnvironmentVariable);
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
		// HttpClient.Timeout is configured once in BindingsModule via
		// AddHttpClient(HttpClientName).ConfigureHttpClient(...). Mutating Timeout
		// here would (a) waste a setter call per request, (b) be a latent race if
		// the named client ever became shared, and (c) throw `InvalidOperationException`
		// after the instance had been used.
		HttpClient http = _httpClientFactory.CreateClient(HttpClientName);
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
				Stream payloadStream = await CacheAndReturnStreamAsync(response, version, url, attempt, cancellationToken).ConfigureAwait(false);
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

	private async Task<Stream> CacheAndReturnStreamAsync(HttpResponseMessage response, string version, string sourceUrl, int attempt, CancellationToken cancellationToken) {
		byte[] payload = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
		// `sourceUrl` is the actual URL the writer fetched from, so the metadata
		// sidecar reflects flavor + override (`CLIO_COMPONENT_REGISTRY_CDN_BASE_URL`)
		// instead of a hard-coded production URL — review #4 on PR #599.
		await _cacheStore.WriteAsync(
			version,
			payload,
			response.Headers.ETag,
			response.Content.Headers.LastModified,
			sourceUrl,
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
		// Fire-and-forget, tracked in PendingRefreshTasks so DrainAsync() can
		// await completion during process shutdown. The gate is keyed by
		// (flavor, version) so a flurry of stale reads on `web:latest` is
		// de-duplicated without blocking a parallel stale read on `mobile:latest`.
		string key = $"{_flavor.DisplayName}|{version}";
		SemaphoreSlim gate = GetBackgroundRefreshGate(version);
		// Declare before Task.Run so the lambda can capture the variable and
		// use value-matched removal in the finally block (avoids evicting a
		// concurrent task's dict entry — the indexer assignment below may
		// overwrite this task if two stale reads race for the same key).
		Task task = null!;
		task = Task.Run(async () => {
			if (!await gate.WaitAsync(0).ConfigureAwait(false)) {
				return;
			}
			try {
				using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
				await TryFetchFromCdnAsync(version, cts.Token).ConfigureAwait(false);
			} catch (Exception ex) {
				_logger.LogWarning(ex,
					"component-registry background-refresh-failed flavor={Flavor} version={Version}",
					_flavor.DisplayName, version);
			} finally {
				gate.Release();
				// Value-matched removal: only evict this task's own entry so a
				// concurrent refresh that overwrote the slot is not accidentally
				// removed from the drain set.
				((ICollection<KeyValuePair<string, Task>>)PendingRefreshTasks)
					.Remove(new KeyValuePair<string, Task>(key, task));
			}
		});
		PendingRefreshTasks[key] = task;
	}

	/// <summary>
	/// Waits for all in-flight background CDN refreshes to complete, up to
	/// <paramref name="timeout"/>. Call during process shutdown so that a
	/// fire-and-forget refresh started just before exit is not killed by the
	/// runtime before it can write its cache file to disk.
	/// </summary>
	internal static async Task DrainAsync(TimeSpan timeout) {
		Task[] tasks = PendingRefreshTasks.Values.ToArray();
		if (tasks.Length == 0) {
			return;
		}
		try {
			await Task.WhenAll(tasks).WaitAsync(timeout).ConfigureAwait(false);
		} catch (TimeoutException) {
			// best-effort: individual task outcomes are already logged
		} catch (Exception) {
			// individual task failures are already logged via LogWarning above
		}
	}

	private Stream? TryOpenLocalOverride(string requestedVersion) {
		string envVarName = _flavor.LocalFileEnvironmentVariable;
		string? path = Environment.GetEnvironmentVariable(envVarName);
		if (string.IsNullOrWhiteSpace(path)) {
			return null;
		}

		// Fail-fast: a non-empty env var means the developer explicitly opted into reading
		// from disk. If the file is missing or unreadable, surface the problem instead of
		// silently serving CDN data — that masks override mistakes and produces stale,
		// confusing results in long-running `clio mcp serve` sessions.
		if (!_fileSystem.File.Exists(path)) {
			_logger.LogWarning(
				"component-registry local-override-missing flavor={Flavor} version={Version} path={Path}",
				_flavor.DisplayName, requestedVersion, path);
			throw new FileNotFoundException(
				$"Component registry override file does not exist: '{path}'. " +
				$"Either fix the path or unset {envVarName} to fall back to the CDN/cache chain.",
				path);
		}

		Stream stream = _fileSystem.File.OpenRead(path);
		_logger.LogInformation(
			"component-registry source=local flavor={Flavor} version={Version} path={Path}",
			_flavor.DisplayName, requestedVersion, path);
		return stream;
	}

	private static string ResolveCdnBaseUrl() {
		string? envOverride = Environment.GetEnvironmentVariable(CdnBaseUrlEnvironmentVariable);
		return string.IsNullOrWhiteSpace(envOverride) ? DefaultCdnBaseUrl : envOverride;
	}

	// Builds {base}{version}/{flavor.CdnRegistryFileName} through Uri composition.
	// The relative path is a single interpolated string passed to Uri's relative-path
	// constructor, so the slash is part of a URL-protocol path token rather than a
	// free-floating concatenation separator (RFC 3986 path delimiter, always '/').
	private string BuildCdnUrl(string version) {
		Uri baseUri = new(_cdnBaseUrl, UriKind.Absolute);
		Uri relativeUri = new($"{version}/{_flavor.CdnRegistryFileName}", UriKind.Relative);
		return new Uri(baseUri, relativeUri).AbsoluteUri;
	}
}

/// <summary>
/// Marker interface that selects the mobile-flavored registry client at DI time.
/// Adds no new methods over <see cref="IComponentRegistryClient"/> — the contract is
/// identical, the implementation is the same <see cref="ComponentRegistryClient"/>
/// type, only the constructor-time <see cref="RegistryFlavor"/> differs.
/// </summary>
public interface IMobileComponentRegistryClient : IComponentRegistryClient {
}

/// <summary>
/// Concrete subtype used to register the mobile flavor through standard DI. The
/// implementation is inherited verbatim from <see cref="ComponentRegistryClient"/>;
/// only the flavor selection happens here.
/// </summary>
public sealed class MobileComponentRegistryClient : ComponentRegistryClient, IMobileComponentRegistryClient {
	public MobileComponentRegistryClient(
		IHttpClientFactory httpClientFactory,
		IComponentRegistryCacheStore cacheStore,
		IFileSystem fileSystem,
		ILogger<MobileComponentRegistryClient> logger)
		: base(httpClientFactory, cacheStore, fileSystem, logger, RegistryFlavor.Mobile) {
	}
}

/// <summary>
/// Marker interface that selects the requests-flavored registry client at DI time.
/// Adds no new methods over <see cref="IComponentRegistryClient"/> — the byte-transport
/// contract is identical, the implementation is the same <see cref="ComponentRegistryClient"/>
/// type, only the constructor-time <see cref="RegistryFlavor"/> differs. The payload it
/// serves (<c>RequestRegistry.json</c>) has its own envelope, parsed by
/// <c>RequestInfoCatalog</c> rather than <c>ComponentInfoCatalog</c>.
/// </summary>
public interface IRequestRegistryClient : IComponentRegistryClient {
}

/// <summary>
/// Concrete subtype used to register the requests flavor through standard DI. The
/// implementation is inherited verbatim from <see cref="ComponentRegistryClient"/>;
/// only the flavor selection happens here.
/// </summary>
public sealed class RequestRegistryClient : ComponentRegistryClient, IRequestRegistryClient {
	public RequestRegistryClient(
		IHttpClientFactory httpClientFactory,
		IComponentRegistryCacheStore cacheStore,
		IFileSystem fileSystem,
		ILogger<RequestRegistryClient> logger)
		: base(httpClientFactory, cacheStore, fileSystem, logger, RegistryFlavor.Requests) {
	}
}

/// <summary>
/// Marker interface that selects the web→mobile page-conversion-rules registry client at DI time.
/// Identical contract to <see cref="IComponentRegistryClient"/>; only the constructor-time
/// <see cref="RegistryFlavor"/> differs.
/// </summary>
public interface IWebToMobilePageConversionRulesRegistryClient : IComponentRegistryClient {
}

/// <summary>
/// Concrete subtype used to register the web→mobile page-conversion-rules flavor through standard
/// DI. Implementation is inherited verbatim from <see cref="ComponentRegistryClient"/>; only the
/// flavor selection happens here.
/// </summary>
public sealed class WebToMobilePageConversionRulesRegistryClient : ComponentRegistryClient, IWebToMobilePageConversionRulesRegistryClient {
	public WebToMobilePageConversionRulesRegistryClient(
		IHttpClientFactory httpClientFactory,
		IComponentRegistryCacheStore cacheStore,
		IFileSystem fileSystem,
		ILogger<WebToMobilePageConversionRulesRegistryClient> logger)
		: base(httpClientFactory, cacheStore, fileSystem, logger, RegistryFlavor.WebToMobilePageConversionRules) {
	}
}

/// <summary>
/// Marker interface that selects the mobile-requests-flavored registry client at DI time.
/// Adds no new methods over <see cref="IComponentRegistryClient"/> — the byte-transport
/// contract is identical, the implementation is the same <see cref="ComponentRegistryClient"/>
/// type, only the constructor-time <see cref="RegistryFlavor"/> differs. It serves
/// <c>MobileRequestRegistry.json</c>, which shares the request envelope parsed by
/// <c>RequestInfoCatalog</c>; the mobile catalog is scoped to the requests actually available
/// on Freedom UI mobile. Mirrors how <see cref="IMobileComponentRegistryClient"/> relates to
/// <see cref="IComponentRegistryClient"/> for components.
/// </summary>
public interface IMobileRequestRegistryClient : IComponentRegistryClient {
}

/// <summary>
/// Concrete subtype used to register the mobile-requests flavor through standard DI. The
/// implementation is inherited verbatim from <see cref="ComponentRegistryClient"/>;
/// only the flavor selection (<see cref="RegistryFlavor.MobileRequests"/>) happens here.
/// </summary>
public sealed class MobileRequestRegistryClient : ComponentRegistryClient, IMobileRequestRegistryClient {
	public MobileRequestRegistryClient(
		IHttpClientFactory httpClientFactory,
		IComponentRegistryCacheStore cacheStore,
		IFileSystem fileSystem,
		ILogger<MobileRequestRegistryClient> logger)
		: base(httpClientFactory, cacheStore, fileSystem, logger, RegistryFlavor.MobileRequests) {
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
		: this(requestedVersion, cdnBaseUrl, RegistryFlavor.Web.LocalFileEnvironmentVariable) {
	}

	public ComponentRegistryUnavailableException(string requestedVersion, string cdnBaseUrl, string localFileEnvironmentVariable)
		: base(BuildMessage(requestedVersion, cdnBaseUrl, localFileEnvironmentVariable)) {
		RequestedVersion = requestedVersion;
		CdnBaseUrl = cdnBaseUrl;
		LocalFileEnvironmentVariable = localFileEnvironmentVariable;
	}

	/// <summary>The version that was originally requested when the chain ran out of tiers.</summary>
	public string RequestedVersion { get; }

	/// <summary>The CDN base URL that the client failed to reach.</summary>
	public string CdnBaseUrl { get; }

	/// <summary>The flavor-specific env var name the operator can use for a local override.</summary>
	public string LocalFileEnvironmentVariable { get; }

	private static string BuildMessage(string requestedVersion, string cdnBaseUrl, string localFileEnvironmentVariable) =>
		$"Component registry version '{requestedVersion}' is unavailable: file cache is empty and the CDN at '{cdnBaseUrl}' could not be reached. " +
		$"Set {localFileEnvironmentVariable} to a local registry JSON for offline development, or retry once connectivity is restored.";
}
