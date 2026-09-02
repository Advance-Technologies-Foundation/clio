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
		[Description("Optional explicit port; omit for local IIS to use deploy-creatio-defaults.site-port-range")]
		int? sitePort = null,
		[Description("Prefer HTTPS for local IIS deployment; falls back to HTTP when no usable certificate is installed")]
		bool useHttps = false) =>
		$"""
		 Before calling `{InstallerCommandTool.DeployCreatioToolName}`, first run `assert-infrastructure`
		 to review all passing and failing infrastructure, then run `show-passing-infrastructure` to get
		 deployable choices and the recommended `dbServerName` and `redisServerName` values.
		 For local IIS, omit `sitePort` to let clio reserve the first available port from the configured
		 `deploy-creatio-defaults.site-port-range`. Run `{FindEmptyIisPortTool.FindEmptyIisPortToolName}` only
		 when you want to inspect or explicitly choose a port. The deploy command reserves and revalidates
		 the chosen port before changing the target. It also serializes deploy and uninstall operations resolving to
		 the same environment name or physical directory; separate names, ports, and target directories can deploy in parallel.
		 The deployment preserves the build database's existing forced-password-change state and does not
		 clear it automatically.
		 After that preflight, call `{InstallerCommandTool.DeployCreatioToolName}` with site name `{siteName}`,
		 zip file `{zipFile}`, site port `{sitePort?.ToString() ?? "omitted (use configured range)"}`, useHttps `{useHttps.ToString().ToLowerInvariant()}`, and the selected or recommended server-name arguments.
		 For local IIS, useHttps is opportunistic: clio uses one usable LocalMachine/My certificate matching
		 the host, or warns and continues with HTTP when none is available.
		 """;
}
