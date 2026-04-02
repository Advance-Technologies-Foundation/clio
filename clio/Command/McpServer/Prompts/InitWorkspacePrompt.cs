using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for initializing local clio workspaces through MCP.
/// </summary>
[McpServerPromptType]
public static class InitWorkspacePrompt {

	/// <summary>
	/// Builds a prompt that directs the agent to initialize the current directory as a workspace through MCP.
	/// </summary>
	[McpServerPrompt(Name = "init-workspace-guidance")]
	public static string InitWorkspace() =>
		"""
		Use clio mcp server `init-workspace` to initialize the current directory as a local workspace.
		Choose this tool when the directory already contains files that must not be overwritten.
		Do not use `create-workspace` for this scenario because that tool is intended for creating a new workspace folder.
		""";
}
