using System;
using System.Collections.Concurrent;

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

	private readonly ConcurrentDictionary<string, RestartOperationRecord> _byId = new();
	private readonly ConcurrentDictionary<string, string> _latestIdByTenant = new();

	/// <inheritdoc/>
	public RestartOperationRecord Begin(string tenantKey, string environmentName) {
		RestartOperationRecord record = new(
			Guid.NewGuid().ToString("N"),
			tenantKey,
			environmentName,
			RestartOperationStatus.Running,
			DateTime.UtcNow,
			null,
			null);
		_byId[record.OperationId] = record;
		if (tenantKey is not null) {
			_latestIdByTenant[tenantKey] = record.OperationId;
		}
		return record;
	}

	/// <inheritdoc/>
	public RestartOperationRecord Finish(string operationId, int exitCode) {
		RestartOperationStatus status = exitCode == 0 ? RestartOperationStatus.Ready : RestartOperationStatus.TimedOut;
		DateTime finishedUtc = DateTime.UtcNow;
		return _byId.AddOrUpdate(
			operationId,
			// Unknown id: defensive fallback (Begin always precedes Finish in the real call path) so a
			// bookkeeping gap surfaces as an odd-looking record instead of an exception from the restart path.
			id => new RestartOperationRecord(id, null, null, status, finishedUtc, finishedUtc, exitCode),
			(_, existing) => existing with {
				Status = status,
				FinishedUtc = finishedUtc,
				ExitCode = exitCode
			});
	}

	/// <inheritdoc/>
	public RestartOperationRecord GetLatest(string tenantKey) {
		return tenantKey is not null && _latestIdByTenant.TryGetValue(tenantKey, out string operationId)
			? GetById(operationId)
			: null;
	}

	/// <inheritdoc/>
	public RestartOperationRecord GetById(string operationId) {
		return operationId is not null && _byId.TryGetValue(operationId, out RestartOperationRecord record)
			? record
			: null;
	}

}
