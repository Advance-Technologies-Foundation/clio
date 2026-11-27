using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Common.Skills;

/// <summary>
/// Default <see cref="ISkillInstallService"/>. Resolves the selected agents,
/// dispatches the requested operation to each, aggregates per-agent outcomes,
/// maintains the managed-skills manifest, and computes the exit code.
/// </summary>
public sealed class SkillInstallService(
	IEnumerable<ICodingAgent> agents,
	IManagedSkillsManifestStore manifestStore,
	IInstalledToolkitVersions installedVersions,
	IToolkitReleaseProbe releaseProbe,
	IToolkitOperationLock operationLock)
	: ISkillInstallService {
	private const int ShortCommitLength = 7;

	internal const string OperationInProgressMessage =
		"Another Creatio toolkit install, update, or removal is already running in another clio process. "
		+ "Run the command again when it finishes.";

	private readonly IReadOnlyList<ICodingAgent> _agents = agents
		.OrderBy(agent => agent.AgentId, StringComparer.OrdinalIgnoreCase)
		.ToList();
	private readonly IManagedSkillsManifestStore _manifestStore = manifestStore;
	private readonly IInstalledToolkitVersions _installedVersions = installedVersions;
	private readonly IToolkitReleaseProbe _releaseProbe = releaseProbe;
	private readonly IToolkitOperationLock _operationLock = operationLock;

	/// <inheritdoc />
	public SkillCommandResult Install(string target, string repo) =>
		Run(SkillOperationKind.Install, target, repo, skipAgentsOnRelease: false);

	/// <inheritdoc />
	public SkillCommandResult Update(string target, string repo, bool force = false) =>
		Run(SkillOperationKind.Update, target, repo, skipAgentsOnRelease: !force);

	/// <inheritdoc />
	public SkillCommandResult Delete(string target) =>
		Run(SkillOperationKind.Delete, target, repositoryOverride: null, skipAgentsOnRelease: false);

	private SkillCommandResult Run(SkillOperationKind kind, string target, string repositoryOverride,
		bool skipAgentsOnRelease) {
		string normalizedTarget = target?.Trim();
		if (Validate(normalizedTarget, target, repositoryOverride) is SkillCommandResult invalid) {
			return invalid;
		}

		using IDisposable operationHandle = _operationLock.TryAcquire();
		if (operationHandle is null) {
			return NotRun(OperationInProgressMessage);
		}

		AgentOperationContext context = new(kind, repositoryOverride);
		// Only the default source has a published release commit to compare against.
		bool checkUpToDate = skipAgentsOnRelease && string.IsNullOrWhiteSpace(repositoryOverride);
		Lazy<string> releaseCommit = new(_releaseProbe.ReadReleaseCommit);
		List<AgentOutcome> outcomes = SelectAgents(normalizedTarget)
			.Select(agent => RunForAgent(agent, context, checkUpToDate, releaseCommit))
			.ToList();
		RecordSucceededAgents(kind, context, outcomes);

		int exitCode = outcomes.Any(outcome => outcome.Status == AgentOutcomeStatus.Failed) ? 1 : 0;
		return new SkillCommandResult(exitCode, outcomes, BuildSummary(kind, outcomes));
	}

	private SkillCommandResult Validate(string normalizedTarget, string target, string repositoryOverride) {
		if (!string.IsNullOrEmpty(normalizedTarget) && !SelectAgents(normalizedTarget).Any()) {
			string valid = string.Join(", ", _agents.Select(agent => agent.AgentId));
			return Invalid($"Unknown target '{target}'. Valid agents: {valid}.");
		}

		return RepositorySource.TryValidate(repositoryOverride, out string repositoryError)
			? null
			: Invalid(repositoryError);
	}

	private IEnumerable<ICodingAgent> SelectAgents(string normalizedTarget) =>
		string.IsNullOrEmpty(normalizedTarget)
			? _agents
			: _agents.Where(agent => string.Equals(agent.AgentId, normalizedTarget, StringComparison.OrdinalIgnoreCase));

	private AgentOutcome RunForAgent(ICodingAgent agent, AgentOperationContext context, bool checkUpToDate,
		Lazy<string> releaseCommit) {
		if (!agent.Detect()) {
			return AgentOutcome.Skipped(agent.AgentId, $"{agent.DisplayName} is not installed; nothing to do.");
		}

		return checkUpToDate && TryGetUpToDateOutcome(agent, releaseCommit) is AgentOutcome upToDate
			? upToDate
			: Dispatch(agent, context.Operation, context);
	}

	/// <summary>
	/// Records every agent that changed state in the managed-skills manifest, saving it only when one did.
	/// Up-to-date and skipped agents are not recorded: nothing was installed for them.
	/// </summary>
	private void RecordSucceededAgents(SkillOperationKind kind, AgentOperationContext context,
		IReadOnlyList<AgentOutcome> outcomes) {
		string[] succeeded = AgentIds(outcomes, AgentOutcomeStatus.Succeeded);
		if (succeeded.Length == 0) {
			return;
		}

		ManagedSkillsManifest manifest = _manifestStore.Read();
		DateTimeOffset now = DateTimeOffset.UtcNow;
		foreach (string agentId in succeeded) {
			if (kind == SkillOperationKind.Delete) {
				manifest.Remove(agentId);
			}
			else {
				manifest.Upsert(agentId, context.MarketplaceUrl, now);
			}
		}

		_manifestStore.Save(manifest);
	}

	/// <summary>
	/// Returns an up-to-date outcome when the agent's own recorded commit equals the published release
	/// commit; otherwise <c>null</c>, which means "update". The installed commit is read first so an agent
	/// that records no commit (Cursor, Copilot) never costs a network call.
	/// </summary>
	private AgentOutcome TryGetUpToDateOutcome(ICodingAgent agent, Lazy<string> releaseCommit) {
		string installed = _installedVersions.ReadInstalledCommit(agent.AgentId);
		if (installed is null) {
			return null;
		}

		string published = releaseCommit.Value;
		if (published is null || !string.Equals(installed, published, StringComparison.Ordinal)) {
			return null;
		}

		return AgentOutcome.UpToDate(agent.AgentId,
			$"{agent.DisplayName} already has the latest {ToolkitDistribution.PluginName} "
			+ $"({published[..ShortCommitLength]}); nothing to update.");
	}

	private static SkillCommandResult Invalid(string message) => NotRun(message);

	/// <summary>A non-zero result with no per-agent outcomes: the operation did not reach any agent.</summary>
	private static SkillCommandResult NotRun(string message) =>
		new(1, Array.Empty<AgentOutcome>(), message);

	private static AgentOutcome Dispatch(ICodingAgent agent, SkillOperationKind kind, AgentOperationContext context) {
		try {
			return kind switch {
				SkillOperationKind.Install => agent.Install(context),
				SkillOperationKind.Update => agent.Update(context),
				SkillOperationKind.Delete => agent.Delete(context),
				_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported skill operation.")
			};
		}
		catch (Exception exception) {
			// Backstop: agents curate their own failures (marketplace agents via Guarded,
			// Cursor via its try/catch), so this only fires on a truly unexpected error.
			return AgentOutcome.Failed(agent.AgentId, $"{agent.DisplayName} {Verb(kind)} failed: {exception.Message}");
		}
	}

	private static string BuildSummary(SkillOperationKind kind, IReadOnlyList<AgentOutcome> outcomes) {
		string[] succeeded = AgentIds(outcomes, AgentOutcomeStatus.Succeeded);
		string[] upToDate = AgentIds(outcomes, AgentOutcomeStatus.UpToDate);
		string upToDateText = upToDate.Length == 0
			? string.Empty
			: $"{ToolkitDistribution.PluginName} is already up to date for: {string.Join(", ", upToDate)}.";

		if (succeeded.Length == 0) {
			return upToDate.Length == 0 ? $"No agents were {PastVerb(kind)}." : upToDateText;
		}

		string succeededText =
			$"{Capitalize(PastVerb(kind))} {ToolkitDistribution.PluginName} for: {string.Join(", ", succeeded)}.";
		return upToDate.Length == 0 ? succeededText : $"{succeededText} {upToDateText}";
	}

	private static string[] AgentIds(IReadOnlyList<AgentOutcome> outcomes, AgentOutcomeStatus status) =>
		outcomes.Where(outcome => outcome.Status == status).Select(outcome => outcome.AgentId).ToArray();

	private static string Verb(SkillOperationKind kind) => kind switch {
		SkillOperationKind.Install => "install",
		SkillOperationKind.Update => "update",
		SkillOperationKind.Delete => "delete",
		_ => "process"
	};

	private static string PastVerb(SkillOperationKind kind) => kind switch {
		SkillOperationKind.Install => "installed",
		SkillOperationKind.Update => "updated",
		SkillOperationKind.Delete => "removed",
		_ => "processed"
	};

	private static string Capitalize(string value) =>
		string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
