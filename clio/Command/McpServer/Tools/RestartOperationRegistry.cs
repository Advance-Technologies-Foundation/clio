using System;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Lifecycle status of a tracked restart readiness wait.
/// </summary>
public enum RestartOperationStatus {
	/// <summary>The restart request already succeeded and the readiness wait is still polling.</summary>
	Running,

	/// <summary>The instance answered its health-check within the timeout — it is ready.</summary>
	Ready,

	/// <summary>The instance did not answer its health-check before the readiness timeout elapsed.</summary>
	TimedOut
}

/// <summary>
/// Snapshot of one restart readiness wait tracked by <see cref="IRestartOperationRegistry"/>, returned by
/// the <c>restart-status</c> tool.
/// </summary>
public sealed record RestartOperationRecord(
	string OperationId,
	string TenantKey,
	string EnvironmentName,
	RestartOperationStatus Status,
	DateTime StartedUtc,
	DateTime? FinishedUtc,
	int? ExitCode);

/// <summary>
/// Tracks in-flight and recently finished restart readiness waits so the <c>restart-status</c> tool can
/// report progress after an MCP response-deadline in-progress notice (ENG-91315). Process-local, in-memory
/// only: the clio MCP server is a single-session long-lived process, so no persistence is needed beyond the
/// current session. Mirrors <see cref="ICompileOperationRegistry"/> for the restart path — the restart
/// request itself runs under the per-tenant execution lock, but the read-only readiness wait is detached and
/// lock-free, so its progress is surfaced here rather than by holding a response open.
/// </summary>
public interface IRestartOperationRegistry {

	/// <summary>
	/// Records a new running readiness wait and makes it the latest tracked operation for
	/// <paramref name="tenantKey"/>.
	/// </summary>
	/// <param name="tenantKey">The resolved per-tenant execution-lock key the restart ran under.</param>
	/// <param name="environmentName">The target environment name, surfaced on status lookups; <see langword="null"/> for a credentials-only restart.</param>
	/// <returns>The newly created running record.</returns>
	RestartOperationRecord Begin(string tenantKey, string environmentName);

	/// <summary>
	/// Finalizes a tracked readiness wait with its exit code.
	/// </summary>
	/// <param name="operationId">The id returned by <see cref="Begin"/>.</param>
	/// <param name="exitCode">The readiness result; <c>0</c> finalizes as <see cref="RestartOperationStatus.Ready"/>, anything else as <see cref="RestartOperationStatus.TimedOut"/>.</param>
	/// <returns>The finalized record.</returns>
	RestartOperationRecord Finish(string operationId, int exitCode);

	/// <summary>
	/// Returns the most recently started readiness wait for <paramref name="tenantKey"/>.
	/// </summary>
	/// <param name="tenantKey">The resolved per-tenant execution-lock key.</param>
	/// <returns>The latest tracked record, or <see langword="null"/> when none has run for this tenant.</returns>
	RestartOperationRecord GetLatest(string tenantKey);

	/// <summary>
	/// Returns the operation with the given id.
	/// </summary>
	/// <param name="operationId">The id returned by <see cref="Begin"/>.</param>
	/// <returns>The tracked record, or <see langword="null"/> when the id is unknown.</returns>
	RestartOperationRecord GetById(string operationId);

}

/// <inheritdoc cref="IRestartOperationRegistry"/>
public sealed class RestartOperationRegistry : IRestartOperationRegistry {

	// Bounded store (Finding 5): idle-TTL + LRU eviction so the long-lived MCP process does not retain one
	// record per restart forever. A running readiness wait is never evicted (its restart-status must stay
	// resolvable); terminal records age out by FinishedUtc.
	private readonly BoundedOperationStore<RestartOperationRecord> _store;
	// Same clock seam the store evicts by, so record timestamps (StartedUtc/FinishedUtc) and the eviction
	// idle/LRU comparison share ONE time source — a test that advances the clock moves both together.
	private readonly Func<DateTime> _utcNow;

	/// <summary>Production ctor: uses the same idle-TTL / capacity defaults as the neighboring caches.</summary>
	public RestartOperationRegistry()
		: this(SessionContainerCacheDefaults.IdleTtl, SessionContainerCacheDefaults.MaxSessions) { }

	/// <summary>Test/host seam: explicit idle-TTL, capacity, and clock for deterministic eviction tests.</summary>
	internal RestartOperationRegistry(TimeSpan idleTtl, int maxEntries, Func<DateTime> utcNow = null) {
		_utcNow = utcNow ?? (() => DateTime.UtcNow);
		_store = new BoundedOperationStore<RestartOperationRecord>(
			idleTtl,
			maxEntries,
			record => record.OperationId,
			record => record.Status == RestartOperationStatus.Running,
			record => record.FinishedUtc ?? record.StartedUtc,
			_utcNow);
	}

	/// <inheritdoc/>
	public RestartOperationRecord Begin(string tenantKey, string environmentName) {
		RestartOperationRecord record = new(
			Guid.NewGuid().ToString("N"),
			tenantKey,
			environmentName,
			RestartOperationStatus.Running,
			_utcNow(),
			null,
			null);
		_store.Add(tenantKey, record);
		return record;
	}

	/// <inheritdoc/>
	public RestartOperationRecord Finish(string operationId, int exitCode) {
		RestartOperationStatus status = exitCode == 0 ? RestartOperationStatus.Ready : RestartOperationStatus.TimedOut;
		DateTime finishedUtc = _utcNow();
		return _store.AddOrUpdate(
			operationId,
			// Unknown id: defensive fallback (Begin always precedes Finish in the real call path) so a
			// bookkeeping gap surfaces as an odd-looking record instead of an exception from the restart path.
			() => new RestartOperationRecord(operationId, null, null, status, finishedUtc, finishedUtc, exitCode),
			existing => existing with {
				Status = status,
				FinishedUtc = finishedUtc,
				ExitCode = exitCode
			});
	}

	/// <inheritdoc/>
	public RestartOperationRecord GetLatest(string tenantKey) => _store.GetLatest(tenantKey);

	/// <inheritdoc/>
	public RestartOperationRecord GetById(string operationId) => _store.GetById(operationId);

}
