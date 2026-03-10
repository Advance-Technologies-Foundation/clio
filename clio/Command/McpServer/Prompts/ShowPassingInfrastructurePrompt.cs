using System.ComponentModel;
using Clio.Command.McpServer.Tools;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for passing-only infrastructure discovery.
/// </summary>
[McpServerPromptType, Description("Prompts for discovering passing infrastructure choices before Creatio deployment")]
public static class ShowPassingInfrastructurePrompt
{
	/// <summary>
	/// Builds a prompt that directs the agent to run the passing-only deployment selection tool.
	/// </summary>
	[McpServerPrompt(Name = ShowPassingInfrastructureTool.ShowPassingInfrastructureToolName),
	 Description("Prompt to show passing infrastructure choices for deployment selection")]
	public static string Prompt() =>
		$"""
		 First run `{AssertInfrastructureTool.AssertInfrastructureToolName}` to inspect the full infrastructure state,
		 including failing areas. Then run `{ShowPassingInfrastructureTool.ShowPassingInfrastructureToolName}` to get
		 only the passing database and Redis choices plus the recommended deployment bundle for `deploy-creatio`.
		 Use the recommended deployment or the per-engine recommendation that matches the target archive.
		 """;
}
