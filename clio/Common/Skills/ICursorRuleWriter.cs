namespace Clio.Common.Skills;

/// <summary>
/// Writes the Cursor orchestrator rule (<c>creatio-app-orchestrator.mdc</c>).
/// </summary>
public interface ICursorRuleWriter {
	/// <summary>
	/// Renders and writes the orchestrator <c>.mdc</c> rule.
	/// </summary>
	/// <param name="ruleFilePath">Absolute path of the <c>.mdc</c> file to write.</param>
	/// <param name="installedPluginDir">Directory the toolkit plugin was copied into.</param>
	/// <param name="mcpConfigPath">Path to the Cursor <c>mcp.json</c> the rule references.</param>
	void WriteOrchestratorRule(string ruleFilePath, string installedPluginDir, string mcpConfigPath);
}
