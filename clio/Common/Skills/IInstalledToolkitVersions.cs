using System.Collections.Generic;

namespace Clio.Common.Skills;

/// <summary>Reads toolkit versions from local coding-agent installations without running agent CLIs.</summary>
public interface IInstalledToolkitVersions {
	/// <summary>Returns a version or an explicit unavailable status for each supported agent.</summary>
	IReadOnlyDictionary<string, string> Read();

	/// <summary>
	/// Returns the git commit of the toolkit payload the agent itself recorded as installed, or <c>null</c>
	/// when it cannot be determined.
	/// </summary>
	/// <param name="agentId">Agent id (<c>claude</c> | <c>codex</c> | <c>cursor</c> | <c>copilot</c>).</param>
	/// <returns>
	/// A lower-case 40-character commit id, or <c>null</c> when the agent is not installed, records no
	/// commit, or its metadata is unreadable or ambiguous. Callers must treat <c>null</c> as "unknown" and
	/// update, never as "up to date".
	/// </returns>
	/// <remarks>
	/// Only Claude (<c>gitCommitSha</c> in <c>installed_plugins.json</c>) and Codex (the active cache
	/// payload, which is a git checkout) record a commit. Cursor's file copy and Copilot's install keep no
	/// git metadata, so they always return <c>null</c>.
	/// </remarks>
	string ReadInstalledCommit(string agentId);
}
