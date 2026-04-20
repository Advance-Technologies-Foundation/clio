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
		 Export package source code from the Creatio database to the file system for
		 environment `{environmentName}` using the `pkg-to-file-system` tool.
		 This is typically the first step when starting local development — it downloads
		 package schemas and resources so they can be edited in an IDE.
		 **Prerequisite:** The environment must have file-system development (FSD) mode enabled.
		 After editing files locally, use `pkg-to-db` to push changes back.
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
		 Import package source code from the file system back into the Creatio database
		 for environment `{environmentName}` using the `pkg-to-db` tool.
		 Use this after editing package files locally to apply changes to the running
		 Creatio instance. Consider compiling configuration and restarting the
		 environment afterward to ensure changes take full effect.
		 **Prerequisite:** The environment must have file-system development (FSD) mode enabled.
		 """;
}
