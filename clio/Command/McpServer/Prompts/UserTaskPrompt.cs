using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for user task MCP tools.
/// </summary>
[McpServerPromptType, Description("Prompts for creating and modifying workspace user tasks")]
public class UserTaskPrompt {

	/// <summary>
	/// Builds a prompt that directs the agent to create a user task through MCP.
	/// </summary>
	[McpServerPrompt(Name = "create-user-task"), Description("Prompt to create a workspace user task")]
	public static string CreateUserTask(
		[Required]
		[Description("User task schema code")]
		string code,
		[Required]
		[Description("Workspace package name")]
		string packageName,
		[Required]
		[Description("User task title")]
		string title,
		[Required]
		[Description("Creatio environment name")]
		string environmentName,
		[Required]
		[Description("Absolute path to the local workspace")]
		string workspacePath,
		[Description("Optional user task description")]
		string description = null) =>
		$"""
		 Use clio mcp server `create-user-task` tool to create user task `{code}` in workspace package
		 `{packageName}` for environment `{environmentName}` using workspace path `{workspacePath}`.
		 Set the title to `{title}` and description to `{description ?? "<not provided>"}`.
		 Include initial parameters only when they were explicitly requested, and include parameter directions
		 when the requested task contract depends on them.
		 """;

	/// <summary>
	/// Builds a prompt that directs the agent to modify parameters on an existing user task.
	/// </summary>
	[McpServerPrompt(Name = "modify-user-task-parameters"),
		Description("Prompt to add or remove parameters on an existing workspace user task")]
	public static string ModifyUserTaskParameters(
		[Required]
		[Description("Existing user task schema name")]
		string userTaskName,
		[Required]
		[Description("Creatio environment name")]
		string environmentName,
		[Required]
		[Description("Absolute path to the local workspace")]
		string workspacePath) =>
		$"""
		 Use clio mcp server `modify-user-task-parameters` tool to add and/or remove parameters on
		 existing user task `{userTaskName}` in environment `{environmentName}` using workspace path
		 `{workspacePath}`.
		 Include parameter directions on added parameters when they were explicitly requested, and use
		 direction updates for existing parameters when the request is to change input/output/variable
		 behavior without recreating the parameter.
		 Because the tool can remove parameters from the schema, confirm the exact parameter changes
		 before using it destructively.
		 """;
}
