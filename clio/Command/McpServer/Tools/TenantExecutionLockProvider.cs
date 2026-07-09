using System;
using System.Collections.Generic;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Hands out a per-credential-identity execution lock (FR-05, ENG-93208). Replaces the single
/// process-global MCP execution lock: same cache key ⇒ same lock object (so concurrent calls of the
/// SAME tenant still serialize against the shared authenticated client), distinct cache keys ⇒
/// distinct lock objects (so DIFFERENT tenants run concurrently).
/// </summary>
public interface ITenantExecutionLockProvider {

	/// <summary>
	/// Returns the execution lock for <paramref name="cacheKey"/>, creating it on first use. The same
	/// key always returns the same object; different keys return different objects.
	/// </summary>
	/// <param name="cacheKey">The credential-discriminating cache key (see <c>ToolCommandResolver</c>).</param>
	/// <returns>A stable lock object for the key.</returns>
	object GetLock(string cacheKey);

	/// <summary>
	/// Marks the lock for <paramref name="cacheKey"/> as held by an in-flight call, so eviction can
	/// never drop its mapping while a thread still holds the object. Balanced by <see cref="MarkAvailable"/>.
	/// No-op for an unknown key.
	/// </summary>
	/// <param name="cacheKey">The cache key whose lock is now held.</param>
	void MarkInUse(string cacheKey);

	/// <summary>
	/// Clears one in-use marker set by <see cref="MarkInUse"/> for <paramref name="cacheKey"/>.
	/// No-op for an unknown key or a lock not currently marked in use.
	/// </summary>
	/// <param name="cacheKey">The cache key whose in-flight call has completed.</param>
	void MarkAvailable(string cacheKey);
}

/// <summary>
/// Default <see cref="ITenantExecutionLockProvider"/>: a bounded key ⇒ lock map. Mirrors
/// <see cref="SessionContainerCache"/>'s eviction policy (idle-TTL + LRU-over-capacity) so a long-lived
/// mcp-http edge that mints a fresh key per rotating passthrough token
/// (<c>passthrough:{url}:{sha256(token)}</c>) stays memory-bounded instead of growing forever
/// (M1, ENG-93208 — the same unbounded-growth class Story 8's bounded cache fixed).
/// </summary>
/// <remarks>
/// <b>Eviction never breaks mutual exclusion.</b> A key whose lock is currently held (in-use count &gt; 0)
/// is never evicted: dropping the mapping and later minting a NEW object for the same key while an old
/// holder still runs would let a second thread lock the new object and run concurrently with the first.
/// The in-use count is driven by <see cref="MarkInUse"/> / <see cref="MarkAvailable"/>, which every lock
/// site calls immediately after acquiring / before releasing the monitor. The sub-microsecond window
/// between <see cref="GetLock"/> and the first <see cref="MarkInUse"/> is covered by the fresh
/// last-access timestamp set in <see cref="GetLock"/> (idle-TTL cannot fire on a just-touched entry, and
/// the LRU sweep never picks the newest entry). Evicting a mapping merely forgets it; an in-flight holder
/// keeps its own object reference, and a later <see cref="GetLock"/> for the same key mints a new object
/// only after the old one is no longer referenced.
/// <para>
/// <b>Note — the SESSION-CACHE window is NOT sub-microsecond.</b> This provider's own
/// <see cref="GetLock"/>→<see cref="MarkInUse"/> gap is trivially short, but on the
/// <c>InternalExecute</c> path the session container is <c>Acquire</c>d and only later marked in-use,
/// AFTER the package and Creatio-version gates (each a potential HTTP round-trip). That wider
/// <c>Acquire</c>→<c>MarkInUse</c> interval is the session cache's concern, not this lock map's: it is
/// covered on the typed-response path by <see cref="SessionContainerCache"/>'s pending-reservation guard
/// (FIX 1, ENG-93208), and stays benign on the <c>InternalExecute</c> path only because nothing on it is
/// <see cref="System.IDisposable"/> (see the disposal note on <see cref="SessionContainerCache"/>).
/// </para>
/// </remarks>
public sealed class TenantExecutionLockProvider : ITenantExecutionLockProvider {

	/// <summary>
	/// Process-wide shared instance. Locks must be shared across every container the MCP host builds
	/// (root + per-session ephemeral), so two calls of the same tenant coming from different containers
	/// still serialize on the same object.
	/// </summary>
	public static readonly ITenantExecutionLockProvider Shared = new TenantExecutionLockProvider();

	private sealed class LockEntry {
		public object Lock { get; init; }
		public DateTime LastAccessUtc { get; set; }
		public int InUseCount { get; set; }
	}

	private readonly Dictionary<string, LockEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
	private readonly object _sync = new();
	private readonly TimeSpan _idleTtl;
	private readonly int _maxEntries;
	private readonly Func<DateTime> _utcNow;

	private TenantExecutionLockProvider()
		: this(SessionContainerCacheDefaults.IdleTtl, SessionContainerCacheDefaults.MaxSessions) { }

	/// <summary>
	/// Test/host seam: creates a provider with an explicit idle-TTL, capacity, and clock.
	/// </summary>
	/// <param name="idleTtl">Idle time before an unheld lock mapping is evicted; must be positive.</param>
	/// <param name="maxEntries">Maximum number of retained lock mappings; must be positive.</param>
	/// <param name="utcNow">Clock seam for deterministic testing. Defaults to <see cref="DateTime.UtcNow"/>.</param>
	internal TenantExecutionLockProvider(TimeSpan idleTtl, int maxEntries, Func<DateTime> utcNow = null) {
		if (idleTtl <= TimeSpan.Zero) {
			throw new ArgumentOutOfRangeException(nameof(idleTtl), idleTtl,
				"Lock idle-TTL must be a positive duration.");
		}
		if (maxEntries <= 0) {
			throw new ArgumentOutOfRangeException(nameof(maxEntries), maxEntries,
				"Maximum lock-entry count must be greater than zero.");
		}
		_idleTtl = idleTtl;
		_maxEntries = maxEntries;
		_utcNow = utcNow ?? (() => DateTime.UtcNow);
	}

	/// <inheritdoc />
	public object GetLock(string cacheKey) {
		ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
		lock (_sync) {
			DateTime now = _utcNow();
			// Opportunistic sweep: idle-first, then capacity. Runs before create so an idle slot is
			// reclaimed before we count a new key against the cap.
			EvictIdle(now);
			if (_entries.TryGetValue(cacheKey, out LockEntry existing)) {
				existing.LastAccessUtc = now;
				return existing.Lock;
			}
			LockEntry entry = new() {
				Lock = new object(),
				LastAccessUtc = now,
				InUseCount = 0
			};
			_entries[cacheKey] = entry;
			EvictOverCapacity(cacheKey);
			return entry.Lock;
		}
	}

	/// <inheritdoc />
	public void MarkInUse(string cacheKey) {
		ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
		lock (_sync) {
			if (_entries.TryGetValue(cacheKey, out LockEntry entry)) {
				entry.InUseCount++;
				entry.LastAccessUtc = _utcNow();
			}
		}
	}

	/// <inheritdoc />
	public void MarkAvailable(string cacheKey) {
		ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
		lock (_sync) {
			if (_entries.TryGetValue(cacheKey, out LockEntry entry) && entry.InUseCount > 0) {
				entry.InUseCount--;
				entry.LastAccessUtc = _utcNow();
			}
		}
	}

	// Evicts every mapping idle past the TTL whose lock is not currently held. A held lock (InUseCount>0)
	// is never dropped — see the class remarks (would break mutual exclusion).
	private void EvictIdle(DateTime now) {
		List<string> expired = null;
		foreach (KeyValuePair<string, LockEntry> kvp in _entries) {
			if (kvp.Value.InUseCount == 0 && now - kvp.Value.LastAccessUtc > _idleTtl) {
				(expired ??= []).Add(kvp.Key);
			}
		}
		if (expired is null) {
			return;
		}
		foreach (string key in expired) {
			_entries.Remove(key);
		}
	}

	// Evicts the least-recently-used unheld mapping until the cap is met. Held locks and the just-added
	// key are never chosen: if every other mapping is held, a temporary overshoot is allowed rather than
	// dropping a mapping whose lock is still held.
	private void EvictOverCapacity(string justAddedKey) {
		while (_entries.Count > _maxEntries) {
			string victim = null;
			DateTime oldest = DateTime.MaxValue;
			foreach (KeyValuePair<string, LockEntry> kvp in _entries) {
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
				// Every other mapping is held (or the just-added is the only slack): allow overshoot,
				// never drop a mapping whose lock is still held.
				return;
			}
			_entries.Remove(victim);
		}
	}
}
