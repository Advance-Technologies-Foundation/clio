using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Command.McpServer.Tools;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for the install-application MCP tool.
/// </summary>
[McpServerPromptType, Description("Prompts for installing application packages into Creatio")]
public static class InstallApplicationPrompt {

	/// <summary>
	/// Builds a prompt that directs the agent to install an application package through MCP.
	/// </summary>
	[McpServerPrompt(Name = InstallApplicationTool.InstallApplicationToolName),
		Description("Prompt to install an application package")]
	public static string InstallApplication(
		[Required]
		[Description("Application package path or name")]
		string name,
		[Required]
		[Description("Registered clio environment name")]
		string environmentName,
		[Description("Optional install report path")]
		string? reportPath = null,
		[Description("Optional compilation-check flag")]
		bool? checkCompilationErrors = null) =>
		$"""
		 Use clio mcp server `{InstallApplicationTool.InstallApplicationToolName}` to install application package
		 `{name}` into Creatio environment `{environmentName}`.
		 Pass `name` and `environment-name` exactly as provided.
		 Pass `report-path` only when you need the command to write an install report file.
		 Pass `check-compilation-errors` only when you want installation to stop on detected compilation errors.
		 """;
}
