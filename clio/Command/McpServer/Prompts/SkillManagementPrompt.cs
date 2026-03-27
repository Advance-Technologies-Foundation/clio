using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Command.McpServer.Tools;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for workspace-local skill management MCP tools.
/// </summary>
[McpServerPromptType, Description("Prompts for installing, updating, and deleting workspace-local skills")]
public static class SkillManagementPrompt {
	/// <summary>
	/// Builds guidance for installing workspace-local skills through MCP.
	/// </summary>
	[McpServerPrompt(Name = "install-skills-guidance"), Description("Prompt to install workspace-local skills")]
	public static string InstallSkills(
		[Required] [Description("Absolute local workspace path")] string workspacePath,
		[Description("Optional skill name")] string skillName = null,
		[Description("Optional repository path or git URL")] string repo = null) =>
		$"""
		 Use clio MCP tool `{InstallSkillsTool.ToolName}` with `workspacePath` set to `{workspacePath}`.
		 {(string.IsNullOrWhiteSpace(skillName)
			 ? "Omit `skillName` to install all available skills."
			 : $"Pass `skillName` as `{skillName}` to install a single skill.")}
		 {(string.IsNullOrWhiteSpace(repo)
			 ? "Omit `repo` to use the default bootstrap skills repository."
			 : $"Pass `repo` as `{repo}` to use that local repository path or git URL.")}
		 Do not overwrite unmanaged skill folders.
		 """;

	/// <summary>
	/// Builds guidance for updating managed workspace-local skills through MCP.
	/// </summary>
	[McpServerPrompt(Name = "update-skill-guidance"), Description("Prompt to update managed workspace-local skills")]
	public static string UpdateSkill(
		[Required] [Description("Absolute local workspace path")] string workspacePath,
		[Description("Optional managed skill name")] string skillName = null,
		[Description("Optional repository path or git URL")] string repo = null) =>
		$"""
		 Use clio MCP tool `{UpdateSkillTool.ToolName}` with `workspacePath` set to `{workspacePath}`.
		 {(string.IsNullOrWhiteSpace(skillName)
			 ? "Omit `skillName` to update all managed skills registered for the selected repository."
			 : $"Pass `skillName` as `{skillName}` to update one managed skill.")}
		 {(string.IsNullOrWhiteSpace(repo)
			 ? "Omit `repo` to use the default bootstrap skills repository."
			 : $"Pass `repo` as `{repo}` to select the repository whose commit hash should be checked.")}
		 Updates only apply to clio-managed skills and only when the repository HEAD hash changed.
		 """;

	/// <summary>
	/// Builds guidance for deleting a managed workspace-local skill through MCP.
	/// </summary>
	[McpServerPrompt(Name = "delete-skill-guidance"), Description("Prompt to delete a managed workspace-local skill")]
	public static string DeleteSkill(
		[Required] [Description("Absolute local workspace path")] string workspacePath,
		[Required] [Description("Managed skill name")] string skillName) =>
		$"""
		 Use clio MCP tool `{DeleteSkillTool.ToolName}` with `workspacePath` set to `{workspacePath}` and `skillName` set to `{skillName}`.
		 Delete only managed skills recorded by clio.
		 """;
}
