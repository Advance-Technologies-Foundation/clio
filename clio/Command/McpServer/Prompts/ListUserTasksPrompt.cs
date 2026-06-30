using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for listing the user-facing user tasks of a Creatio environment through MCP.
/// </summary>
[McpServerPromptType, Description("Prompts to list the user tasks (process designer palette) of an environment")]
[FeatureToggle("process-designer")]
public static class ListUserTasksPrompt {

	/// <summary>
	/// Builds a prompt that lists the user tasks available on a registered environment.
	/// </summary>
	[McpServerPrompt(Name = "list-user-tasks"),
	 Description("Prompt to list the user tasks available on a Creatio environment")]
	public static string PromptByEnvironmentName(
		[Required] [Description("The name of the environment to read the user task palette from")]
		string environmentName) =>
		$"""
		 List the user-facing user tasks available on Creatio environment `{environmentName}` using the
		 `list-user-tasks` tool. The result is the same palette the visual process designer shows (including
		 custom user tasks); each entry has a name and a UId. Use one of the returned names as the
		 `userTaskName` of a `userTask` element when building a process with `create-business-process`.
		 """;
}
