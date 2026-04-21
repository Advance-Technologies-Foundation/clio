using System.ComponentModel;
using Clio.Command.McpServer.Tools;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for empty IIS port discovery.
/// </summary>
[McpServerPromptType, Description("Prompts for discovering an empty IIS deployment port")]
public static class FindEmptyIisPortPrompt
{
	/// <summary>
	/// Builds a prompt that directs the agent to find a safe local IIS port before deployment.
	/// </summary>
	[McpServerPrompt(Name = FindEmptyIisPortTool.FindEmptyIisPortToolName),
	 Description("Prompt to discover an empty IIS port for local Creatio deployment")]
	public static string Prompt() =>
		$"""
		 Before deploying Creatio locally via IIS, follow this sequence:
		 1. Run `{AssertInfrastructureTool.AssertInfrastructureToolName}` to check infrastructure health and identify failing areas.
		 2. Run `{ShowPassingInfrastructureTool.ShowPassingInfrastructureToolName}` to select passing database and Redis targets.
		 3. Run `{FindEmptyIisPortTool.FindEmptyIisPortToolName}` to find an available port in range {FindEmptyIisPortTool.RangeStart}–{FindEmptyIisPortTool.RangeEnd}.
		 4. Pass the returned port as `sitePort` to `{InstallerCommandTool.DeployCreatioToolName}`.
		 This workflow ensures no port conflicts and validates all prerequisites before deployment.
		 """;
}
