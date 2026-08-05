using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
///     MCP surface for <see cref="ReloadWorkplacesCommand" />: publishes navigation changes to signed-in users so a
///     new or changed workplace appears without a re-login.
/// </summary>
public class ReloadWorkplacesTool(
	ReloadWorkplacesCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<ReloadWorkplacesOptions>(command, logger, commandResolver) {

	internal const string ToolName = "reload-workplaces";

	/// <summary>
	///     Reloads the platform navigation caches on the target environment.
	/// </summary>
	/// <param name="environmentName">Registered environment name.</param>
	/// <returns>The command execution result; a failure names the reason so the caller can fall back to re-login.</returns>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
	 Description(
		 "Publishes navigation changes to users who are ALREADY signed in, so a new or changed workplace appears "
		 + "after a page refresh instead of a re-login. Navigation is cached per session, so call this as the LAST "
		 + "step of any navigation change — workplace, section placement, role grant, or home page — then tell the "
		 + "user to refresh. Requires cliogate. Read get-guidance name=workplaces for the recipes it completes.")]
	public CommandExecutionResult ReloadWorkplaces(
		[Description("Target Environment name")] [Required] string environmentName
	){
		if (string.IsNullOrWhiteSpace(environmentName)) {
			return CommandExecutionResult.FromValidationError("environment-name is required and cannot be empty.");
		}
		ReloadWorkplacesOptions options = new() {
			Environment = environmentName
		};
		return InternalExecute<ReloadWorkplacesCommand>(options);
	}

}
