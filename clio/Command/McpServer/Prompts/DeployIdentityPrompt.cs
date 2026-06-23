using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for the <c>deploy-identity</c> MCP tool.
/// </summary>
[McpServerPromptType, Description("Prompts for deploying IdentityService through MCP")]
[FeatureToggle("deploy-identity")]
public static class DeployIdentityPrompt
{
	/// <summary>
	/// Builds a prompt that directs the agent to run the recommended IdentityService deployment flow.
	/// </summary>
	[McpServerPrompt(Name = DeployIdentityTool.DeployIdentityToolName),
	 Description("Prompt to deploy IdentityService with EnvironmentPath archive discovery and automatic IIS port selection")]
	public static string Prompt(
		[Required]
		[Description("Registered clio environment name")]
		string environmentName,
		[Description("Optional path to IdentityService.zip or a Creatio bundle")]
		string zipFile = null,
		[Description("Optional port where IdentityService will listen")]
		int? identitySitePort = null) =>
		$"""
		 Call `{DeployIdentityTool.DeployIdentityToolName}` for the registered environment `{environmentName}`.
		 Verify the target Creatio environment has a local EnvironmentPath and
		 default Supervisor credentials still work, because the command connects Creatio through the
		 platform REST/sys-settings path and, by default, creates a fresh clio OAuth app bound to the
		 existing Supervisor user before storing generated clio OAuth credentials in local clio settings.
		 Use noApp only when asked to deploy and connect IdentityService without creating an OAuth app;
		 noApp skips client_credentials verification and local clio credential persistence. Use createTechUser
		 only when asked to create a new technical user instead of binding the fresh app to an existing user.
		 If zipFile is omitted, the command finds IdentityService.zip under EnvironmentPath.
		 If identitySitePort is omitted, the command selects the first free IIS port in range 40001-40100.
		 Explicit zipFile `{zipFile ?? "<auto>"}` and identitySitePort `{identitySitePort?.ToString() ?? "<auto>"}` override those defaults.
		 """;
}
