using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Command.McpServer.Tools;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for workspace synchronization MCP tools.
/// </summary>
[McpServerPromptType, Description("Prompts for pushing and restoring clio workspaces")]
public static class WorkspaceSyncPrompt {

	/// <summary>
	/// Builds a prompt that directs the agent to push a local workspace to Creatio.
	/// </summary>
	[McpServerPrompt(Name = PushWorkspaceTool.PushWorkspaceToolName), Description("Prompt to push a local workspace")]
	public static string PushWorkspace(
		[Required]
		[Description("Creatio environment name")]
		string environmentName,
		[Required]
		[Description("Absolute path to the local workspace")]
		string workspacePath) =>
		$"""
		 Use clio mcp server `{PushWorkspaceTool.PushWorkspaceToolName}` tool to push the workspace at
		 `{workspacePath}` to Creatio environment `{environmentName}`.
		 Pass `workspace-path` exactly as provided and use `environment-name` `{environmentName}`.
		 """;

	/// <summary>
	/// Builds a prompt that directs the agent to restore a local workspace from Creatio.
	/// </summary>
	[McpServerPrompt(Name = RestoreWorkspaceTool.RestoreWorkspaceToolName), Description("Prompt to restore a local workspace")]
	public static string RestoreWorkspace(
		[Required]
		[Description("Creatio environment name")]
		string environmentName,
		[Required]
		[Description("Absolute path to the local workspace")]
		string workspacePath) =>
		$"""
		 Use clio mcp server `{RestoreWorkspaceTool.RestoreWorkspaceToolName}` tool to restore the workspace at
		 `{workspacePath}` from Creatio environment `{environmentName}`.
		 Pass `workspace-path` exactly as provided and use `environment-name` `{environmentName}`.
		 """;
}
