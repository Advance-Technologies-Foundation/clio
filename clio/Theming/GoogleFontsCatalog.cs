using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Theming;

/// <summary>Whether a font family is published on Google Fonts, or could not be determined.</summary>
public enum GoogleFontAvailability {
	Unverified,
	InCatalog,
	NotInCatalog
}

/// <summary>Process-wide memo of definitive Google Fonts availability verdicts.</summary>
public interface IGoogleFontsAvailabilityCache {

	/// <summary>Returns a still-valid cached verdict for <paramref name="family"/>, if one exists.</summary>
	bool TryGet(string family, out GoogleFontAvailability availability);

	/// <summary>Stores a verdict for <paramref name="family"/>; unverified verdicts are never stored.</summary>
	void Store(string family, GoogleFontAvailability availability);
}

/// <summary>
/// Singleton availability memo (see the <c>ICurrentUserCultureCache</c> precedent in
/// <c>BindingsModule</c>): the probing catalog is a transient typed HTTP client, so the memo must live
/// outside it to survive across CLI/MCP calls in a long-lived server. Only definitive verdicts are
/// stored — a transient network failure must not pin a stale answer — and keys are ordinal because the
/// endpoint is case-sensitive.
/// </summary>
public sealed class GoogleFontsAvailabilityCache(TimeProvider timeProvider) : IGoogleFontsAvailabilityCache {

	private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

	private const int MaxEntries = 512;

	private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
	private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);

	/// <inheritdoc />
	public bool TryGet(string family, out GoogleFontAvailability availability) {
		if (_entries.TryGetValue(family, out CacheEntry entry)) {
			if (entry.ExpiresAt > _timeProvider.GetUtcNow()) {
				availability = entry.Availability;
				return true;
			}
			_entries.TryRemove(family, out _);
		}
		availability = GoogleFontAvailability.Unverified;
		return false;
	}

	/// <summary>The number of live entries; test-only observability for the eviction behavior.</summary>
	internal int EntryCount => _entries.Count;

	/// <inheritdoc />
	public void Store(string family, GoogleFontAvailability availability) {
		if (availability == GoogleFontAvailability.Unverified) {
			return;
		}
		DateTimeOffset now = _timeProvider.GetUtcNow();
		if (_entries.Count >= MaxEntries && !_entries.ContainsKey(family)) {
			SweepExpired(now);
			if (_entries.Count >= MaxEntries) {
				return;
			}
		}
		_entries[family] = new CacheEntry(availability, now.Add(CacheTtl));
	}

	private void SweepExpired(DateTimeOffset now) {
		foreach (KeyValuePair<string, CacheEntry> entry in _entries) {
			if (entry.Value.ExpiresAt <= now) {
				_entries.TryRemove(entry.Key, out _);
			}
		}
	}

	private readonly record struct CacheEntry(GoogleFontAvailability Availability, DateTimeOffset ExpiresAt);
}

/// <summary>Looks up whether a font family is published on Google Fonts.</summary>
public interface IGoogleFontsCatalog {
	/// <summary>
	/// Reports whether <paramref name="family"/> is published on Google Fonts. A blank family, or one
	/// that fails the font-family grammar or its length cap, is reported as
	/// <see cref="GoogleFontAvailability.Unverified"/> without a network probe.
	/// </summary>
	Task<GoogleFontAvailability> LookupAsync(string family, CancellationToken cancellationToken);
}

/// <summary>
/// Queries the Google Fonts family metadata endpoint, treating any inconclusive answer as unverified.
/// The endpoint is an undocumented internal contract, so <see cref="GoogleFontAvailability.InCatalog"/> is
/// claimed only for a JSON success response — a consent page, bot check, or SPA shell must not read as
/// published. Definitive answers are memoized per process through the shared
/// <see cref="IGoogleFontsAvailabilityCache"/> singleton (this class itself is a transient typed client).
/// </summary>
public sealed class GoogleFontsCatalog(HttpClient httpClient, IGoogleFontsAvailabilityCache cache) : IGoogleFontsCatalog {

	/// <summary>
	/// Per-probe budget, configured once on the typed client in <c>BindingsModule</c>. Kept short because
	/// it is the only bound on the blocking wait in <c>BuildThemeCommand.ResolveFontAvailability</c>; the
	/// <c>build-theme</c> MCP tool runs that probe BEFORE taking the shared execution lock, so a slow probe
	/// delays its own call instead of every other environment-less tool.
	/// </summary>
	internal static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

	private const string MetadataHost = "fonts.google.com";

	private const string MetadataPath = "metadata/fonts/";

	private static readonly string FamilyMetadataUrl =
		new UriBuilder(Uri.UriSchemeHttps, MetadataHost) { Path = MetadataPath }.Uri.ToString();

	private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
	private readonly IGoogleFontsAvailabilityCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));

	/// <inheritdoc />
	public async Task<GoogleFontAvailability> LookupAsync(string family, CancellationToken cancellationToken) {
		if (string.IsNullOrWhiteSpace(family)) {
			return GoogleFontAvailability.Unverified;
		}
		string key = FontImportBuilder.CollapseWhitespace(family.Trim());
		if (!FontImportBuilder.IsValidFamily(key)) {
			return GoogleFontAvailability.Unverified;
		}
		if (_cache.TryGet(key, out GoogleFontAvailability cached)) {
			return cached;
		}
		GoogleFontAvailability availability = await ProbeAsync(key, cancellationToken).ConfigureAwait(false);
		_cache.Store(key, availability);
		return availability;
	}

	private async Task<GoogleFontAvailability> ProbeAsync(string family, CancellationToken cancellationToken) {
		try {
			using HttpResponseMessage response = await _httpClient
				.GetAsync(FamilyMetadataUrl + Uri.EscapeDataString(family), HttpCompletionOption.ResponseHeadersRead, cancellationToken)
				.ConfigureAwait(false);
			if (response.StatusCode == HttpStatusCode.NotFound) {
				return GoogleFontAvailability.NotInCatalog;
			}
			return response.IsSuccessStatusCode && IsJson(response)
				? GoogleFontAvailability.InCatalog
				: GoogleFontAvailability.Unverified;
		} catch (HttpRequestException) {
			return GoogleFontAvailability.Unverified;
		} catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
			return GoogleFontAvailability.Unverified;
		}
	}

	private static bool IsJson(HttpResponseMessage response) {
		string mediaType = response.Content?.Headers?.ContentType?.MediaType;
		return mediaType is not null && mediaType.Contains("json", StringComparison.OrdinalIgnoreCase);
	}
}
