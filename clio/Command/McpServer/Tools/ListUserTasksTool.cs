using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;
using System.Collections.Generic;
using System.Text.Json;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that lists the user-facing user tasks (process designer palette) of a Creatio environment.
/// </summary>
public class ListUserTasksTool(
	ListUserTasksCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<ListUserTasksOptions>(command, logger, commandResolver) {

	internal const string ListUserTasksToolName = "list-user-tasks";

	/// <summary>The canonical field list echoed back when an unknown argument key is refused (ENG-98566).</summary>
	internal const string ValidArgsHint = "Valid: environment-name.";

	/// <summary>Refusal for a call whose whole argument object is absent (ENG-98566, Sonar S2259).</summary>
	/// <remarks>
	/// Stays inline rather than moving into the shared helper: it has to run before any field can be read,
	/// and that is what lets the analyser prove no field read is reached with null (csharpsquid:S2259). See
	/// <see cref="McpToolArgumentSupport.BuildUnknownArgumentError"/>.
	/// </remarks>
	internal const string NullArgsError = "args is required: the call carried no argument object. " + ValidArgsHint;

	/// <summary>
	/// Lists the user-facing user tasks available on the specified environment.
	/// </summary>
	/// <param name="args">The tool arguments; see <see cref="ListUserTasksArgs"/>.</param>
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
		 + "on a userTask element when building a process with create-business-process. Three exceptions, where a "
		 + "DEDICATED element type carries configuration the generic userTask route cannot: for "
		 + "EmailTemplateUserTask (Send email) prefer type sendEmail with its email block, and for "
		 + "ApprovalUserTask (Approval) prefer type approval with its approval block — an Approval element built "
		 + "as a generic userTask has no approval object, no record under approval and nobody assigned to approve "
		 + "it; and for ChangeAdminRightsUserTask (Change access rights) prefer type changeAccessRights with its "
		 + "accessRights block plus the element record filter - a generic userTask naming that schema IS accepted "
		 + "and carries both, but does not survive a deployed CrtProcessBuilder that predates the element, which "
		 + "discards the block and still answers success. If an environment rejects either (\"Element type 'sendEmail' is not supported yet\"), its deployed "
		 + "CrtProcessBuilder predates that element type: fall back to a generic userTask named after the schema, "
		 + "which older packages do build. Requires the "
		 + "ProcessDesignService (CrtProcessBuilder) package on the target environment. Install it with install-process-builder.")]
	public CommandExecutionResult ListUserTasks(
		[Description("list-user-tasks parameters")] [Required] ListUserTasksArgs args
	) {
		if (args is null) {
			return CommandExecutionResult.FromValidationError(NullArgsError);
		}

		// The only unknown-key defence this tool has; the helper's docs say why. ENG-98566 review finding 8:
		// with a single declared argument, an unbound environmentName left EnvironmentName null. The finding
		// said the call then ran against the DEFAULT registered environment; measured, it does not - the
		// resolver throws instead. The real cost was a generic resolution error in place of a named argument.
		string argumentError = McpToolArgumentSupport.BuildUnknownArgumentError(
			args.ExtensionData, ValidArgsHint);
		if (!string.IsNullOrWhiteSpace(argumentError)) {
			return CommandExecutionResult.FromValidationError(argumentError);
		}

		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
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
	string EnvironmentName) {

	/// <summary>
	/// Overflow bag for unknown JSON fields (ENG-98566). Inspected by the tool; a bag that is never read is
	/// the failure mode, not the fix.
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
