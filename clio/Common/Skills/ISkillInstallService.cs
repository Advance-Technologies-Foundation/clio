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
	SkillCommandResult Update(string target, string repo);

	/// <summary>
	/// Uninstalls the toolkit from all detected agents, or one when <paramref name="target"/> is set.
	/// </summary>
	SkillCommandResult Delete(string target);
}
