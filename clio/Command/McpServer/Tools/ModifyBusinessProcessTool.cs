using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that edits an existing business process on a Creatio environment by applying a list of operations.
/// </summary>
public class ModifyBusinessProcessTool(
	ModifyBusinessProcessCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<ModifyBusinessProcessOptions>(command, logger, commandResolver) {

	internal const string ModifyBusinessProcessToolName = "modify-business-process";

	/// <summary>
	/// Applies an inline JSON operations array to an existing process (identified by name or uid).
	/// </summary>
	/// <param name="environmentName">Registered clio environment name.</param>
	/// <param name="processName">Process code (schema Name) to edit. Provide this or <paramref name="processUid"/>.</param>
	/// <param name="processUid">Process schema UId to edit. Provide this or <paramref name="processName"/>.</param>
	/// <param name="operations">Inline JSON operations array.</param>
	/// <returns>The command execution result with the edited schema identity in the log output.</returns>
	[McpServerTool(Name = ModifyBusinessProcessToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		 OpenWorld = false),
	 Description("Edit an EXISTING business process on a Creatio environment by applying an ordered JSON array of "
		 + "operations. Identify the process by name (schema code) or uid. Each operation is an object with an "
		 + "'op': addElement (with an 'element' descriptor: id, type, caption, userTaskName?, signal?), "
		 + "removeElement (with 'elementId' = the element's local id or UId), addFlow / removeFlow (with 'source' "
		 + "and 'target' element ids). Operations apply in order; any failure aborts the edit (nothing is saved). "
		 + "Use describe-process to inspect the current elements/ids first. May remove elements — destructive.")]
	public CommandExecutionResult ModifyBusinessProcess(
		[Description("Target Environment name")] [Required] string environmentName,
		[Description("Process code (schema Name) to edit; provide this or processUid")] string processName,
		[Description("Process schema UId to edit; provide this or processName")] string processUid,
		[Description("Inline JSON operations array, e.g. [{\"op\":\"removeElement\",\"elementId\":\"StartEvent1\"}]")]
		[Required] string operations
	) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			return CommandExecutionResult.FromError("environment-name is required and cannot be empty.");
		}

		if (string.IsNullOrWhiteSpace(processName) && string.IsNullOrWhiteSpace(processUid)) {
			return CommandExecutionResult.FromError("one of processName or processUid is required.");
		}

		if (string.IsNullOrWhiteSpace(operations)) {
			return CommandExecutionResult.FromError("operations is required and cannot be empty.");
		}

		ModifyBusinessProcessOptions options = new() {
			Environment = environmentName,
			ProcessName = processName ?? string.Empty,
			ProcessUid = processUid ?? string.Empty,
			OperationsJson = operations
		};
		return InternalExecute<ModifyBusinessProcessCommand>(options);
	}
}
