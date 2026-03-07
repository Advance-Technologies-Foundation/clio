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
		 Include initial parameters only when they were explicitly requested, include parameter directions
		 when the requested task contract depends on them, and when a parameter type is `Lookup`
		 provide its `lookup` entity schema name or schema UId.
		 When a parameter type is `Serializable list of composite values`, include its child items
		 under `items`.
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
		 Include parameter directions on added parameters when they were explicitly requested, use
		 direction updates for existing parameters when the request is to change input/output/variable
		 behavior without recreating the parameter, and provide `lookup` entity schema names or schema
		 UIds on added parameters whose type is `Lookup`.
		 Use `add-parameters[].items` when creating a new `Serializable list of composite values`
		 parameter with child items, and use `add-parameter-items` when adding child items to an
		 existing composite list parameter.
		 Because the tool can remove parameters from the schema, confirm the exact parameter changes
		 before using it destructively.
		 """;
}
