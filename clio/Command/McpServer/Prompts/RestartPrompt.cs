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
	[McpServerPrompt(Name = "RestartByEnvironmentName"), Description("Prompt to restart Creatio by environment name")]
	public static string PromptByEnvironmentName(
		[Required] 
		[Description("The name of the environment to restart")]
		string environmentName) =>
		$"Use clio mcp server `restart` command to restart Creatio identified by environment name `{environmentName}`.";

	/// <summary>
	/// Builds a prompt that restarts Creatio by explicit credentials.
	/// </summary>
	[McpServerPrompt(Name = "RestartByCredentials"), Description("Prompt to restart Creatio by explicit credentials")]
	public static string PromptByCredentials(
		[Description("Creatio instance url")] [Required] string url,
		[Description("Creatio user name")] [Required] string userName,
		[Description("Creatio user password")] [Required] string password,
		[Description("Whether the target environment runs on .NET Core")] [Required] bool isNetCore) =>
		$"""
		 Use clio mcp server `restart` command to restart Creatio at `{url}` with user `{userName}`,
		 password `{(string.IsNullOrWhiteSpace(password) ? "<not provided>" : "<provided>")}`, and
		 `isNetCore` set to `{isNetCore}`.
		 """;
}
