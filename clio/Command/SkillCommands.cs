using Clio.Common;
using Clio.Common.Skills;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// Base options for the multi-agent skill lifecycle commands.
/// </summary>
public abstract class SkillCommandOptions {
	/// <summary>
	/// Optional agent to limit the operation to. When omitted, all detected agents are processed.
	/// </summary>
	[Option("target", Required = false,
		HelpText = "Limit the operation to one agent: claude | codex | cursor | copilot. Default: all detected agents.")]
	public string Target { get; set; }

	/// <summary>
	/// Optional source override. Marketplace git URL for claude/codex/copilot; local path or git URL for cursor.
	/// </summary>
	[Option("repo", Required = false,
		HelpText = "Override the install source. Marketplace git URL for claude/codex/copilot; local path or git URL for cursor.")]
	public string Repo { get; set; }

	// Deprecation shims: hidden, present only so a caller passing a removed option
	// receives an actionable error rather than CommandLineParser's generic message.
	[Option("scope", Hidden = true)]
	public string Scope { get; set; }

	[Option("skill", Hidden = true)]
	public string Skill { get; set; }

	/// <summary>
	/// Returns an actionable error when a removed option (<c>--scope</c>/<c>--skill</c>) was supplied.
	/// </summary>
	/// <param name="error">The error message to surface, when one applies.</param>
	/// <returns><c>true</c> when a removed option was used; otherwise <c>false</c>.</returns>
	public bool TryGetRemovedOptionError(out string error) {
		if (!string.IsNullOrWhiteSpace(Scope)) {
			error = "The --scope option has been removed. Skills now install globally for all detected coding agents. "
				+ "Use --target <claude|codex|cursor|copilot> to limit to one agent.";
			return true;
		}

		if (!string.IsNullOrWhiteSpace(Skill)) {
			error = "The --skill option has been removed. The whole Creatio toolkit bundle is installed per agent; "
				+ "per-skill selection is no longer supported.";
			return true;
		}

		error = null;
		return false;
	}
}

/// <summary>
/// Options for the <c>install-skills</c> command.
/// </summary>
[Verb("install-toolkit", Aliases = ["install-skills"],
	HelpText = "Install the Creatio AI App Development Toolkit for all detected coding agents")]
public class InstallSkillsOptions : SkillCommandOptions {
}

/// <summary>
/// Options for the <c>update-skill</c> command.
/// </summary>
[Verb("update-toolkit", Aliases = ["update-skill"],
	HelpText = "Update the Creatio AI App Development Toolkit for all detected coding agents")]
public class UpdateSkillOptions : SkillCommandOptions {
}

/// <summary>
/// Options for the <c>delete-skill</c> command.
/// </summary>
[Verb("delete-toolkit", Aliases = ["delete-skill"],
	HelpText = "Uninstall the Creatio AI App Development Toolkit from coding agents")]
public class DeleteSkillOptions : SkillCommandOptions {
}

/// <summary>
/// Reports an aggregated skill operation result through the logger.
/// </summary>
internal static class SkillCommandReporting {
	/// <summary>
	/// Writes per-agent outcomes and the summary, returning the result's exit code.
	/// </summary>
	public static int Report(ILogger logger, SkillCommandResult result) {
		foreach (AgentOutcome outcome in result.Outcomes) {
			if (outcome.Status == AgentOutcomeStatus.Failed) {
				logger.WriteError(outcome.Message);
			}
			else {
				logger.WriteInfo(outcome.Message);
			}
		}

		// A non-zero result with no per-agent outcomes is a validation failure
		// (unknown target / invalid --repo) whose explanation lives in the summary —
		// surface it as an error. Otherwise the summary lists what succeeded and is
		// informational (per-agent failures above already drive the non-zero exit).
		if (result.ExitCode != 0 && result.Outcomes.Count == 0) {
			logger.WriteError(result.Summary);
		}
		else {
			logger.WriteInfo(result.Summary);
		}

		return result.ExitCode;
	}
}

/// <summary>
/// Installs the Creatio toolkit skill globally for detected coding agents.
/// </summary>
public class InstallSkillsCommand(ISkillInstallService skillInstallService, ILogger logger)
	: Command<InstallSkillsOptions> {
	/// <inheritdoc />
	public override int Execute(InstallSkillsOptions options) {
		if (options.TryGetRemovedOptionError(out string error)) {
			logger.WriteError(error);
			return 1;
		}

		return SkillCommandReporting.Report(logger, skillInstallService.Install(options.Target, options.Repo));
	}
}

/// <summary>
/// Updates the Creatio toolkit skill for detected coding agents.
/// </summary>
public class UpdateSkillCommand(ISkillInstallService skillInstallService, ILogger logger)
	: Command<UpdateSkillOptions> {
	/// <inheritdoc />
	public override int Execute(UpdateSkillOptions options) {
		if (options.TryGetRemovedOptionError(out string error)) {
			logger.WriteError(error);
			return 1;
		}

		return SkillCommandReporting.Report(logger, skillInstallService.Update(options.Target, options.Repo));
	}
}

/// <summary>
/// Uninstalls the Creatio toolkit skill from detected coding agents.
/// </summary>
public class DeleteSkillCommand(ISkillInstallService skillInstallService, ILogger logger)
	: Command<DeleteSkillOptions> {
	/// <inheritdoc />
	public override int Execute(DeleteSkillOptions options) {
		if (options.TryGetRemovedOptionError(out string error)) {
			logger.WriteError(error);
			return 1;
		}

		return SkillCommandReporting.Report(logger, skillInstallService.Delete(options.Target));
	}
}
