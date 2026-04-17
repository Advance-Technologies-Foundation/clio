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
	[McpServerPrompt(Name = "lookup-help"), Description("Prompt to open a command help resource")]
	public static string LookupCommand(
		[Required] 
		[Description("Command name to lookup, by name or alias (e.g. 'restart-web-app', 'compile-configuration')")]
		string commandName) =>
		$"""
		 Look up detailed usage for the `{commandName}` command by reading the
		 `docs://help/command/{commandName}` resource. This returns the CLI help text
		 including all available options, examples, and notes. Both MCP tool names
		 and CLI command names are accepted — aliases are resolved automatically.
		 """;

}
