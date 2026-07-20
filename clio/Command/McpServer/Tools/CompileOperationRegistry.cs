using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Lifecycle status of a tracked <c>compile-creatio</c> operation.
/// </summary>
public enum CompileOperationStatus {
	Running,
	Succeeded,
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

	private readonly ConcurrentDictionary<string, CompileOperationRecord> _byId = new();
	private readonly ConcurrentDictionary<string, string> _latestIdByTenant = new();

	/// <inheritdoc/>
	public CompileOperationRecord Begin(string tenantKey, string environmentName, string packageName) {
		CompileOperationRecord record = new(
			Guid.NewGuid().ToString("N"),
			tenantKey,
			environmentName,
			packageName,
			CompileOperationStatus.Running,
			DateTime.UtcNow,
			null,
			null,
			[]);
		_byId[record.OperationId] = record;
		_latestIdByTenant[tenantKey] = record.OperationId;
		return record;
	}

	/// <inheritdoc/>
	public CompileOperationRecord Finish(string operationId, int exitCode, IReadOnlyList<LogMessage> messages) {
		CompileOperationStatus status = exitCode == 0 ? CompileOperationStatus.Succeeded : CompileOperationStatus.Failed;
		IReadOnlyList<string> messageTail = BuildMessageTail(messages);
		DateTime finishedUtc = DateTime.UtcNow;
		return _byId.AddOrUpdate(
			operationId,
			// Unknown id: defensive fallback (Begin always precedes Finish in the real call path) so a
			// bookkeeping gap surfaces as an odd-looking record instead of an exception from the compile path.
			_ => new CompileOperationRecord(operationId, null, null, null, status, finishedUtc, finishedUtc, exitCode, messageTail),
			(_, existing) => existing with {
				Status = status,
				FinishedUtc = finishedUtc,
				ExitCode = exitCode,
				MessageTail = messageTail
			});
	}

	/// <inheritdoc/>
	public CompileOperationRecord GetLatest(string tenantKey) {
		return tenantKey is not null && _latestIdByTenant.TryGetValue(tenantKey, out string operationId)
			? GetById(operationId)
			: null;
	}

	/// <inheritdoc/>
	public CompileOperationRecord GetById(string operationId) {
		return operationId is not null && _byId.TryGetValue(operationId, out CompileOperationRecord record)
			? record
			: null;
	}

	private static IReadOnlyList<string> BuildMessageTail(IReadOnlyList<LogMessage> messages) {
		return messages is null
			? []
			: messages.TakeLast(MessageTailCap).Select(message => message.Value?.ToString() ?? string.Empty).ToArray();
	}

}
