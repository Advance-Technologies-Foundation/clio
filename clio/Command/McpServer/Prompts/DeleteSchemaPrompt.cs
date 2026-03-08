using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for deleting workspace schemas through MCP.
/// </summary>
[McpServerPromptType, Description("Prompt to delete a schema from a workspace package")]
public static class DeleteSchemaPrompt {

	/// <summary>
	/// Builds a prompt that directs the agent to delete a schema owned by the current workspace.
	/// </summary>
	[McpServerPrompt(Name = "delete-schema"), Description("Prompt to delete a workspace schema")]
	public static string Prompt(
		[Required]
		[Description("Schema name to delete")]
		string schemaName,
		[Required]
		[Description("Creatio environment name")]
		string environmentName,
		[Required]
		[Description("Absolute path to the local workspace")]
		string workspacePath) =>
		$"""
		 Use clio mcp server `delete-schema` destructive tool to delete schema `{schemaName}`
		 from Creatio environment `{environmentName}` using workspace path `{workspacePath}`.
		 Only proceed when the schema belongs to one of the packages in that workspace.
		 """;
}
