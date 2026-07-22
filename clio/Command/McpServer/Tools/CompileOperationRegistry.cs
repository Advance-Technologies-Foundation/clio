using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Lifecycle status of a tracked <c>compile-creatio</c> operation.
/// </summary>
public enum CompileOperationStatus {
	/// <summary>The compile is in flight — it has been accepted but has not yet reported an exit code.</summary>
	Running,

	/// <summary>The compile finished with exit code 0.</summary>
	Succeeded,

	/// <summary>The compile finished with a non-zero exit code (or resolution failed before it could run).</summary>
	Failed
}

/// <summary>
/// Snapshot of one <c>compile-creatio</c> invocation tracked by <see cref="ICompileOperationRegistry"/>,
/// returned by the <c>compile-status</c> tool.
/// </summary>
public sealed record CompileOperationRecord(
	string OperationId,
	string TenantKey,
	string EnvironmentName,
	string PackageName,
	CompileOperationStatus Status,
	DateTime StartedUtc,
	DateTime? FinishedUtc,
	int? ExitCode,
	IReadOnlyList<string> MessageTail);

/// <summary>
/// Tracks in-flight and recently finished <c>compile-creatio</c> operations so the <c>compile-status</c>
/// tool can report progress after an MCP response-deadline in-progress notice (ENG-91315). Process-local,
/// in-memory only: the clio MCP server is a single-session long-lived process, so no persistence is
/// needed beyond the current session.
/// </summary>
public interface ICompileOperationRegistry {

	/// <summary>
	/// Records a new running operation and makes it the latest tracked operation for
	/// <paramref name="tenantKey"/>.
	/// </summary>
	/// <param name="tenantKey">The resolved per-tenant execution-lock key the operation runs under.</param>
	/// <param name="environmentName">The target environment name, surfaced on status lookups.</param>
	/// <param name="packageName">The single package compiled, or <see langword="null"/> for a full compilation.</param>
	/// <returns>The newly created running record.</returns>
	CompileOperationRecord Begin(string tenantKey, string environmentName, string packageName);

	/// <summary>
	/// Finalizes a tracked operation with its exit code and a capped, already-redacted message tail.
	/// </summary>
	/// <param name="operationId">The id returned by <see cref="Begin"/>.</param>
	/// <param name="exitCode">The command exit code; <c>0</c> finalizes as <see cref="CompileOperationStatus.Succeeded"/>, anything else as <see cref="CompileOperationStatus.Failed"/>.</param>
	/// <param name="messages">The captured execution log, already sanitized/redacted by the caller.</param>
	/// <returns>The finalized record.</returns>
	CompileOperationRecord Finish(string operationId, int exitCode, IReadOnlyList<LogMessage> messages);

	/// <summary>
	/// Returns the most recently started operation for <paramref name="tenantKey"/>.
	/// </summary>
	/// <param name="tenantKey">The resolved per-tenant execution-lock key.</param>
	/// <returns>The latest tracked record, or <see langword="null"/> when none has run for this tenant.</returns>
	CompileOperationRecord GetLatest(string tenantKey);

	/// <summary>
	/// Returns the operation with the given id.
	/// </summary>
	/// <param name="operationId">The id returned by <see cref="Begin"/>.</param>
	/// <returns>The tracked record, or <see langword="null"/> when the id is unknown.</returns>
	CompileOperationRecord GetById(string operationId);

}

/// <inheritdoc cref="ICompileOperationRegistry"/>
public sealed class CompileOperationRegistry : ICompileOperationRegistry {

	/// <summary>Maximum number of trailing output lines retained per operation.</summary>
	internal const int MessageTailCap = 50;

	// Bounded store (Finding 5): idle-TTL + LRU eviction so the long-lived MCP process does not retain one
	// record per compile forever. A running compile is never evicted (its compile-status must stay
	// resolvable); terminal records age out by FinishedUtc.
	private readonly BoundedOperationStore<CompileOperationRecord> _store;
	// Same clock seam the store evicts by, so record timestamps (StartedUtc/FinishedUtc) and the eviction
	// idle/LRU comparison share ONE time source — a test that advances the clock moves both together.
	private readonly Func<DateTime> _utcNow;

	/// <summary>Production ctor: uses the same idle-TTL / capacity defaults as the neighboring caches.</summary>
	public CompileOperationRegistry()
		: this(SessionContainerCacheDefaults.IdleTtl, SessionContainerCacheDefaults.MaxSessions) { }

	/// <summary>Test/host seam: explicit idle-TTL, capacity, and clock for deterministic eviction tests.</summary>
	internal CompileOperationRegistry(TimeSpan idleTtl, int maxEntries, Func<DateTime> utcNow = null) {
		_utcNow = utcNow ?? (() => DateTime.UtcNow);
		_store = new BoundedOperationStore<CompileOperationRecord>(
			idleTtl,
			maxEntries,
			record => record.OperationId,
			record => record.Status == CompileOperationStatus.Running,
			record => record.FinishedUtc ?? record.StartedUtc,
			_utcNow);
	}

	/// <inheritdoc/>
	public CompileOperationRecord Begin(string tenantKey, string environmentName, string packageName) {
		CompileOperationRecord record = new(
			Guid.NewGuid().ToString("N"),
			tenantKey,
			environmentName,
			packageName,
			CompileOperationStatus.Running,
			_utcNow(),
			null,
			null,
			[]);
		_store.Add(tenantKey, record);
		return record;
	}

	/// <inheritdoc/>
	public CompileOperationRecord Finish(string operationId, int exitCode, IReadOnlyList<LogMessage> messages) {
		CompileOperationStatus status = exitCode == 0 ? CompileOperationStatus.Succeeded : CompileOperationStatus.Failed;
		IReadOnlyList<string> messageTail = BuildMessageTail(messages);
		DateTime finishedUtc = _utcNow();
		return _store.AddOrUpdate(
			operationId,
			// Unknown id: defensive fallback (Begin always precedes Finish in the real call path) so a
			// bookkeeping gap surfaces as an odd-looking record instead of an exception from the compile path.
			() => new CompileOperationRecord(operationId, null, null, null, status, finishedUtc, finishedUtc, exitCode, messageTail),
			existing => existing with {
				Status = status,
				FinishedUtc = finishedUtc,
				ExitCode = exitCode,
				MessageTail = messageTail
			});
	}

	/// <inheritdoc/>
	public CompileOperationRecord GetLatest(string tenantKey) => _store.GetLatest(tenantKey);

	/// <inheritdoc/>
	public CompileOperationRecord GetById(string operationId) => _store.GetById(operationId);

	private static IReadOnlyList<string> BuildMessageTail(IReadOnlyList<LogMessage> messages) {
		return messages is null
			? []
			: messages.TakeLast(MessageTailCap).Select(message => message.Value?.ToString() ?? string.Empty).ToArray();
	}

}
