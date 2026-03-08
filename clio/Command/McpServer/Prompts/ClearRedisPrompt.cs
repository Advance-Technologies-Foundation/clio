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
	[McpServerPrompt(Name = "ClearRedisByEnvironmentName"),
	 Description("Prompt to clear Redis by environment name")]
	public static string PromptByEnvironmentName(
		[Required] [Description("The name of the environment to clear Redis for")]
		string environmentName) =>
		$"""
		 Use clio mcp server `clear-redis-db` command to empty the Redis database used by
		 the Creatio environment `{environmentName}`.
		 """;

	/// <summary>
	/// Builds a prompt that clears Redis by explicit environment credentials.
	/// </summary>
	[McpServerPrompt(Name = "ClearRedisByCredentials"),
	 Description("Prompt to clear Redis by explicit credentials")]
	public static string PromptByCredentials(
		[Description("Creatio instance url")] [Required]
		string url,
		[Description("Creatio user name")] [Required]
		string userName,
		[Description("Creatio user password")] [Required]
		string password,
		[Description("Whether the target environment runs on .NET Core")] [Required]
		bool isNetCore) =>
		$"""
		 Use clio mcp server `clear-redis-db` command to empty the Redis database used by
		 the Creatio instance at `{url}` with user `{userName}`, password
		 `{(string.IsNullOrWhiteSpace(password) ? "<not provided>" : "<provided>")}`, and `isNetCore`
		 set to `{isNetCore}`.
		 """;
}
