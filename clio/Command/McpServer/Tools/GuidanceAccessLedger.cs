using System;
using System.Collections.Generic;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Process-scoped record of which clio MCP guidance articles have been fetched during the
/// current <c>clio mcp-server</c> process lifetime.
/// </summary>
/// <remarks>
/// clio MCP runs over stdio: one <c>clio mcp-server</c> process serves a single client session
/// (subagents and forks share the same process), so a process-lifetime singleton is the correct
/// scope for "which guidance did this session already read" state. Implementations must be
/// thread-safe — guidance-consuming flows such as sync-pages process pages concurrently — and
/// must compare guidance names case-insensitively to match the catalog's
/// <see cref="StringComparer.OrdinalIgnoreCase"/> keying.
/// </remarks>
public interface IGuidanceAccessLedger {
	/// <summary>
	/// Records that the named guidance article was fetched in this process. Pass the canonical
	/// catalog name. Recording the same name more than once is a no-op. Null, empty, or
	/// whitespace names are ignored.
	/// </summary>
	/// <param name="guidanceName">The canonical guidance name to record.</param>
	void Record(string guidanceName);

	/// <summary>
	/// Returns whether the named guidance article was fetched in this process. The comparison is
	/// case-insensitive (<see cref="StringComparer.OrdinalIgnoreCase"/>). A null, empty, or
	/// whitespace name always returns <see langword="false"/>.
	/// </summary>
	/// <param name="guidanceName">The guidance name to check.</param>
	/// <returns><see langword="true"/> when the name was recorded; otherwise <see langword="false"/>.</returns>
	bool WasFetched(string guidanceName);

	/// <summary>
	/// Gets a snapshot of the distinct canonical guidance names recorded so far in this process.
	/// The returned collection is a point-in-time copy and is not affected by later
	/// <see cref="Record(string)"/> calls.
	/// </summary>
	IReadOnlyCollection<string> Fetched { get; }
}

/// <summary>
/// Thread-safe, process-lifetime implementation of <see cref="IGuidanceAccessLedger"/> backed by
/// a case-insensitive set. Registered as a singleton so every MCP tool invocation in the process
/// shares one ledger.
/// </summary>
public sealed class GuidanceAccessLedger : IGuidanceAccessLedger {
	private readonly HashSet<string> _fetched = new(StringComparer.OrdinalIgnoreCase);
	private readonly object _syncRoot = new();

	/// <inheritdoc />
	public void Record(string guidanceName) {
		if (string.IsNullOrWhiteSpace(guidanceName)) {
			return;
		}
		lock (_syncRoot) {
			_fetched.Add(guidanceName);
		}
	}

	/// <inheritdoc />
	public bool WasFetched(string guidanceName) {
		if (string.IsNullOrWhiteSpace(guidanceName)) {
			return false;
		}
		lock (_syncRoot) {
			return _fetched.Contains(guidanceName);
		}
	}

	/// <inheritdoc />
	public IReadOnlyCollection<string> Fetched {
		get {
			lock (_syncRoot) {
				return new List<string>(_fetched);
			}
		}
	}
}
