namespace Clio.Common.Skills;

/// <summary>
/// Edits the JSON config files touched by the multi-agent installer
/// (Claude <c>settings.json</c>, Cursor <c>mcp.json</c>, the
/// <c>~/.agents/plugins/marketplace.json</c> catalog), preserving unrelated keys.
/// </summary>
public interface IJsonConfigEditor {
	/// <summary>
	/// Sets <c>extraKnownMarketplaces.&lt;name&gt;.autoUpdate = true</c> in Claude
	/// settings, dropping a stale <c>directory</c> source if present.
	/// </summary>
	void EnableMarketplaceAutoUpdate(string settingsPath, string marketplaceName);

	/// <summary>
	/// Removes the <c>extraKnownMarketplaces.&lt;name&gt;</c> entry from Claude settings (delete path).
	/// </summary>
	void RemoveMarketplaceFromSettings(string settingsPath, string marketplaceName);

	/// <summary>
	/// Merges the shared <c>clio</c> MCP server into an <c>mcp.json</c>-style file,
	/// skipping when a <c>clio</c> server already exists.
	/// </summary>
	void MergeClioMcpServer(string mcpJsonPath);

	/// <summary>
	/// Strips the toolkit plugin entry from a personal marketplace catalog, deleting
	/// the file when it becomes an empty installer-owned catalog.
	/// </summary>
	void RemovePersonalMarketplacePluginEntry(string catalogPath, string marketplaceName, string pluginName);
}
