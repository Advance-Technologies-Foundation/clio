namespace Clio.Common.Skills;

/// <summary>
/// Default <see cref="ICursorRuleWriter"/> — a C# port of the toolkit installer's
/// <c>render_cursor_rule</c> / <c>render_load_order</c>.
/// </summary>
public sealed class CursorRuleWriter(IFileSystem fileSystem) : ICursorRuleWriter {
	private readonly IFileSystem _fileSystem = fileSystem;

	/// <inheritdoc />
	public void WriteOrchestratorRule(string ruleFilePath, string installedPluginDir, string mcpConfigPath) {
		string directory = _fileSystem.GetDirectoryInfo(ruleFilePath).Parent?.FullName;
		if (!string.IsNullOrEmpty(directory)) {
			_fileSystem.CreateDirectoryIfNotExists(directory);
		}

		_fileSystem.WriteAllTextToFile(ruleFilePath, RenderRule(installedPluginDir, mcpConfigPath));
	}

	private string RenderRule(string pluginRoot, string mcpConfigPath) =>
		"---\n" +
		"description: Use when creating Creatio app Business Plans, " +
		"technical implementation handoffs, or applying the approved plan through clio MCP.\n" +
		"alwaysApply: false\n" +
		"---\n" +
		"\n" +
		"# Creatio App Orchestrator\n" +
		"\n" +
		"Entrypoint for the Creatio AI App Development Toolkit (CAADT) workflow.\n" +
		"\n" +
		$"Toolkit repository is installed at: `{pluginRoot}`\n" +
		"\n" +
		"## Load Order\n" +
		"\n" +
		RenderLoadOrder(pluginRoot) +
		"\n" +
		"## Core Rules\n" +
		"\n" +
		"- Keep the visible planning artifact in the BA-style Business Plan format defined by `AGENTS.md`.\n" +
		"- Resolve executable clio MCP tool contracts through `get-tool-contract`; do not invent payload shapes.\n" +
		$"- The `clio` MCP server is registered in `{mcpConfigPath}`.\n";

	private string RenderLoadOrder(string pluginRoot) =>
		$"1. Read `{RepoFile(pluginRoot, "AGENTS.md")}` for the active orchestration contract.\n" +
		$"2. Read `{RepoFile(pluginRoot, "context", "INDEX.md")}` to choose the smallest relevant reference set.\n" +
		$"3. For environment setup, read `{RepoFile(pluginRoot, "runbooks", "01-environment-setup.md")}`.\n" +
		$"4. For requirements gathering, read `{RepoFile(pluginRoot, "runbooks", "02-requirements-gathering.md")}`.\n" +
		$"5. For executable helper behavior, use `{RepoFile(pluginRoot, "runtime", "scripts", "mcp_client.py")}` " +
		$"and `{RepoFile(pluginRoot, "runtime", "scripts", "workflow_validators.py")}`.\n";

	private string RepoFile(string root, params string[] segments) {
		string[] parts = new string[segments.Length + 1];
		parts[0] = root;
		segments.CopyTo(parts, 1);
		return _fileSystem.Combine(parts);
	}
}
