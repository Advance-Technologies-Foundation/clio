using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Command.McpServer.Tools;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for the add-item-model MCP tool.
/// </summary>
[McpServerPromptType, Description("Prompts for generating all C# models from a Creatio environment")]
public static class AddItemModelPrompt {

	/// <summary>
	/// Builds a prompt that directs the agent to generate all models through MCP.
	/// </summary>
	[McpServerPrompt(Name = AddItemModelTool.AddItemModelToolName), Description("Prompt to generate all C# models")]
	public static string AddItemModel(
		[Required]
		[Description("C# namespace for generated models")]
		string @namespace,
		[Required]
		[Description("Absolute local folder for generated files")]
		string folder,
		[Required]
		[Description("Registered clio environment name")]
		string environmentName) =>
		$"""
		 Use clio mcp server `{AddItemModelTool.AddItemModelToolName}` to generate all C# models from
		 Creatio environment `{environmentName}` into folder `{folder}` with namespace `{@namespace}`.
		 Pass `namespace`, `folder`, and `environment-name` exactly as provided.
		 The `folder` value must be an existing local absolute directory.
		 """;
}
