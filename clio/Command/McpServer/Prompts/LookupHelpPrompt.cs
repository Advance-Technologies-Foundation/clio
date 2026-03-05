using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;


[McpServerPromptType, Description("Prompt to restart Creatio instance")]
public class LookupHelpPrompt{

	[McpServerPrompt(Name = "Lookup up help"), Description("Propmt to help article")]
	public static string PromptByEnvironmentName(
		[Required] 
		[Description("Command name to lookup, by name or alias")]
		string commandName) =>
		$"Use `docs://help/command/{commandName}` resource to lookup help for command: {commandName}.";

}
