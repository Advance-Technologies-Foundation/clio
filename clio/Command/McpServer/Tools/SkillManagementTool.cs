using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>install-skills</c> command.
/// </summary>
[McpServerToolType]
public sealed class InstallSkillsTool(InstallSkillsCommand command, ILogger logger)
	: BaseTool<InstallSkillsOptions>(command, logger) {
	internal const string ToolName = "install-adac";

	/// <summary>
	/// Installs the Creatio toolkit skill globally for detected coding agents.
	/// </summary>
	[Description("Installs the Creatio AI App Development Toolkit skill for all detected coding agents "
		+ "(claude, codex, cursor, copilot) using each agent's native plugin mechanism, or one agent via target")]
	public CommandExecutionResult InstallSkills(
		[Description("Install-skills parameters")] [Required] InstallSkillsRunArgs args) {
		InstallSkillsOptions options = new() {
			Target = args.Target,
			Repo = args.Repo
		};
		return InternalExecute(options);
	}
}

/// <summary>
/// MCP tool surface for the <c>update-skill</c> command.
/// </summary>
[McpServerToolType]
public sealed class UpdateSkillTool(UpdateSkillCommand command, ILogger logger)
	: BaseTool<UpdateSkillOptions>(command, logger) {
	internal const string ToolName = "update-adac";

	/// <summary>
	/// Updates the Creatio toolkit skill for detected coding agents (Claude included).
	/// </summary>
	[Description("Updates the Creatio AI App Development Toolkit skill for all detected coding agents "
		+ "(claude, codex, cursor, copilot), or one agent via target")]
	public CommandExecutionResult UpdateSkill(
		[Description("Update-skill parameters")] [Required] UpdateSkillRunArgs args) {
		UpdateSkillOptions options = new() {
			Target = args.Target,
			Repo = args.Repo
		};
		return InternalExecute(options);
	}
}

/// <summary>
/// MCP tool surface for the <c>delete-skill</c> command.
/// </summary>
[McpServerToolType]
public sealed class DeleteSkillTool(DeleteSkillCommand command, ILogger logger)
	: BaseTool<DeleteSkillOptions>(command, logger) {
	internal const string ToolName = "delete-adac";

	/// <summary>
	/// Uninstalls the Creatio toolkit skill from detected coding agents (idempotent).
	/// </summary>
	[Description("Uninstalls the Creatio AI App Development Toolkit skill from all detected coding agents, "
		+ "or one agent via target. Leaves the shared clio MCP server entry in place")]
	public CommandExecutionResult DeleteSkill(
		[Description("Delete-skill parameters")] [Required] DeleteSkillRunArgs args) {
		DeleteSkillOptions options = new() {
			Target = args.Target
		};
		return InternalExecute(options);
	}
}

/// <summary>
/// MCP arguments for the <c>install-skills</c> tool.
/// </summary>
public sealed record InstallSkillsRunArgs(
	[property: JsonPropertyName("target")]
	[property: Description("Optional agent to limit to: claude | codex | cursor | copilot. Default: all detected")]
	string Target = null,

	[property: JsonPropertyName("repo")]
	[property: Description("Optional source override. Marketplace git URL for claude/codex/copilot; "
		+ "local path or git URL for cursor. Defaults to the public toolkit marketplace")]
	string Repo = null
) : ClioRunArgs;

/// <summary>
/// MCP arguments for the <c>update-skill</c> tool.
/// </summary>
public sealed record UpdateSkillRunArgs(
	[property: JsonPropertyName("target")]
	[property: Description("Optional agent to limit to: claude | codex | cursor | copilot. Default: all detected")]
	string Target = null,

	[property: JsonPropertyName("repo")]
	[property: Description("Optional source override. Marketplace git URL for claude/codex/copilot; "
		+ "local path or git URL for cursor. Defaults to the public toolkit marketplace")]
	string Repo = null
) : ClioRunArgs;

/// <summary>
/// MCP arguments for the <c>delete-skill</c> tool.
/// </summary>
public sealed record DeleteSkillRunArgs(
	[property: JsonPropertyName("target")]
	[property: Description("Optional agent to limit to: claude | codex | cursor | copilot. Default: all detected")]
	string Target = null
) : ClioRunArgs;
