using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for the add package MCP tool.
/// </summary>
[McpServerPromptType, Description("Prompts for creating packages in a workspace or local folder")]
public static class AddPackagePrompt {

	/// <summary>
	/// Builds a prompt that directs the agent to create a package through MCP.
	/// </summary>
	[McpServerPrompt(Name = "add-package"), Description("Prompt to add a package to a workspace or folder")]
	public static string AddPackage(
		[Required]
		[Description("Package name")]
		string packageName,
		[Description("Whether to create or update app-descriptor.json")]
		bool asApp = false,
		[Description("Optional workspace root path")]
		string workspacePath = null,
		[Description("Optional environment name")]
		string environmentName = null,
		[Description("Optional build zip or extracted build path")]
		string buildZipPath = null) =>
		$"""
		 Use clio mcp server `add-package` tool to create package `{packageName}`.
		 Set `as-app` to `{asApp}`.
		 Use `workspace-path` when the package must be added inside a specific workspace root that already contains
		 `.clio/workspaceSettings.json`:
		 `{workspacePath ?? "<current MCP server working directory>"}`.
		 Set `environment-name` only when follow-up steps should use a registered environment:
		 `{environmentName ?? "<not provided>"}`.
		 Set `build-zip-path` only when follow-up steps should use a specific Creatio build source:
		 `{buildZipPath ?? "<not provided>"}`.
		 """;
}
