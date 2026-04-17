using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for clearing a Creatio Redis database through MCP.
/// </summary>
[McpServerPromptType, Description("Prompts to clear a Creatio Redis database")]
public static class ClearRedisPrompt {

	/// <summary>
	/// Builds a prompt that clears Redis by a registered environment name.
	/// </summary>
	[McpServerPrompt(Name = "clear-redis-by-environment-name"),
	 Description("Prompt to clear Redis by environment name")]
	public static string PromptByEnvironmentName(
		[Required] [Description("The name of the environment to clear Redis for")]
		string environmentName) =>
		$"""
		 Clear the Redis cache for Creatio environment `{environmentName}` using the
		 `clear-redis-db-by-environment` tool. This flushes all cached data and forces
		 Creatio to rebuild its cache on next request. Typically needed after configuration
		 changes, package installations, or when troubleshooting stale-data issues.
		 Always confirm the target environment with the user before proceeding.
		 """;

	/// <summary>
	/// Builds a prompt that clears Redis by explicit environment credentials.
	/// </summary>
	[McpServerPrompt(Name = "clear-redis-by-credentials"),
	 Description("Prompt to clear Redis by explicit credentials")]
	public static string PromptByCredentials(
		[Description("Creatio instance url")] [Required]
		string url,
		[Description("Creatio user name")] [Required]
		string userName,
		[Description("Creatio user password")] [Required]
		string password,
		[Description("Whether the target environment runs on .NET Core. Optional, defaults to false.")]
		bool isNetCore = false) =>
		$"""
		 Clear the Redis cache for the Creatio instance at `{url}` using direct credentials
		 via the `clear-redis-db-by-credentials` tool. Use this variant only when the
		 environment is not registered in clio. Prefer `clear-redis-by-environment-name`
		 when the environment is registered.
		 Credentials: user `{userName}`, password {(string.IsNullOrWhiteSpace(password) ? "not provided" : "provided")}, isNetCore={isNetCore}.
		 """;
}
