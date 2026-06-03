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
	IManagedSkillsManifestStore manifestStore)
	: ISkillInstallService {
	private readonly IReadOnlyList<ICodingAgent> _agents = agents
		.OrderBy(agent => agent.AgentId, StringComparer.OrdinalIgnoreCase)
		.ToList();
	private readonly IManagedSkillsManifestStore _manifestStore = manifestStore;

	/// <inheritdoc />
	public SkillCommandResult Install(string target, string repo) =>
		Run(SkillOperationKind.Install, target, repo);

	/// <inheritdoc />
	public SkillCommandResult Update(string target, string repo) =>
		Run(SkillOperationKind.Update, target, repo);

	/// <inheritdoc />
	public SkillCommandResult Delete(string target) =>
		Run(SkillOperationKind.Delete, target, repositoryOverride: null);

	private SkillCommandResult Run(SkillOperationKind kind, string target, string repositoryOverride) {
		string normalizedTarget = target?.Trim();
		if (!string.IsNullOrEmpty(normalizedTarget)
			&& !_agents.Any(agent => string.Equals(agent.AgentId, normalizedTarget, StringComparison.OrdinalIgnoreCase))) {
			string valid = string.Join(", ", _agents.Select(agent => agent.AgentId));
			return Invalid($"Unknown target '{target}'. Valid agents: {valid}.");
		}

		if (!RepositorySource.TryValidate(repositoryOverride, out string repositoryError)) {
			return Invalid(repositoryError);
		}

		List<ICodingAgent> selected = string.IsNullOrEmpty(normalizedTarget)
			? _agents.ToList()
			: _agents.Where(agent => string.Equals(agent.AgentId, normalizedTarget, StringComparison.OrdinalIgnoreCase)).ToList();

		AgentOperationContext context = new(kind, repositoryOverride);
		ManagedSkillsManifest manifest = _manifestStore.Read();
		DateTimeOffset now = DateTimeOffset.UtcNow;
		List<AgentOutcome> outcomes = [];
		bool manifestChanged = false;

		foreach (ICodingAgent agent in selected) {
			if (!agent.Detect()) {
				outcomes.Add(AgentOutcome.Skipped(agent.AgentId,
					$"{agent.DisplayName} is not installed; nothing to do."));
				continue;
			}

			AgentOutcome outcome = Dispatch(agent, kind, context);
			outcomes.Add(outcome);

			if (outcome.Status != AgentOutcomeStatus.Succeeded) {
				continue;
			}

			if (kind == SkillOperationKind.Delete) {
				manifest.Remove(agent.AgentId);
			}
			else {
				manifest.Upsert(agent.AgentId, context.MarketplaceUrl, now);
			}

			manifestChanged = true;
		}

		if (manifestChanged) {
			_manifestStore.Save(manifest);
		}

		int exitCode = outcomes.Any(outcome => outcome.Status == AgentOutcomeStatus.Failed) ? 1 : 0;
		return new SkillCommandResult(exitCode, outcomes, BuildSummary(kind, outcomes));
	}

	private static SkillCommandResult Invalid(string message) =>
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
		string[] succeeded = outcomes
			.Where(outcome => outcome.Status == AgentOutcomeStatus.Succeeded)
			.Select(outcome => outcome.AgentId)
			.ToArray();

		if (succeeded.Length == 0) {
			return $"No agents were {PastVerb(kind)}.";
		}

		return $"{Capitalize(PastVerb(kind))} {ToolkitDistribution.PluginName} for: {string.Join(", ", succeeded)}.";
	}

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
