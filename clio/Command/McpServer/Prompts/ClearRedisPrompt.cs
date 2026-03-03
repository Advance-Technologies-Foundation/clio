using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;


[McpServerPromptType, Description("Prompt to restart Creatio instance")]
public class ClearRedisPrompt{

	[McpServerPrompt(Name = "ClearRedisByEnvironmentName"),
	 Description("Propmt to restart Creatio instanceby environment name")]
	public static string PromptByEnvironmentName(
		[Required] [Description("The name of the environment to restart")]
		string environmentName) =>
		$"""
		 Use clio mcp server `clear-redis-db` command to empty/clear redis database used by 
		 Creatio instance identified by environment name: {environmentName}.";
		 """;

	[McpServerPrompt(Name = "ClearRedisByCredentials"),
	 Description("Propmt to restart Creatio instanceby providing credentials")]
	public static string PromptByCredentials(
		[Description("Creatio instance url")] [Required]
		string url,
		[Description("Creatio instance url")] [Required]
		string userName,
		[Description("Creatio instance url")] [Required]
		string password,
		[Description("Creatio instance url")] [Required]
		bool isNetCore) =>
		$"""
		 Use clio mcp server `clear-redis-db` command to empty/clear redis database used by 
		 Creatio instance with url: {url}, username: {userName}, password: {password}, isNetCore: {isNetCore}.";
		 """;
}
