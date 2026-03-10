using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for creating empty local clio workspaces through MCP.
/// </summary>
[McpServerPromptType, Description("Prompts for creating empty local clio workspaces")]
public static class CreateWorkspacePrompt {

	/// <summary>
	/// Builds a prompt that directs the agent to create an empty workspace through MCP.
	/// </summary>
	[McpServerPrompt(Name = "create-workspace-guidance"), Description("Prompt to create an empty local workspace")]
	public static string CreateWorkspace(
		[Required]
		[Description("Relative workspace folder name")]
		string workspaceName,
		[Description("Optional absolute directory where the workspace should be created")]
		string directory = null) =>
		$"""
		 Use clio mcp server `create-workspace` to create an empty local workspace named
		 `{workspaceName}`.
		 {(string.IsNullOrWhiteSpace(directory)
			 ? "Pass only `workspaceName`; do not pass `directory` so clio falls back to the configured `workspaces-root` setting."
			 : $"Pass `workspaceName` and `directory` as `{directory}` so clio creates the workspace under that absolute path.")}
		 Do not use environment-backed restore/download modes for this tool.
		 """;
}
