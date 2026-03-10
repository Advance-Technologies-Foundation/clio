using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Command.McpServer.Tools;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for linking repository packages into Creatio package directories.
/// </summary>
[McpServerPromptType, Description("Prompts for linking repository packages into Creatio environment package directories")]
public static class LinkFromRepositoryPrompt {

	/// <summary>
	/// Builds a prompt for linking repository packages by registered environment name.
	/// </summary>
	[McpServerPrompt(Name = LinkFromRepositoryTool.LinkFromRepositoryByEnvironmentToolName),
	 Description("Prompt to link repository packages by registered environment name")]
	public static string LinkFromRepositoryByEnvironment(
		[Required]
		[Description("Registered clio environment name")]
		string environmentName,
		[Required]
		[Description("Path to the package repository folder")]
		string repoPath,
		[Required]
		[Description("Packages to link")]
		string packages) =>
		$"""
		 Use clio mcp server `{LinkFromRepositoryTool.LinkFromRepositoryByEnvironmentToolName}` to link repository package content
		 into the Creatio package directory for registered environment `{environmentName}`.
		 Use repository path `{repoPath}` and package selector `{packages}` exactly as provided.
		 """;

	/// <summary>
	/// Builds a prompt for linking repository packages by explicit environment package path.
	/// </summary>
	[McpServerPrompt(Name = LinkFromRepositoryTool.LinkFromRepositoryByEnvPackagePathToolName),
	 Description("Prompt to link repository packages by explicit environment package path")]
	public static string LinkFromRepositoryByEnvPackagePath(
		[Required]
		[Description("Path to the target Creatio environment package directory")]
		string envPkgPath,
		[Required]
		[Description("Path to the package repository folder")]
		string repoPath,
		[Required]
		[Description("Packages to link")]
		string packages) =>
		$"""
		 Use clio mcp server `{LinkFromRepositoryTool.LinkFromRepositoryByEnvPackagePathToolName}` to link repository package content
		 into the Creatio package directory at `{envPkgPath}`.
		 Use repository path `{repoPath}` and package selector `{packages}` exactly as provided.
		 """;
}
