namespace Clio.Common.Skills;

/// <summary>
/// Result classification for a single coding agent's install/update/delete operation.
/// </summary>
public enum AgentOutcomeStatus {
	/// <summary>
	/// The agent was not acted on (home directory absent, or its CLI is not on PATH).
	/// Does not contribute to a non-zero exit code.
	/// </summary>
	Skipped,

	/// <summary>
	/// The operation completed successfully (including idempotent no-ops such as
	/// deleting an already-clean agent).
	/// </summary>
	Succeeded,

	/// <summary>
	/// The operation was attempted and failed. Contributes to a non-zero exit code.
	/// </summary>
	Failed
}

/// <summary>
/// Outcome of a single agent's operation, used to build the per-agent report,
/// the summary line, and the aggregate exit code.
/// </summary>
/// <param name="AgentId">Agent identifier (<c>claude</c> | <c>codex</c> | <c>cursor</c> | <c>copilot</c>).</param>
/// <param name="Status">Outcome classification.</param>
/// <param name="Message">User-facing message describing what happened.</param>
public sealed record AgentOutcome(string AgentId, AgentOutcomeStatus Status, string Message) {
	/// <summary>
	/// Creates a <see cref="AgentOutcomeStatus.Succeeded"/> outcome.
	/// </summary>
	public static AgentOutcome Succeeded(string agentId, string message) =>
		new(agentId, AgentOutcomeStatus.Succeeded, message);

	/// <summary>
	/// Creates a <see cref="AgentOutcomeStatus.Skipped"/> outcome.
	/// </summary>
	public static AgentOutcome Skipped(string agentId, string message) =>
		new(agentId, AgentOutcomeStatus.Skipped, message);

	/// <summary>
	/// Creates a <see cref="AgentOutcomeStatus.Failed"/> outcome.
	/// </summary>
	public static AgentOutcome Failed(string agentId, string message) =>
		new(agentId, AgentOutcomeStatus.Failed, message);
}
