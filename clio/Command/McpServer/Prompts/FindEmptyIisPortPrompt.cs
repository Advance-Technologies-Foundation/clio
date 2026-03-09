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
	 Description("Prompt to discover an empty IIS port between 40000 and 42000")]
	public static string Prompt() =>
		$"""
		 When preparing a local IIS deployment, first run `{AssertInfrastructureTool.AssertInfrastructureToolName}`
		 to inspect failing areas, then run `{ShowPassingInfrastructureTool.ShowPassingInfrastructureToolName}` to
		 select passing database and Redis targets, and then run `{FindEmptyIisPortTool.FindEmptyIisPortToolName}`
		 to get a safe `sitePort` between {FindEmptyIisPortTool.RangeStart} and {FindEmptyIisPortTool.RangeEnd}
		 before calling `{InstallerCommandTool.DeployCreatioToolName}`.
		 """;
}
