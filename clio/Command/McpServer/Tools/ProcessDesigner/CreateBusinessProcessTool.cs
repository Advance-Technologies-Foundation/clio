using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools.ProcessDesigner;

/// <summary>
/// MCP tool that builds a business process on a Creatio environment from a declarative JSON descriptor.
/// </summary>
[FeatureToggle("process-designer")]
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
		 + "({name (the element handle/local code), type:startEvent|signalStart|endEvent|userTask "
		 + "(aliases readData/performTask; readData and other data-operation tasks land UNCONFIGURED — their "
		 + "source object/filters/columns cannot be set yet, tell the user), caption, userTaskName?, signal?, "
		 + "filter?}), flows[] ({source, target} of "
		 + "element names), parameters[] ({name, type (a supported scalar or Lookup — other types rejected), "
		 + "direction, caption, description?, value? (a literal constant default — not a formula)}; or "
		 + "typeFromElement + typeFromElementParameter to copy an element parameter's exact type), "
		 + "and mappings[] (bind a target to a source — target is {elementName, "
		 + "elementParameter} (an element input) or {targetProcessParameter} (a process parameter, e.g. expose an "
		 + "element output as a process output); source is exactly one of {sourceElement, sourceElementParameter} "
		 + "(another element's output), processParameter, value, or expression; parameter-to-parameter mappings "
		 + "require compatible types). To run the process when a record "
		 + "is saved/added/changed, use a "
		 + "signalStart element with signal:{entity:<EntityName>, on:added|modified|deleted} (one event) instead "
		 + "of a page save handler. To fire that trigger only for matching records, add filter:{object, logicalOperation:and|or, conditions:[{column (entity column name, may be a lookup dot-path like Account.Code), comparison:equal|notEqual|greater|less|contains|isNull|..., and one of value|macro (a relative-date/system macro like Today/NextNDays + macroArgument), plus optional datePart (Year|Month|Day|... to compare a date's part against an integer value)}], groups?} to the signalStart element. A signalStart filter's right side must be a constant/macro/datePart — NOT a process/element parameter (the signal is evaluated before the process instance exists; the server rejects a parameter reference here). The server serializes the platform filter; never hand-write filter JSON. Use list-user-tasks to discover valid userTaskName values.")]
	public CommandExecutionResult CreateBusinessProcess(
		[Description("create-business-process parameters")] [Required] CreateBusinessProcessArgs args
	) {
		if (string.IsNullOrWhiteSpace(args?.EnvironmentName)) {
			return CommandExecutionResult.FromError("environment-name is required and cannot be empty.");
		}

		if (string.IsNullOrWhiteSpace(args.Descriptor)) {
			return CommandExecutionResult.FromError("descriptor is required and cannot be empty.");
		}

		CreateBusinessProcessOptions options = new() {
			Environment = args.EnvironmentName,
			DescriptorJson = args.Descriptor,
			PackageName = args.PackageName ?? string.Empty
		};
		return InternalExecute<CreateBusinessProcessCommand>(options);
	}
}

/// <summary>
/// MCP arguments for the <c>create-business-process</c> tool (kebab-case wire keys, repo convention).
/// </summary>
public sealed record CreateBusinessProcessArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("descriptor")]
	[property: Description("Inline JSON process descriptor (name, caption, packageName, elements[], flows[], "
		+ "parameters[], mappings[]).")]
	[property: Required]
	string Descriptor,

	[property: JsonPropertyName("package-name")]
	[property: Description("Optional package name that overrides the descriptor's packageName.")]
	string PackageName);
