using System;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Thrown by <see cref="McpProgressHeartbeat.RunWithProgressAndDeadlineAsync{TResult}"/> when a
/// long-running backend operation does not complete within the MCP response deadline. The operation
/// is <strong>not</strong> cancelled — it keeps running on the long-lived clio MCP server — so the
/// tool catching this exception must surface an "in-progress, poll" result rather than a failure
/// (see <c>spec/adr/adr-create-app-section-response-deadline.md</c>, ENG-91316).
/// </summary>
public sealed class McpResponseDeadlineExceededException : Exception {

	/// <summary>Initializes a new instance for the given operation and deadline.</summary>
	/// <param name="operationName">Human-readable operation label (typically the MCP tool name).</param>
	/// <param name="deadline">The wall-clock response budget that elapsed.</param>
	public McpResponseDeadlineExceededException(string operationName, TimeSpan deadline)
		: base($"'{operationName}' did not respond within {(int)deadline.TotalSeconds}s; "
			+ "the operation is still running server-side.") {
		OperationName = operationName;
		Deadline = deadline;
	}

	/// <summary>Operation label that exceeded the deadline.</summary>
	public string OperationName { get; }

	/// <summary>Wall-clock response budget that elapsed before the work completed.</summary>
	public TimeSpan Deadline { get; }
}
