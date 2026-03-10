using System.ComponentModel;
using Clio.Command.McpServer.Tools;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for the full infrastructure assert MCP tool.
/// </summary>
[McpServerPromptType, Description("Prompts for running the full infrastructure assertion sweep")]
public static class AssertInfrastructurePrompt
{
	/// <summary>
	/// Builds a prompt that directs the agent to inspect the full infrastructure inventory through MCP.
	/// </summary>
	[McpServerPrompt(Name = AssertInfrastructureTool.AssertInfrastructureToolName),
	 Description("Prompt to run the full infrastructure assertion sweep")]
	public static string Prompt() =>
		$"""
		 Use clio mcp server `{AssertInfrastructureTool.AssertInfrastructureToolName}` to inspect the full infrastructure inventory.
		 The tool returns Kubernetes, local, and filesystem assertion results plus normalized database candidates that can be used
		 to choose which database should be used for Creatio deployment.
		 """;
}
