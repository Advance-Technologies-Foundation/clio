using System.Collections.Generic;

namespace Clio.Common.Skills;

/// <summary>
/// Aggregated result of a multi-agent skill lifecycle command.
/// </summary>
/// <param name="ExitCode">Process exit code (non-zero when any selected agent failed or the target was invalid).</param>
/// <param name="Outcomes">Per-agent outcomes, in display order.</param>
/// <param name="Summary">One-line human-readable summary.</param>
public sealed record SkillCommandResult(int ExitCode, IReadOnlyList<AgentOutcome> Outcomes, string Summary);

/// <summary>
/// Orchestrates install/update/delete of the toolkit skill across coding agents.
/// </summary>
/// <remarks>
/// Every operation holds a machine-wide toolkit lock; when another clio process is already running one,
/// the call returns a non-zero result without touching any agent.
/// </remarks>
public interface ISkillInstallService {
	/// <summary>
	/// Installs the toolkit for all detected agents, or one when <paramref name="target"/> is set.
	/// </summary>
	/// <param name="target">Optional agent id to narrow to; <c>null</c>/empty means all detected.</param>
	/// <param name="repo">Optional source override; <c>null</c>/empty means the default toolkit source.</param>
	SkillCommandResult Install(string target, string repo);

	/// <summary>
	/// Updates the toolkit for all detected agents, or one when <paramref name="target"/> is set.
	/// </summary>
	/// <param name="target">Optional agent id to narrow to; <c>null</c>/empty means all detected.</param>
	/// <param name="repo">Optional source override; <c>null</c>/empty means the default toolkit source.</param>
	/// <param name="force">
	/// <c>true</c> reinstalls every selected agent. <c>false</c> (default) skips an agent whose own recorded
	/// installed commit equals the commit published on the default source's release branch.
	/// </param>
	/// <remarks>
	/// The up-to-date check applies only to the default source; a <paramref name="repo"/> override always
	/// updates. It fails toward updating: when the published commit or the installed commit cannot be
	/// determined, the agent is updated exactly as without the check. A skipped agent also skips the
	/// configuration repairs its update performs (for example re-adding the clio MCP server entry); use
	/// <paramref name="force"/> or install to restore them.
	/// </remarks>
	SkillCommandResult Update(string target, string repo, bool force = false);

	/// <summary>
	/// Uninstalls the toolkit from all detected agents, or one when <paramref name="target"/> is set.
	/// </summary>
	SkillCommandResult Delete(string target);
}
