using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that lists the user-facing user tasks (process designer palette) of a Creatio environment.
/// </summary>
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
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = ListUserTasksToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		 OpenWorld = false),
	 Description("List the user-facing user tasks available on a Creatio environment (the process designer "
		 + "palette), including custom ones. Returns each task's name and UId; pass a name as a userTaskName "
		 + "on a userTask element when building a process with create-business-process. Exception: for "
		 + "ChangeAdminRightsUserTask (Change access rights) prefer the dedicated type changeAccessRights with "
		 + "its accessRights block plus the element record filter. A generic userTask naming ChangeAdminRightsUserTask IS "
		 + "accepted by the server and carries both, so it is a valid fallback; what it does NOT survive is a deployed "
		 + "CrtProcessBuilder that predates the element, which discards the block and still answers success. For "
		 + "EmailTemplateUserTask (Send email) prefer the dedicated type sendEmail with its email block over "
		 + "the generic userTask route. If an environment rejects it (\"Element type 'sendEmail' is not supported "
		 + "yet\"), its deployed CrtProcessBuilder predates that element type: fall back to a generic userTask "
		 + "named EmailTemplateUserTask, which older packages do build. Requires the "
		 + "ProcessDesignService (CrtProcessBuilder) package on the target environment. Install it with install-process-builder.")]
	public CommandExecutionResult ListUserTasks(
		[Description("list-user-tasks parameters")] [Required] ListUserTasksArgs args
	) {
		if (string.IsNullOrWhiteSpace(args?.EnvironmentName)) {
			return CommandExecutionResult.FromError("environment-name is required and cannot be empty.");
		}

		ListUserTasksOptions options = new() {
			Environment = args.EnvironmentName
		};
		return InternalExecute<ListUserTasksCommand>(options);
	}
}

/// <summary>
/// MCP arguments for the <c>list-user-tasks</c> tool (kebab-case wire keys, repo convention).
/// </summary>
public sealed record ListUserTasksArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string EnvironmentName);
