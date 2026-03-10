using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for download-configuration MCP tools.
/// </summary>
[McpServerPromptType, Description("Prompts for downloading Creatio configuration into a local workspace")]
public static class DownloadConfigurationPrompt {

	/// <summary>
	/// Builds a prompt that downloads configuration from a registered environment into a workspace.
	/// </summary>
	[McpServerPrompt(Name = "download-configuration-by-environment"),
	 Description("Prompt to download configuration from a registered environment")]
	public static string DownloadConfigurationByEnvironment(
		[Required] [Description("Registered clio environment name")]
		string environmentName,
		[Required] [Description("Absolute path to the local workspace")]
		string workspacePath) =>
		$"""
		 Use clio mcp server `{Tools.DownloadConfigurationTool.DownloadConfigurationByEnvironmentToolName}` to
		 download Creatio configuration from registered environment `{environmentName}` into the workspace at
		 `{workspacePath}`.
		 Pass `workspace-path` exactly as provided so the tool writes into that workspace's `.application`
		 folder.
		 """;

	/// <summary>
	/// Builds a prompt that downloads configuration from a local build path into a workspace.
	/// </summary>
	[McpServerPrompt(Name = "download-configuration-by-build"),
	 Description("Prompt to download configuration from a Creatio build path")]
	public static string DownloadConfigurationByBuild(
		[Required] [Description("Absolute path to the Creatio zip file or extracted directory")]
		string buildPath,
		[Required] [Description("Absolute path to the local workspace")]
		string workspacePath) =>
		$"""
		 Use clio mcp server `{Tools.DownloadConfigurationTool.DownloadConfigurationByBuildToolName}` to
		 download Creatio configuration from build path `{buildPath}` into the workspace at `{workspacePath}`.
		 Pass `workspace-path` exactly as provided so the tool writes into that workspace's `.application`
		 folder.
		 """;
}
