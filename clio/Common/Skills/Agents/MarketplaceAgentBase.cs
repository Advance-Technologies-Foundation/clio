using System;
using Clio.Common.Skills;

namespace Clio.Common.Skills.Agents;

/// <summary>
/// Base for agents installed through their own CLI's plugin marketplace
/// (Claude, Codex, Copilot). Encapsulates CLI preflight, the
/// register-marketplace-then-install flow, and tolerant teardown — a C# port of
/// the toolkit installer's <c>register_remote_marketplace_and_install_plugin</c>.
/// </summary>
public abstract class MarketplaceAgentBase(IFileSystem fileSystem, IUserHomeProvider homeProvider, IAgentCliRunner cli)
	: CodingAgentBase(fileSystem, homeProvider) {
	/// <summary>
	/// Gets the agent CLI runner.
	/// </summary>
	protected IAgentCliRunner Cli { get; } = cli;

	/// <summary>
	/// Gets the CLI executable name (e.g. <c>claude</c>).
	/// </summary>
	protected abstract string CliName { get; }

	/// <summary>
	/// Gets the plugin-install verb for this CLI (<c>install</c> or <c>add</c>).
	/// </summary>
	protected abstract string InstallVerb { get; }

	/// <summary>
	/// Gets the plugin-uninstall verb for this CLI (<c>uninstall</c> or <c>remove</c>).
	/// </summary>
	protected abstract string UninstallVerb { get; }

	/// <summary>
	/// Gets extra flags appended to <c>plugin marketplace remove</c>.
	/// </summary>
	protected virtual string[] MarketplaceRemoveFlags => [];

	/// <summary>
	/// Gets a value indicating whether the marketplace is always removed before re-adding.
	/// </summary>
	protected virtual bool PreRemoveMarketplace => false;

	/// <summary>
	/// Returns a <see cref="AgentOutcomeStatus.Skipped"/> outcome when the agent's
	/// CLI is not on PATH (decision NOQ-01), otherwise <c>null</c>.
	/// </summary>
	protected AgentOutcome SkipIfCliMissing() =>
		Cli.IsOnPath(CliName)
			? null
			: AgentOutcome.Skipped(AgentId, $"{DisplayName} is detected but '{CliName}' is not on PATH; skipped.");

	/// <summary>
	/// Runs <paramref name="body"/>, converting an agent-CLI failure into a
	/// <see cref="AgentOutcomeStatus.Failed"/> outcome.
	/// </summary>
	protected AgentOutcome Guarded(Func<AgentOutcome> body) {
		try {
			return body();
		}
		catch (AgentCliFailure failure) {
			return AgentOutcome.Failed(AgentId, failure.Message);
		}
	}

	/// <summary>
	/// Registers the remote marketplace and installs the plugin, mirroring the
	/// toolkit installer's two modes (unconditional pre-remove vs. conflict-driven retry).
	/// </summary>
	protected void RegisterMarketplaceAndInstall(string marketplaceUrl) {
		string[] addCommand = ["plugin", "marketplace", "add", marketplaceUrl];
		string[] installCommand = ["plugin", InstallVerb, ToolkitDistribution.PluginSource];

		if (PreRemoveMarketplace) {
			// Best-effort cleanup: the remove may fail on a re-install (e.g. the
			// marketplace still has the plugin installed) — that must NOT abort the
			// operation. The add/install below surface any real error and keep
			// re-running install idempotent.
			Run(MarketplaceRemoveCommand());
			RunOrThrow(addCommand);
			RunOrThrow(installCommand);
			return;
		}

		AgentCliResult add = Run(addCommand);
		if (!add.Succeeded) {
			if (!IsMarketplaceAlreadyRegistered(add.ErrorText)) {
				throw Failure("plugin marketplace add", add);
			}

			// Conflict: drop the stale marketplace (best-effort) and re-add.
			Run(MarketplaceRemoveCommand());
			RunOrThrow(addCommand);
		}

		RunOrThrow(installCommand);
	}

	/// <summary>
	/// Removes the plugin and marketplace, tolerating "not found / not installed"
	/// so delete is idempotent.
	/// </summary>
	protected void UninstallAndRemoveMarketplace() {
		RunTolerant(["plugin", UninstallVerb, ToolkitDistribution.PluginSource]);
		RunTolerant(MarketplaceRemoveCommand());
	}

	/// <summary>
	/// Runs a CLI command and throws when it fails.
	/// </summary>
	protected void RunOrThrow(params string[] args) {
		AgentCliResult result = Run(args);
		if (!result.Succeeded) {
			throw Failure(string.Join(' ', args), result);
		}
	}

	/// <summary>
	/// Runs a CLI command, swallowing "not found / not installed" failures (delete idempotency).
	/// </summary>
	protected void RunTolerant(string[] args) {
		AgentCliResult result = Run(args);
		if (!result.Succeeded && !IsNotFoundLike(result.ErrorText)) {
			throw Failure(string.Join(' ', args), result);
		}
	}

	/// <summary>
	/// Runs a CLI command and returns the raw result.
	/// </summary>
	protected AgentCliResult Run(params string[] args) => Cli.Run(CliName, args);

	private string[] MarketplaceRemoveCommand() {
		string[] baseArgs = ["plugin", "marketplace", "remove", ToolkitDistribution.MarketplaceName];
		if (MarketplaceRemoveFlags.Length == 0) {
			return baseArgs;
		}

		string[] combined = new string[baseArgs.Length + MarketplaceRemoveFlags.Length];
		baseArgs.CopyTo(combined, 0);
		MarketplaceRemoveFlags.CopyTo(combined, baseArgs.Length);
		return combined;
	}

	private AgentCliFailure Failure(string commandLabel, AgentCliResult result) =>
		new($"{CliName} {commandLabel} failed: {result.ErrorText}");

	private static bool IsMarketplaceAlreadyRegistered(string errorText) {
		string text = (errorText ?? string.Empty).ToLowerInvariant();
		string name = ToolkitDistribution.MarketplaceName;
		return text.Contains($"marketplace \"{name}\" already registered", StringComparison.Ordinal)
			|| text.Contains($"marketplace '{name}' is already added", StringComparison.Ordinal);
	}

	private static bool IsNotFoundLike(string errorText) {
		string text = (errorText ?? string.Empty).ToLowerInvariant();
		return text.Contains("not found", StringComparison.Ordinal)
			|| text.Contains("not installed", StringComparison.Ordinal)
			|| text.Contains("no such", StringComparison.Ordinal)
			|| text.Contains("is not configured", StringComparison.Ordinal);
	}

	/// <summary>
	/// Internal signal that an agent CLI command failed; converted to a
	/// <see cref="AgentOutcomeStatus.Failed"/> outcome by <see cref="Guarded"/>.
	/// </summary>
	protected sealed class AgentCliFailure(string message) : Exception(message);
}
