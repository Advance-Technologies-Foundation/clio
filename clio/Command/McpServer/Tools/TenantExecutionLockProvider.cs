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
	/// Returns the execution lock for <paramref name="cacheKey"/>, creating it on first use, and PINS the
	/// mapping in-use before returning (review #3, ENG-93208) so the object it hands out can never be
	/// evicted between hand-out and the caller taking the monitor. The same key always returns the same
	/// object; different keys return different objects. Every <see cref="GetLock"/> must be balanced by a
	/// <see cref="MarkAvailable"/> for the same key when the call completes.
	/// </summary>
	/// <param name="cacheKey">The credential-discriminating cache key (see <c>ToolCommandResolver</c>).</param>
	/// <returns>A stable, pinned lock object for the key.</returns>
	object GetLock(string cacheKey);

	/// <summary>
	/// Releases one in-use pin taken by <see cref="GetLock"/> for <paramref name="cacheKey"/>, making the
	/// mapping evictable again once no pin remains. No-op for an unknown key or a lock not currently pinned.
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
/// <b>The pin is taken inside <see cref="GetLock"/> itself (review #3, ENG-93208)</b>, not in a separate
/// later call: a hand-out atomically increments the in-use count under the map lock, so there is NO window
/// in which a just-handed-out mapping is unpinned and evictable — even under a burst that saturates the
/// map while the caller is preempted before it takes the monitor. Every <see cref="GetLock"/> is balanced
/// by a <see cref="MarkAvailable"/> when the call completes. Evicting a mapping merely forgets it; an
/// in-flight holder keeps its own object reference, and a later <see cref="GetLock"/> for the same key
/// mints a new object only after every pin on the old one is released.
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
				// Review #3: pin at hand-out so a concurrent burst cannot evict this mapping between the
				// hand-out and the caller taking the monitor. Balanced by MarkAvailable.
				existing.InUseCount++;
				return existing.Lock;
			}
			LockEntry entry = new() {
				Lock = new object(),
				LastAccessUtc = now,
				// Review #3: created already pinned — the mapping is protected the instant it is handed out,
				// closing the GetLock→(former)MarkInUse window under map-saturation eviction.
				InUseCount = 1
			};
			_entries[cacheKey] = entry;
			EvictOverCapacity(cacheKey);
			return entry.Lock;
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
