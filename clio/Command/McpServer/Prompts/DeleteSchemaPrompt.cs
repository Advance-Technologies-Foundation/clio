using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

[McpServerPromptType, Description("Prompt to delete a schema from a workspace package")]
public class DeleteSchemaPrompt {

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
