using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Command.McpServer.Tools;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for scope-aware skill management MCP tools.
/// </summary>
[McpServerPromptType, Description("Prompts for installing, updating, and deleting managed skills in workspace or user scope")]
public static class SkillManagementPrompt {
	/// <summary>
	/// Builds guidance for installing managed skills through MCP.
	/// </summary>
	[McpServerPrompt(Name = "install-skills-guidance"), Description("Prompt to install managed skills")]
	public static string InstallSkills(
		[Description("Skill target scope: workspace or user")] string scope = SkillScopeParser.Workspace,
		[Description("Absolute local workspace path when scope is workspace")] string workspacePath = null,
		[Description("Optional skill name")] string skillName = null,
		[Description("Optional repository path or git URL")] string repo = null) =>
		$"""
		 Use clio MCP tool `{InstallSkillsTool.ToolName}`.
		 Pass `scope` as `{scope}`.
		 {(string.Equals(scope, SkillScopeParser.User, StringComparison.OrdinalIgnoreCase)
			 ? "Omit `workspacePath` when installing into user scope."
			 : $"Pass `workspacePath` as `{workspacePath}` when installing into workspace scope.")}
		 {(string.IsNullOrWhiteSpace(skillName)
			 ? "Omit `skillName` to install all available skills."
			 : $"Pass `skillName` as `{skillName}` to install a single skill.")}
		 {(string.IsNullOrWhiteSpace(repo)
			 ? "Omit `repo` to use the default bootstrap skills repository."
			 : $"Pass `repo` as `{repo}` to use that local repository path or git URL.")}
		 Do not overwrite unmanaged skill folders.
		 """;

	/// <summary>
	/// Builds guidance for updating managed skills through MCP.
	/// </summary>
	[McpServerPrompt(Name = "update-skill-guidance"), Description("Prompt to update managed skills")]
	public static string UpdateSkill(
		[Description("Skill target scope: workspace or user")] string scope = SkillScopeParser.Workspace,
		[Description("Absolute local workspace path when scope is workspace")] string workspacePath = null,
		[Description("Optional managed skill name")] string skillName = null,
		[Description("Optional repository path or git URL")] string repo = null) =>
		$"""
		 Use clio MCP tool `{UpdateSkillTool.ToolName}`.
		 Pass `scope` as `{scope}`.
		 {(string.Equals(scope, SkillScopeParser.User, StringComparison.OrdinalIgnoreCase)
			 ? "Omit `workspacePath` when updating user-scope skills."
			 : $"Pass `workspacePath` as `{workspacePath}` when updating workspace-scope skills.")}
		 {(string.IsNullOrWhiteSpace(skillName)
			 ? "Omit `skillName` to update all managed skills registered for the selected repository."
			 : $"Pass `skillName` as `{skillName}` to update one managed skill.")}
		 {(string.IsNullOrWhiteSpace(repo)
			 ? "Omit `repo` to use the default bootstrap skills repository."
			 : $"Pass `repo` as `{repo}` to select the repository whose commit hash should be checked.")}
		 Updates only apply to clio-managed skills and only when the repository HEAD hash changed.
		 """;

	/// <summary>
	/// Builds guidance for deleting a managed skill through MCP.
	/// </summary>
	[McpServerPrompt(Name = "delete-skill-guidance"), Description("Prompt to delete a managed skill")]
	public static string DeleteSkill(
		[Required] [Description("Managed skill name")] string skillName,
		[Description("Skill target scope: workspace or user")] string scope = SkillScopeParser.Workspace,
		[Description("Absolute local workspace path when scope is workspace")] string workspacePath = null) =>
		$"""
		 Use clio MCP tool `{DeleteSkillTool.ToolName}` with `skillName` set to `{skillName}`.
		 Pass `scope` as `{scope}`.
		 {(string.Equals(scope, SkillScopeParser.User, StringComparison.OrdinalIgnoreCase)
			 ? "Omit `workspacePath` when deleting a user-scope managed skill."
			 : $"Pass `workspacePath` as `{workspacePath}` when deleting a workspace-scope managed skill.")}
		 Delete only managed skills recorded by clio.
		 """;
}
