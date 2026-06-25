using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that lists the user-facing user tasks (process designer palette) of a Creatio environment.
/// </summary>
[FeatureToggle("process-designer")]
public class ListUserTasksTool(
	ListUserTasksCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<ListUserTasksOptions>(command, logger, commandResolver) {

	internal const string ListUserTasksToolName = "list-user-tasks";

	/// <summary>
	/// Lists the user-facing user tasks available on the specified environment.
	/// </summary>
	/// <param name="environmentName">Registered clio environment name.</param>
	/// <returns>The command execution result; the log output lists each task as <c>name\tuid</c>.</returns>
	[McpServerTool(Name = ListUserTasksToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		 OpenWorld = false),
	 Description("List the user-facing user tasks available on a Creatio environment (the process designer "
		 + "palette), including custom ones. Returns each task's name and UId; pass a name as a userTaskName "
		 + "on a userTask element when building a process with create-business-process.")]
	public CommandExecutionResult ListUserTasks(
		[Description("Target Environment name")] [Required] string environmentName
	) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			return CommandExecutionResult.FromError("environment-name is required and cannot be empty.");
		}

		ListUserTasksOptions options = new() {
			Environment = environmentName
		};
		return InternalExecute<ListUserTasksCommand>(options);
	}
}
