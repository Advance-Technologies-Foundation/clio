using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for package storage synchronization commands.
/// </summary>
[McpServerPromptType, Description("Prompts to load packages between database and file system")]
public static class LoadPackagesPrompt {

	/// <summary>
	/// Builds a prompt for loading packages from the database into the file system.
	/// </summary>
	/// <param name="environmentName">Target environment name.</param>
	/// <returns>Prompt text for the MCP agent.</returns>
	[McpServerPrompt(Name = "pkg-to-file-system"), Description("Prompt to load packages from database to file system")]
	public static string LoadPackagesToFileSystem(
		[Required]
		[Description("Target environment name")]
		string environmentName) =>
		$"""
		 Use clio mcp server `pkg-to-file-system` tool to load packages from the database
		 into the file system for Creatio environment `{environmentName}`.
		 This operation requires the target environment to have file design mode enabled.
		 """;

	/// <summary>
	/// Builds a prompt for loading packages from the file system into the database.
	/// </summary>
	/// <param name="environmentName">Target environment name.</param>
	/// <returns>Prompt text for the MCP agent.</returns>
	[McpServerPrompt(Name = "pkg-to-db"), Description("Prompt to load packages from file system to database")]
	public static string LoadPackagesToDb(
		[Required]
		[Description("Target environment name")]
		string environmentName) =>
		$"""
		 Use clio mcp server `pkg-to-db` tool to load packages from the file system
		 into the database for Creatio environment `{environmentName}`.
		 This operation requires the target environment to have file design mode enabled.
		 """;
}
