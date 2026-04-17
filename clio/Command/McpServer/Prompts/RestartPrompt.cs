using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for restarting Creatio instances through MCP.
/// </summary>
[McpServerPromptType, Description("Prompts to restart Creatio instances")]
public static class RestartPrompt {

	/// <summary>
	/// Builds a prompt that restarts Creatio by a registered environment name.
	/// </summary>
	[McpServerPrompt(Name = "restart-by-environment-name"), Description("Prompt to restart Creatio by environment name")]
	public static string PromptByEnvironmentName(
		[Required] 
		[Description("The name of the environment to restart")]
		string environmentName) =>
		$"""
		 Restart the Creatio application pool for environment `{environmentName}` using the
		 `restart-by-environment-name` tool. A restart is typically needed after deploying
		 packages, compiling configuration, or changing system settings. The application
		 will be temporarily unavailable during restart (usually 10–30 seconds).
		 """;

	/// <summary>
	/// Builds a prompt that restarts Creatio by explicit credentials.
	/// </summary>
	[McpServerPrompt(Name = "restart-by-credentials"), Description("Prompt to restart Creatio by explicit credentials")]
	public static string PromptByCredentials(
		[Description("Creatio instance url")] [Required] string url,
		[Description("Creatio user name")] [Required] string userName,
		[Description("Creatio user password")] [Required] string password,
		[Description("Whether the target environment runs on .NET Core")] [Required] bool isNetCore) =>
		$"""
		 Restart the Creatio application pool at `{url}` using direct credentials via
		 the `restart-by-credentials` tool. Use this variant only when the environment
		 is not registered in clio. Prefer `restart-by-environment-name` when possible.
		 The application will be temporarily unavailable during restart (usually 10–30 seconds).
		 """;
}
