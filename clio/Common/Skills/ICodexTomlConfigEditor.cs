namespace Clio.Common.Skills;

/// <summary>
/// Surgically edits Codex's <c>~/.codex/config.toml</c> for toolkit install/delete.
/// </summary>
/// <remarks>
/// Edits are line-oriented and non-destructive: only the targeted table blocks are
/// added or removed; comments, key order, unrelated tables, and the user's own
/// <c>[mcp_servers.*]</c> entries are preserved byte-for-byte. This ports the
/// table-block logic from the toolkit's <c>install.py</c> rather than using a
/// round-tripping TOML parser, which would reflow the whole document (PRD A-04).
/// </remarks>
public interface ICodexTomlConfigEditor {
	/// <summary>
	/// Appends an <c>[mcp_servers.clio]</c> block when one is not already present.
	/// </summary>
	/// <param name="configPath">Path to <c>config.toml</c>.</param>
	void MergeClioMcpServer(string configPath);

	/// <summary>
	/// Removes a top-level <c>[marketplaces.&lt;name&gt;]</c> block, if present.
	/// </summary>
	void RemoveMarketplaceSection(string configPath, string marketplaceName);

	/// <summary>
	/// Removes a top-level <c>[plugins."&lt;plugin&gt;@&lt;marketplace&gt;"]</c> block, if present.
	/// </summary>
	void RemovePluginSection(string configPath, string pluginName, string marketplaceName);

	/// <summary>
	/// Removes any <c>[[skills.config]]</c> block whose body declares <c>name = "&lt;skillName&gt;"</c>.
	/// </summary>
	void RemoveSkillConfigOverride(string configPath, string skillName);
}
