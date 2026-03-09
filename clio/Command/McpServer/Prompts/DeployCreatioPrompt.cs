using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Command.McpServer.Tools;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for the <c>deploy-creatio</c> MCP tool.
/// </summary>
[McpServerPromptType, Description("Prompts for deploying Creatio through MCP")]
public static class DeployCreatioPrompt
{
	/// <summary>
	/// Builds a prompt that directs the agent to run the recommended deployment preflight sequence.
	/// </summary>
	[McpServerPrompt(Name = InstallerCommandTool.DeployCreatioToolName),
	 Description("Prompt to deploy Creatio after infrastructure preflight checks")]
	public static string Prompt(
		[Required]
		[Description("Creatio instance name")]
		string siteName,
		[Required]
		[Description("Path to the Creatio archive file")]
		string zipFile,
		[Required]
		[Description("Port where Creatio will be deployed")]
		int sitePort) =>
		$"""
		 Before calling `{InstallerCommandTool.DeployCreatioToolName}`, first run `assert-infrastructure`
		 to review all passing and failing infrastructure, then run `show-passing-infrastructure` to get
		 deployable choices and the recommended `dbServerName` and `redisServerName` values.
		 If you are deploying locally to IIS, run `{FindEmptyIisPortTool.FindEmptyIisPortToolName}` to pick
		 a safe `sitePort` between {FindEmptyIisPortTool.RangeStart} and {FindEmptyIisPortTool.RangeEnd}.
		 After that preflight, call `{InstallerCommandTool.DeployCreatioToolName}` with site name `{siteName}`,
		 zip file `{zipFile}`, site port `{sitePort}`, and the selected or recommended server-name arguments.
		 """;
}
