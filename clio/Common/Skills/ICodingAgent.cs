namespace Clio.Common.Skills;

/// <summary>
/// A coding agent the toolkit skill can be installed into, encapsulating that
/// agent's native install/update/delete mechanism.
/// </summary>
/// <remarks>
/// All four implementations are registered against this interface and resolved
/// together as <c>IEnumerable&lt;ICodingAgent&gt;</c> by the orchestrating service,
/// which dispatches by <see cref="AgentId"/>.
/// </remarks>
public interface ICodingAgent {
	/// <summary>
	/// Stable agent id (<c>claude</c> | <c>codex</c> | <c>cursor</c> | <c>copilot</c>).
	/// </summary>
	string AgentId { get; }

	/// <summary>
	/// Human-friendly agent name used in messages.
	/// </summary>
	string DisplayName { get; }

	/// <summary>
	/// Returns <c>true</c> when this agent is present on the machine (home dir exists).
	/// </summary>
	bool Detect();

	/// <summary>
	/// Installs the toolkit for this agent.
	/// </summary>
	AgentOutcome Install(AgentOperationContext context);

	/// <summary>
	/// Updates the toolkit for this agent.
	/// </summary>
	AgentOutcome Update(AgentOperationContext context);

	/// <summary>
	/// Uninstalls the toolkit from this agent (idempotent).
	/// </summary>
	AgentOutcome Delete(AgentOperationContext context);
}
