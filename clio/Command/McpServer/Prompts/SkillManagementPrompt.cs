using System.ComponentModel;
using Clio.Command.McpServer.Tools;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for the multi-agent skill management MCP tools.
/// </summary>
[McpServerPromptType, Description("Prompts for installing, updating, and deleting the Creatio toolkit skill across coding agents")]
public static class SkillManagementPrompt {
	/// <summary>
	/// Builds guidance for installing the toolkit skill through MCP.
	/// </summary>
	[McpServerPrompt(Name = "install-toolkit-guidance"), Description("Prompt to install the Creatio toolkit skill")]
	public static string InstallSkills(
		[Description("Optional agent to limit to: claude | codex | cursor | copilot")] string target = null,
		[Description("Optional source override (marketplace git URL, or path/URL for cursor)")] string repo = null) =>
		$"""
		 Use clio MCP tool `{InstallSkillsTool.ToolName}`.
		 {TargetGuidance(target, "install")}
		 {RepoGuidance(repo)}
		 Installs the whole Creatio AI App Development Toolkit bundle per agent using each agent's
		 native plugin mechanism. There is no per-skill selection.
		 """;

	/// <summary>
	/// Builds guidance for updating the toolkit skill through MCP.
	/// </summary>
	[McpServerPrompt(Name = "update-toolkit-guidance"), Description("Prompt to update the Creatio toolkit skill")]
	public static string UpdateSkill(
		[Description("Optional agent to limit to: claude | codex | cursor | copilot")] string target = null,
		[Description("Optional source override (marketplace git URL, or path/URL for cursor)")] string repo = null) =>
		$"""
		 Use clio MCP tool `{UpdateSkillTool.ToolName}`.
		 {TargetGuidance(target, "update")}
		 {RepoGuidance(repo)}
		 Updates every detected agent, including Claude (refreshed via `claude plugin update`).
		 """;

	/// <summary>
	/// Builds guidance for uninstalling the toolkit skill through MCP.
	/// </summary>
	[McpServerPrompt(Name = "delete-toolkit-guidance"), Description("Prompt to uninstall the Creatio toolkit skill")]
	public static string DeleteSkill(
		[Description("Optional agent to limit to: claude | codex | cursor | copilot")] string target = null) =>
		$"""
		 Use clio MCP tool `{DeleteSkillTool.ToolName}`.
		 {TargetGuidance(target, "uninstall from")}
		 The shared `clio` MCP server entry is intentionally left in place. Delete is idempotent —
		 an already-clean agent is reported as success.
		 """;

	private static string TargetGuidance(string target, string verb) =>
		string.IsNullOrWhiteSpace(target)
			? $"Omit `target` to {verb} all detected agents."
			: $"Pass `target` as `{target}` to {verb} only that agent.";

	private static string RepoGuidance(string repo) =>
		string.IsNullOrWhiteSpace(repo)
			? "Omit `repo` to use the default public toolkit marketplace."
			: $"Pass `repo` as `{repo}` to override the source.";
}
