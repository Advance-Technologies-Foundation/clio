using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Command.McpServer.Tools;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for the generate-process-model MCP tool.
/// </summary>
[McpServerPromptType, Description("Prompts for generating ATF process models from Creatio processes")]
public static class GenerateProcessModelPrompt {

	/// <summary>
	/// Builds a prompt that directs the agent to generate a process model through MCP.
	/// </summary>
	[McpServerPrompt(Name = GenerateProcessModelTool.GenerateProcessModelToolName),
		Description("Prompt to generate a process model")]
	public static string GenerateProcessModel(
		[Required]
		[Description("Process code")]
		string code,
		[Required]
		[Description("Registered clio environment name")]
		string environmentName,
		[Description("Optional destination folder or explicit .cs file path for the generated file")]
		string? destinationPath = null,
		[Description("Optional namespace for the generated class")]
		string? @namespace = null,
		[Description("Optional culture used to resolve localized descriptions")]
		string? culture = null) =>
		$"""
		 Use clio mcp server `{GenerateProcessModelTool.GenerateProcessModelToolName}` to generate a C# process model
		 for process `{code}` from environment `{environmentName}`.
		 Pass `code` and `environment-name` exactly as provided.
		 Pass `destination-path` as either a destination folder or an explicit `.cs` file path when you need a custom output location.
		 Pass `namespace` and `culture` only when you need values different from the command defaults
		 (`.`, `AtfTIDE.ProcessModels`, and `en-US` respectively).
		 """;
}
