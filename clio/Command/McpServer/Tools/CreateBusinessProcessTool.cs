using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that builds a business process on a Creatio environment from a declarative JSON descriptor.
/// </summary>
public class CreateBusinessProcessTool(
	CreateBusinessProcessCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<CreateBusinessProcessOptions>(command, logger, commandResolver) {

	internal const string CreateBusinessProcessToolName = "create-business-process";

	/// <summary>
	/// Builds a business process from an inline JSON descriptor on the specified environment.
	/// </summary>
	/// <param name="environmentName">Registered clio environment name.</param>
	/// <param name="descriptor">Inline JSON process descriptor.</param>
	/// <param name="packageName">Optional package name that overrides the descriptor's <c>packageName</c>.</param>
	/// <returns>The command execution result with the created schema identity in the log output.</returns>
	[McpServerTool(Name = CreateBusinessProcessToolName, ReadOnly = false, Destructive = false, Idempotent = false,
		 OpenWorld = false),
	 Description("Build a business process on a Creatio environment from a declarative JSON descriptor. The "
		 + "descriptor is an object with: name (schema code), caption, packageName, elements[] "
		 + "({id, type:startEvent|signalStart|endEvent|userTask (aliases readData/performTask), caption, "
		 + "userTaskName?, signal?}), flows[] ({source, target} of element ids), parameters[] "
		 + "({name, type, direction, caption}), and mappings[] ({elementId, elementParameter, and one of "
		 + "processParameter|expression|value}). To run the process when a record is saved/added/changed, use a "
		 + "signalStart element with signal:{entity:<EntityName>, on:added|modified|deleted} (one event) instead "
		 + "of a page save handler. Use list-user-tasks to discover valid userTaskName values.")]
	public CommandExecutionResult CreateBusinessProcess(
		[Description("Target Environment name")] [Required] string environmentName,
		[Description("Inline JSON process descriptor (name, caption, packageName, elements[], flows[], "
			+ "parameters[], mappings[])")] [Required] string descriptor,
		[Description("Optional package name that overrides the descriptor's packageName")] string packageName = null
	) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			return CommandExecutionResult.FromError("environment-name is required and cannot be empty.");
		}

		if (string.IsNullOrWhiteSpace(descriptor)) {
			return CommandExecutionResult.FromError("descriptor is required and cannot be empty.");
		}

		CreateBusinessProcessOptions options = new() {
			Environment = environmentName,
			DescriptorJson = descriptor,
			PackageName = packageName ?? string.Empty
		};
		return InternalExecute<CreateBusinessProcessCommand>(options);
	}
}
