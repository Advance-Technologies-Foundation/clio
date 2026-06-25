using System;
using System.Collections.Generic;

namespace Clio.Common.Skills;

/// <summary>
/// Single managed-agent entry in the global manifest.
/// </summary>
public sealed class ManagedAgentEntry {
	/// <summary>
	/// Gets or sets the source the toolkit was installed from (marketplace URL or local path).
	/// </summary>
	public string Source { get; set; }

	/// <summary>
	/// Gets or sets the UTC timestamp of the first install for this agent.
	/// </summary>
	public DateTimeOffset InstalledAtUtc { get; set; }

	/// <summary>
	/// Gets or sets the UTC timestamp of the most recent install or update for this agent.
	/// </summary>
	public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>
/// Lightweight clio-owned record of which coding agents the toolkit skill is
/// installed for, persisted at <c>~/.clio/managed-skills.json</c> (decision O3).
/// </summary>
/// <remarks>
/// Does not track MCP-entry provenance: per decision O1 the shared <c>clio</c>
/// MCP entry is never removed on delete, so its provenance is irrelevant.
/// </remarks>
public sealed class ManagedSkillsManifest {
	/// <summary>
	/// Gets or sets the per-agent entries keyed by agent id.
	/// </summary>
	public Dictionary<string, ManagedAgentEntry> Agents { get; set; } =
		new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Records a successful install or update for an agent (per-agent upsert that
	/// leaves other agents' entries untouched, so <c>--target</c> runs are safe).
	/// </summary>
	/// <param name="agentId">Agent id.</param>
	/// <param name="source">Install source.</param>
	/// <param name="nowUtc">Current UTC timestamp.</param>
	public void Upsert(string agentId, string source, DateTimeOffset nowUtc) {
		Agents ??= new Dictionary<string, ManagedAgentEntry>(StringComparer.OrdinalIgnoreCase);
		if (Agents.TryGetValue(agentId, out ManagedAgentEntry existing)) {
			existing.Source = source;
			existing.UpdatedAtUtc = nowUtc;
			return;
		}

		Agents[agentId] = new ManagedAgentEntry {
			Source = source,
			InstalledAtUtc = nowUtc,
			UpdatedAtUtc = nowUtc
		};
	}

	/// <summary>
	/// Removes an agent's entry. No-op when the agent is not present.
	/// </summary>
	/// <param name="agentId">Agent id.</param>
	public void Remove(string agentId) => Agents?.Remove(agentId);
}
