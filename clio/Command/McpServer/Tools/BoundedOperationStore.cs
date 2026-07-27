using System;
using System.Collections.Generic;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Bounded, in-memory store of tracked MCP operation records keyed by operation-id, with a
/// latest-per-tenant index. Applies the SAME idle-TTL + LRU-over-capacity eviction policy as
/// <see cref="TenantExecutionLockProvider"/> / <see cref="SessionContainerCache"/> (M1, ENG-93208) so the
/// long-lived clio MCP process does not grow one retained record per operation forever — the
/// unbounded-growth class both neighboring caches were built to prevent (ENG-91315 review, Finding 5).
/// Shared by <see cref="CompileOperationRegistry"/> and <see cref="RestartOperationRegistry"/> so the
/// eviction invariants live and are tested in one place rather than duplicated per registry.
/// </summary>
/// <remarks>
/// <b>Eviction never drops a running operation.</b> A record the caller reports as still running
/// (<paramref name="isRunning"/> is <see langword="true"/>) is never evicted — dropping it would make
/// <c>compile-status</c>/<c>restart-status</c> report "not found" for an operation that is genuinely
/// in flight. Only terminal (finished) records are evictable, keyed on their last-activity time
/// (<paramref name="lastActivityOf"/>, typically the finish timestamp). If every retained record is
/// running, a temporary capacity overshoot is allowed rather than dropping a live operation — exactly the
/// "never evict a held mapping" rule <see cref="TenantExecutionLockProvider"/> follows for in-use locks.
/// The sweep is opportunistic (runs on <see cref="Add"/>, i.e. each new operation), like the lock
/// provider's sweep-on-hand-out, so no background timer is needed.
/// </remarks>
internal sealed class BoundedOperationStore<TRecord> where TRecord : class {

	private readonly Dictionary<string, TRecord> _byId = new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _latestIdByTenant = new(StringComparer.Ordinal);
	private readonly object _sync = new();
	private readonly TimeSpan _idleTtl;
	private readonly int _maxEntries;
	private readonly Func<DateTime> _utcNow;
	private readonly Func<TRecord, string> _idOf;
	private readonly Func<TRecord, bool> _isRunning;
	private readonly Func<TRecord, DateTime> _lastActivityOf;

	/// <summary>
	/// Creates a bounded store.
	/// </summary>
	/// <param name="idleTtl">Idle time before a terminal record is evicted; must be positive.</param>
	/// <param name="maxEntries">Maximum number of retained records; must be positive.</param>
	/// <param name="idOf">Extracts the operation-id from a record (the primary key).</param>
	/// <param name="isRunning">Whether a record is still in flight; running records are never evicted.</param>
	/// <param name="lastActivityOf">The record's last-activity timestamp used for idle/LRU ordering.</param>
	/// <param name="utcNow">Clock seam for deterministic testing. Defaults to <see cref="DateTime.UtcNow"/>.</param>
	internal BoundedOperationStore(
		TimeSpan idleTtl,
		int maxEntries,
		Func<TRecord, string> idOf,
		Func<TRecord, bool> isRunning,
		Func<TRecord, DateTime> lastActivityOf,
		Func<DateTime> utcNow = null) {
		if (idleTtl <= TimeSpan.Zero) {
			throw new ArgumentOutOfRangeException(nameof(idleTtl), idleTtl,
				"Operation idle-TTL must be a positive duration.");
		}
		if (maxEntries <= 0) {
			throw new ArgumentOutOfRangeException(nameof(maxEntries), maxEntries,
				"Maximum operation-record count must be greater than zero.");
		}
		_idleTtl = idleTtl;
		_maxEntries = maxEntries;
		_idOf = idOf ?? throw new ArgumentNullException(nameof(idOf));
		_isRunning = isRunning ?? throw new ArgumentNullException(nameof(isRunning));
		_lastActivityOf = lastActivityOf ?? throw new ArgumentNullException(nameof(lastActivityOf));
		_utcNow = utcNow ?? (() => DateTime.UtcNow);
	}

	/// <summary>
	/// Adds a new (running) record and makes it the latest for <paramref name="tenantKey"/>, then sweeps
	/// idle-first, then capacity — mirroring the lock provider's sweep-before-create so a reclaimable slot
	/// is freed before the new record is counted against the cap.
	/// </summary>
	internal void Add(string tenantKey, TRecord record) {
		lock (_sync) {
			DateTime now = _utcNow();
			EvictIdle(now);
			string id = _idOf(record);
			_byId[id] = record;
			if (tenantKey is not null) {
				_latestIdByTenant[tenantKey] = id;
			}
			EvictOverCapacity(id);
		}
	}

	/// <summary>
	/// Updates the record with <paramref name="operationId"/> via <paramref name="update"/>, or inserts the
	/// <paramref name="whenMissing"/> fallback when the id is unknown (a bookkeeping gap — Begin always
	/// precedes Finish on the real path). Does not sweep: finalizing an operation never grows the table.
	/// </summary>
	internal TRecord AddOrUpdate(string operationId, Func<TRecord> whenMissing, Func<TRecord, TRecord> update) {
		lock (_sync) {
			TRecord result = _byId.TryGetValue(operationId, out TRecord existing) ? update(existing) : whenMissing();
			_byId[_idOf(result)] = result;
			return result;
		}
	}

	/// <summary>Returns the record with the given id, or <see langword="null"/> when unknown/evicted.</summary>
	internal TRecord GetById(string operationId) {
		lock (_sync) {
			return operationId is not null && _byId.TryGetValue(operationId, out TRecord record) ? record : null;
		}
	}

	/// <summary>Returns the latest record for the tenant, or <see langword="null"/> when none/evicted.</summary>
	internal TRecord GetLatest(string tenantKey) {
		lock (_sync) {
			return tenantKey is not null
				&& _latestIdByTenant.TryGetValue(tenantKey, out string operationId)
				&& _byId.TryGetValue(operationId, out TRecord record)
					? record
					: null;
		}
	}

	// Evicts every terminal record idle past the TTL. A running record is never dropped (see class remarks).
	private void EvictIdle(DateTime now) {
		List<string> expired = null;
		foreach (KeyValuePair<string, TRecord> kvp in _byId) {
			if (!_isRunning(kvp.Value) && now - _lastActivityOf(kvp.Value) > _idleTtl) {
				(expired ??= []).Add(kvp.Key);
			}
		}
		if (expired is null) {
			return;
		}
		foreach (string id in expired) {
			Remove(id);
		}
	}

	// Evicts the least-recently-active terminal record until the cap is met. Running records and the
	// just-added id are never chosen: if every other record is running, a temporary overshoot is allowed
	// rather than dropping a live operation.
	private void EvictOverCapacity(string justAddedId) {
		while (_byId.Count > _maxEntries) {
			string victim = null;
			DateTime oldest = DateTime.MaxValue;
			foreach (KeyValuePair<string, TRecord> kvp in _byId) {
				if (_isRunning(kvp.Value) || string.Equals(kvp.Key, justAddedId, StringComparison.Ordinal)) {
					continue;
				}
				if (_lastActivityOf(kvp.Value) < oldest) {
					oldest = _lastActivityOf(kvp.Value);
					victim = kvp.Key;
				}
			}
			if (victim is null) {
				// Every other record is running (or the just-added is the only slack): allow overshoot,
				// never drop a live operation.
				return;
			}
			Remove(victim);
		}
	}

	// Removes a record and any tenant index entry that pointed at it, so an evicted id leaves no dangling
	// latest-per-tenant mapping (which would otherwise be a slow second leak in the tenant index).
	private void Remove(string operationId) {
		_byId.Remove(operationId);
		string staleTenant = null;
		foreach (KeyValuePair<string, string> kvp in _latestIdByTenant) {
			if (string.Equals(kvp.Value, operationId, StringComparison.Ordinal)) {
				staleTenant = kvp.Key;
				break;
			}
		}
		if (staleTenant is not null) {
			_latestIdByTenant.Remove(staleTenant);
		}
	}
}
