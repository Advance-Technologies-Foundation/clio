using System;
using System.Collections.Generic;

namespace Clio.Command.McpServer;

/// <summary>
/// Bounded, evictable cache of per-session <see cref="IServiceProvider"/> containers keyed by a
/// credential-discriminating cache key (see <c>ToolCommandResolver</c>). Replaces the process-wide
/// static dictionary that grew unbounded and could not evict, so a many-tenant mcp-http edge stays
/// memory-bounded (FR-08). Entries are evicted on idle-TTL and on an LRU capacity cap; an entry that
/// is marked in-use is never evicted mid-call (FR-08 in-flight guard).
/// </summary>
public interface ISessionContainerCache {

	/// <summary>
	/// Returns the cached container for <paramref name="cacheKey"/>, creating it via
	/// <paramref name="factory"/> on first use. Every call refreshes the entry's last-access
	/// timestamp and runs an opportunistic eviction sweep (idle-TTL then LRU-over-capacity).
	/// </summary>
	/// <param name="cacheKey">The credential-discriminating cache key.</param>
	/// <param name="factory">Builds the container when the key is not yet cached.</param>
	/// <returns>The cached or newly created <see cref="IServiceProvider"/>.</returns>
	IServiceProvider Acquire(string cacheKey, Func<IServiceProvider> factory);

	/// <summary>
	/// Marks the entry for <paramref name="cacheKey"/> as having an in-flight call, so eviction
	/// never disposes it mid-call. Balanced by <see cref="MarkAvailable"/>. No-op for an unknown key.
	/// </summary>
	/// <remarks>
	/// TODAY the global <c>McpToolExecutionLock</c> serializes all tool execution, so no container of
	/// another tenant can be evicted while a call is in flight; the execution-boundary wiring of
	/// <see cref="MarkInUse"/> / <see cref="MarkAvailable"/> is completed in Story 9 when that global
	/// lock is removed. The guard itself is implemented and unit-tested here so the eviction path can
	/// never pick an in-use entry.
	/// </remarks>
	/// <param name="cacheKey">The cache key whose entry is now in use.</param>
	void MarkInUse(string cacheKey);

	/// <summary>
	/// Clears one in-use marker set by <see cref="MarkInUse"/> for <paramref name="cacheKey"/>.
	/// No-op for an unknown key or an entry that is not currently in use.
	/// </summary>
	/// <param name="cacheKey">The cache key whose in-flight call has completed.</param>
	void MarkAvailable(string cacheKey);
}

/// <summary>
/// Default idle-TTL and capacity used when the host does not override them, plus the
/// <c>--session-idle-ttl</c> duration parser. Shared by <c>BindingsModule</c> (default singleton)
/// and <c>McpHttpServerCommand</c> (run-time-configured override).
/// </summary>
public static class SessionContainerCacheDefaults {

	/// <summary>Default idle time-to-live before an unused container is evicted.</summary>
	public static readonly TimeSpan IdleTtl = TimeSpan.FromMinutes(5);

	/// <summary>Default maximum number of concurrently cached session containers.</summary>
	public const int MaxSessions = 50;

	/// <summary>
	/// Parses the <c>--session-idle-ttl</c> value. Accepted forms: a bare number of seconds
	/// (<c>300</c>), a suffixed duration (<c>90s</c>, <c>5m</c>, <c>1h</c>, <c>1d</c>), or a
	/// <see cref="TimeSpan"/> string (<c>00:05:00</c>). A null/blank or unparseable value falls back
	/// to <see cref="IdleTtl"/>; a non-positive value is rejected in favor of the default.
	/// </summary>
	/// <param name="raw">The raw CLI value.</param>
	/// <returns>The resolved idle-TTL, never non-positive.</returns>
	public static TimeSpan ResolveIdleTtl(string raw) {
		if (string.IsNullOrWhiteSpace(raw)) {
			return IdleTtl;
		}
		string value = raw.Trim();
		TimeSpan parsed;
		char suffix = value[^1];
		if (char.IsLetter(suffix)) {
			string number = value[..^1].Trim();
			if (!double.TryParse(number, System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture, out double magnitude)) {
				return IdleTtl;
			}
			parsed = ToDuration(magnitude, suffix);
		}
		else if (double.TryParse(value, System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture, out double seconds)) {
			parsed = TimeSpan.FromSeconds(seconds);
		}
		else if (!TimeSpan.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out parsed)) {
			return IdleTtl;
		}

		return parsed > TimeSpan.Zero ? parsed : IdleTtl;
	}

	private static TimeSpan ToDuration(double magnitude, char suffix) {
		switch (char.ToLowerInvariant(suffix)) {
			case 's':
				return TimeSpan.FromSeconds(magnitude);
			case 'm':
				return TimeSpan.FromMinutes(magnitude);
			case 'h':
				return TimeSpan.FromHours(magnitude);
			case 'd':
				return TimeSpan.FromDays(magnitude);
			default:
				return IdleTtl;
		}
	}
}

/// <summary>
/// Default <see cref="ISessionContainerCache"/> implementation. Thread-safe via a single lock (all
/// operations are cheap); the factory is invoked under the lock so a key is built at most once.
/// </summary>
/// <remarks>
/// <para>
/// <b>Disposal / GC-safety (Story 8, AC-05 — decision (a)).</b> On eviction the child
/// <see cref="IServiceProvider"/> is disposed, which is sufficient. Decompiling
/// <c>Creatio.Client.CreatioClient</c> (creatio.client 1.0.38, netstandard2.0) shows it is declared
/// <c>class CreatioClient : ICreatioClient</c> — it does NOT implement <see cref="IDisposable"/> — and
/// holds no long-lived per-instance transport: its only fields are <c>string</c>/<c>bool</c>,
/// a <c>CookieContainer</c>, an <c>ICredentials</c> and a <c>RetryPolicy</c>. Every HTTP call creates
/// and disposes its own <c>HttpClient</c>/<c>HttpClientHandler</c> inside a <c>using</c> block
/// (per-request, no shared static or pooled handler), and the WebSocket listener
/// (<c>WsListenerNetFramework</c>, which owns a <c>ClientWebSocket</c> + 8&#160;MB buffer) is a
/// separate <see cref="IDisposable"/> object created only inside <c>StartListening</c> — it is never a
/// field of <c>CreatioClient</c> and is not touched by the request/response command path that the
/// cached passthrough containers use. <c>CreatioClientAdapter</c>/<c>IApplicationClient</c> are
/// likewise not <see cref="IDisposable"/> and wrap nothing long-lived. Therefore an evicted container
/// leaks no transport resource: disposing the provider releases any incidental IDisposable services,
/// and the adapter + client are then plain GC-collectable managed state. No custom transport
/// lifecycle (option (b)) is required.
/// </para>
/// </remarks>
public sealed class SessionContainerCache : ISessionContainerCache {

	private sealed class CacheEntry {
		public IServiceProvider Provider { get; init; }
		public DateTime LastAccessUtc { get; set; }
		public int InUseCount { get; set; }
	}

	private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
	private readonly object _sync = new();
	private readonly TimeSpan _idleTtl;
	private readonly int _maxSessions;
	private readonly Func<DateTime> _utcNow;

	/// <summary>
	/// Creates a cache with the given idle-TTL and capacity.
	/// </summary>
	/// <param name="idleTtl">Idle time before an unused container is evicted; must be positive.</param>
	/// <param name="maxSessions">Maximum number of cached containers; must be positive.</param>
	/// <param name="utcNow">
	/// Clock seam for deterministic testing. Defaults to <see cref="DateTime.UtcNow"/> in production.
	/// </param>
	public SessionContainerCache(TimeSpan idleTtl, int maxSessions, Func<DateTime> utcNow = null) {
		if (idleTtl <= TimeSpan.Zero) {
			throw new ArgumentOutOfRangeException(nameof(idleTtl), idleTtl,
				"Session idle-TTL must be a positive duration.");
		}
		if (maxSessions <= 0) {
			throw new ArgumentOutOfRangeException(nameof(maxSessions), maxSessions,
				"Maximum session count must be greater than zero.");
		}
		_idleTtl = idleTtl;
		_maxSessions = maxSessions;
		_utcNow = utcNow ?? (() => DateTime.UtcNow);
	}

	/// <inheritdoc />
	public IServiceProvider Acquire(string cacheKey, Func<IServiceProvider> factory) {
		ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
		ArgumentNullException.ThrowIfNull(factory);
		lock (_sync) {
			DateTime now = _utcNow();
			// Opportunistic sweep: idle-first, then capacity. Runs before create so an idle slot is
			// reclaimed before we count against the cap.
			EvictIdle(now);
			if (_entries.TryGetValue(cacheKey, out CacheEntry existing)) {
				existing.LastAccessUtc = now;
				return existing.Provider;
			}
			// Build under the lock so a given key is materialized exactly once even under contention.
			// A failing factory throws here BEFORE the entry is added, so no broken entry is cached.
			IServiceProvider provider = factory();
			_entries[cacheKey] = new CacheEntry {
				Provider = provider,
				LastAccessUtc = now,
				InUseCount = 0
			};
			EvictOverCapacity(cacheKey);
			return provider;
		}
	}

	/// <inheritdoc />
	public void MarkInUse(string cacheKey) {
		ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
		lock (_sync) {
			if (_entries.TryGetValue(cacheKey, out CacheEntry entry)) {
				entry.InUseCount++;
				entry.LastAccessUtc = _utcNow();
			}
		}
	}

	/// <inheritdoc />
	public void MarkAvailable(string cacheKey) {
		ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
		lock (_sync) {
			if (_entries.TryGetValue(cacheKey, out CacheEntry entry) && entry.InUseCount > 0) {
				entry.InUseCount--;
				entry.LastAccessUtc = _utcNow();
			}
		}
	}

	// Evicts every entry idle past the TTL. In-use entries are never evicted (FR-08 in-flight guard).
	private void EvictIdle(DateTime now) {
		List<string> expired = null;
		foreach (KeyValuePair<string, CacheEntry> kvp in _entries) {
			if (kvp.Value.InUseCount == 0 && now - kvp.Value.LastAccessUtc > _idleTtl) {
				(expired ??= []).Add(kvp.Key);
			}
		}
		if (expired is null) {
			return;
		}
		foreach (string key in expired) {
			RemoveAndDispose(key);
		}
	}

	// Evicts the least-recently-used evictable entry until the cap is met. In-use entries and the
	// just-added entry are never chosen: if every other entry is in-use, a temporary overshoot is
	// allowed rather than evicting an in-flight container OR the container the caller just requested.
	private void EvictOverCapacity(string justAddedKey) {
		while (_entries.Count > _maxSessions) {
			string victim = null;
			DateTime oldest = DateTime.MaxValue;
			foreach (KeyValuePair<string, CacheEntry> kvp in _entries) {
				if (kvp.Value.InUseCount > 0
					|| string.Equals(kvp.Key, justAddedKey, StringComparison.OrdinalIgnoreCase)) {
					continue;
				}
				if (kvp.Value.LastAccessUtc < oldest) {
					oldest = kvp.Value.LastAccessUtc;
					victim = kvp.Key;
				}
			}
			if (victim is null) {
				// Every other entry is in-use (or the just-added is the only slack): allow overshoot,
				// never evict mid-call or drop the container the caller is about to use.
				return;
			}
			RemoveAndDispose(victim);
		}
	}

	private void RemoveAndDispose(string cacheKey) {
		if (_entries.Remove(cacheKey, out CacheEntry entry)) {
			(entry.Provider as IDisposable)?.Dispose();
		}
	}
}
