using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for command help lookups through MCP resources.
/// </summary>
[McpServerPromptType, Description("Prompt to resolve command help resources")]
public static class LookupHelpPrompt {

	/// <summary>
	/// Builds a prompt that directs the agent to the command help resource.
	/// </summary>
	[McpServerPrompt(Name = "Lookup up help"), Description("Prompt to open a command help resource")]
	public static string PromptByEnvironmentName(
		[Required] 
		[Description("Command name to lookup, by name or alias")]
		string commandName) =>
		$"Use `docs://help/command/{commandName}` resource to look up help for command `{commandName}`.";

}
