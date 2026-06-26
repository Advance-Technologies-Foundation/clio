using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools.ProcessDesigner;

/// <summary>
/// MCP tool that edits an existing business process on a Creatio environment by applying a list of operations.
/// </summary>
[FeatureToggle("process-designer")]
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
		 + "and 'target' element ids), addParameter (with a 'parameter': name, type e.g. Text/Integer/Guid, "
		 + "direction?, caption?, or referenceSchema for a Lookup to an object e.g. City), addMapping (with a "
		 + "'mapping': elementId, elementParameter, and exactly one of processParameter | value | expression). "
		 + "Operations apply in order; any failure aborts the edit (nothing is saved). "
		 + "Use describe-business-process to inspect the current elements/ids first. May remove elements — destructive.")]
	public CommandExecutionResult ModifyBusinessProcess(
		[Description("modify-business-process parameters")] [Required] ModifyBusinessProcessArgs args
	) {
		if (string.IsNullOrWhiteSpace(args?.EnvironmentName)) {
			return CommandExecutionResult.FromError("environment-name is required and cannot be empty.");
		}

		bool hasName = !string.IsNullOrWhiteSpace(args.ProcessName);
		bool hasUid = !string.IsNullOrWhiteSpace(args.ProcessUid);
		if (hasName == hasUid) {
			return CommandExecutionResult.FromError(hasName
				? "Provide only one of process-name or process-uid, not both."
				: "one of process-name or process-uid is required.");
		}

		if (string.IsNullOrWhiteSpace(args.Operations)) {
			return CommandExecutionResult.FromError("operations is required and cannot be empty.");
		}

		ModifyBusinessProcessOptions options = new() {
			Environment = args.EnvironmentName,
			ProcessName = args.ProcessName ?? string.Empty,
			ProcessUid = args.ProcessUid ?? string.Empty,
			OperationsJson = args.Operations
		};
		return InternalExecute<ModifyBusinessProcessCommand>(options);
	}
}

/// <summary>
/// MCP arguments for the <c>modify-business-process</c> tool (kebab-case wire keys, repo convention).
/// Provide exactly one of <c>process-name</c> / <c>process-uid</c>.
/// </summary>
public sealed record ModifyBusinessProcessArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("operations")]
	[property: Description("Inline JSON operations array, e.g. [{\"op\":\"removeElement\",\"elementId\":\"StartEvent1\"}].")]
	[property: Required]
	string Operations,

	[property: JsonPropertyName("process-name")]
	[property: Description("Process code (schema Name) to edit; provide exactly one of process-name or process-uid.")]
	string ProcessName,

	[property: JsonPropertyName("process-uid")]
	[property: Description("Process schema UId to edit; provide exactly one of process-name or process-uid.")]
	string ProcessUid);
