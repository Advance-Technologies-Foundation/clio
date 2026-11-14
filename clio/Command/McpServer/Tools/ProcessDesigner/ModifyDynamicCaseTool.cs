using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools.ProcessDesigner;

/// <summary>
/// MCP tool that changes an existing dynamic case (DCM) in place: its stage list, stage groups, captions and colours,
/// its caption, or whether it is enabled.
/// </summary>
public class ModifyDynamicCaseTool(
	ModifyDynamicCaseCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<ModifyDynamicCaseOptions>(command, logger, commandResolver) {

	internal const string ModifyDynamicCaseToolName = "modify-dynamic-case";

	/// <summary>The canonical field list echoed back when an unknown argument key is refused.</summary>
	internal const string ValidArgsHint = "Valid: environment-name, case-name, case-uid, entity-schema-name, stages, "
		+ "caption, filter, remove-filter, enabled, confirm-running-instances, dry-run.";

	/// <summary>Refusal for a call whose whole argument object is absent.</summary>
	/// <remarks>
	/// Inline rather than in the shared helper for the reason <see cref="CreateBusinessProcessTool.NullArgsError"/>
	/// gives: it runs before any field can be read.
	/// </remarks>
	internal const string NullArgsError = "args is required: the call carried no argument object. " + ValidArgsHint;

	/// <summary>The pseudo-argument the mini-contract names when the call identifies no case.</summary>
	internal const string CaseIdentityArgument = "case-name | case-uid | entity-schema-name";

	/// <summary>Minimal valid <c>args</c> object, used by the mini-contract: a dry run that writes nothing.</summary>
	internal const string MinimalExample =
		"{\"environment-name\":\"<env>\",\"entity-schema-name\":\"UsrOrder\",\"stages\":[\"New\",\"In progress\","
		+ "\"Done\"],\"dry-run\":true}";

	/// <summary>The stage item shape named back when an item carries an undeclared key.</summary>
	internal const string StageShape =
		"{\"value\": \"<lookup value Id or exact display value>\", \"caption\"?: \"...\", \"color\"?: \"#RRGGBB\", "
		+ "\"group\"?: \"<parent stage value>\" | null, \"successful\"?: false}";

	private static readonly string[] StageKeys = [
		DynamicCaseStageArguments.ValueKey, DynamicCaseStageArguments.CaptionKey, DynamicCaseStageArguments.ColorKey,
		DynamicCaseStageArguments.GroupKey, DynamicCaseStageArguments.SuccessfulKey
	];

	/// <summary>
	/// Changes the case, or plans the change on a dry run, on the specified environment.
	/// </summary>
	/// <param name="args">The tool arguments; see <see cref="ModifyDynamicCaseArgs"/>.</param>
	/// <returns>The command execution result; the result document is in the log output.</returns>
	// Destructive: it rewrites the case schema, and a left-out stage is removed. Idempotent: the same declarative call
	// again finds nothing to change and answers unchanged. Destructive also keeps it out of the read-response deadline.
	[McpServerTool(Name = ModifyDynamicCaseToolName, ReadOnly = false, Destructive = true, Idempotent = true,
		 OpenWorld = false),
	 Description("Change an existing dynamic case (DCM) in place. Read get-guidance name=process-dynamic-case first. "
		 + "Call describe-dynamic-case, then this tool with dry-run=true, show the user the planned diff and ask "
		 + "before writing. Name the case by case-name or case-uid, or by entity-schema-name when the object has one "
		 + "case. stages is the WHOLE desired list in order, matched to the stored stages by lookup value: a stage "
		 + "left out is removed, a new value is added, and an item member left out keeps its stored value. caption "
		 + "renames the case. filter gives the case a filter (which new records start it; the object's first filter "
		 + "splits all its cases by that column) and remove-filter=true removes it; running cases continue. "
		 + "enabled=false disables the case and true enables it; send enabled alone, with no other "
		 + "change. A case that has running cases is changed only after the user agreed "
		 + "(confirm-running-instances). Every refusal answers outcome refused with a stable errorCode and writes "
		 + "nothing; the same call again answers unchanged. A case is interpreted: no compile-creatio and no restart, "
		 + "but open pages of the object need a reload (F5). Requires a CrtProcessBuilder that carries ModifyCase "
		 + "(install-process-builder, after user approval). Steps inside stages are not supported.")]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	public CommandExecutionResult ModifyDynamicCase(
		[Description("modify-dynamic-case parameters")] [Required] ModifyDynamicCaseArgs args) {
		if (args is null) {
			return CommandExecutionResult.FromValidationError(NullArgsError);
		}

		// The only unknown-key defence a long-tail tool has; McpToolArgumentSupport.BuildUnknownArgumentError says why.
		string argumentError = McpToolArgumentSupport.BuildUnknownArgumentError(args.ExtensionData, ValidArgsHint);
		if (!string.IsNullOrWhiteSpace(argumentError)) {
			return CommandExecutionResult.FromValidationError(argumentError);
		}

		List<string> missing = MissingRequired(args);
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return ToolMiniContract.Decorate(
				CommandExecutionResult.FromError("environment-name is required and cannot be empty."),
				missing, ForModifyDynamicCase);
		}
		if (missing.Count > 0) {
			return ToolMiniContract.Decorate(
				CommandExecutionResult.FromValidationError(
					"One of case-name, case-uid or entity-schema-name is required: name the case to change."),
				missing, ForModifyDynamicCase);
		}
		if (args.Caption is not null && string.IsNullOrWhiteSpace(args.Caption)) {
			return CommandExecutionResult.FromValidationError(
				"caption cannot be empty: leave it out to keep the case caption. [invalid-request]");
		}
		if (!DynamicCaseStageArguments.TryRead(args.Stages ?? default, StageKeys, StageShape,
				out List<DynamicCaseStageItem> stages, out CommandExecutionResult stagesRefusal)) {
			return stagesRefusal;
		}
		if (!DynamicCaseFilterArguments.TryRead(args.Filter ?? default, out DynamicCaseFilterArgument filter,
				out CommandExecutionResult filterRefusal)) {
			return filterRefusal;
		}
		if (filter is not null && args.RemoveFilter == true) {
			return CommandExecutionResult.FromValidationError(
				"filter and remove-filter=true contradict each other: send one of them. [invalid-request]");
		}

		ModifyDynamicCaseOptions options = new() {
			Environment = args.EnvironmentName,
			CaseName = args.CaseName?.Trim(),
			CaseUId = args.CaseUId?.Trim(),
			EntitySchemaName = args.EntitySchemaName?.Trim(),
			Stages = stages,
			Caption = args.Caption?.Trim(),
			Enabled = args.Enabled,
			Filter = filter,
			RemoveFilter = args.RemoveFilter ?? false,
			ConfirmRunningInstances = args.ConfirmRunningInstances ?? false,
			DryRun = args.DryRun ?? false
		};
		// A case is interpreted, so the deterministic note can never contradict the response; it rides success only,
		// like on every other process-designer write.
		CommandExecutionResult result = InternalExecute<ModifyDynamicCaseCommand>(options);
		return result.ExitCode != 0 ? result : result.WithCompileNotRequiredNote();
	}

	/// <summary>The mini-contract returned when a required argument is missing.</summary>
	/// <param name="missing">The required arguments the call lacked.</param>
	internal static string ForModifyDynamicCase(IReadOnlyList<string> missing) => ToolMiniContract.Build(
		ModifyDynamicCaseToolName,
		missing,
		[
			"environment-name (registered environment)",
			"case-name, case-uid or entity-schema-name (the case, or its object when the object has one case)",
			"optional: stages, caption, enabled, confirm-running-instances, dry-run (see the tool description)"
		],
		MinimalExample);

	private static List<string> MissingRequired(ModifyDynamicCaseArgs args) {
		List<string> missing = [];
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			missing.Add("environment-name");
		}
		if (string.IsNullOrWhiteSpace(args.CaseName) && string.IsNullOrWhiteSpace(args.CaseUId)
			&& string.IsNullOrWhiteSpace(args.EntitySchemaName)) {
			missing.Add(CaseIdentityArgument);
		}
		return missing;
	}
}

/// <summary>
/// MCP arguments for the <c>modify-dynamic-case</c> tool (kebab-case wire keys, repo convention).
/// </summary>
public sealed record ModifyDynamicCaseArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("case-name")]
	[property: Description("The case's schema name.")]
	string CaseName = null,

	[property: JsonPropertyName("case-uid")]
	[property: Description("The case's schema UId.")]
	string CaseUId = null,

	[property: JsonPropertyName("entity-schema-name")]
	[property: Description("The object; accepted only when it has exactly one case (else case-ambiguous with the "
		+ "list).")]
	string EntitySchemaName = null,

	// JsonElement, not a typed list: an array of stage objects, an array of strings and a string holding either are
	// all accepted, an undeclared item key is refused instead of being dropped by the binder, and an explicit
	// "group": null stays apart from an absent key. Nullable because the schema exporter serializes a parameter's
	// default value, and default(JsonElement) has none.
	[property: JsonPropertyName("stages")]
	[property: Description("Optional WHOLE desired stage list in order: an array of {\"value\": \"<lookup value Id or "
		+ "exact display value>\", \"caption\"?, \"color\"?: \"#RRGGBB\", \"group\"?, \"successful\"?} (a bare string "
		+ "is {\"value\"}). A stored stage left out is removed; a member left out keeps its stored value. group: the "
		+ "parent stage's value, a stage of the list in no group itself (one level); \"group\": null takes the stage out "
		+ "of its group. successful: false for a stage of the final group (the last top-level stage and its members) "
		+ "that ends the case unsuccessfully. caption renames the stage in the case only, never the lookup value. "
		+ "Omit to keep the stages.")]
	JsonElement? Stages = null,

	[property: JsonPropertyName("caption")]
	[property: Description("Optional new case caption; omit to keep it.")]
	string Caption = null,

	// JsonElement for the reason stages gives: an object or a string holding one, and an undeclared key refused.
	[property: JsonPropertyName("filter")]
	[property: Description("Optional new case filter: {\"column\": \"<lookup column of the object, not the stage "
		+ "column>\", \"values\": [\"<value Id or exact display value>\", ...]} or {\"column\": \"...\", \"empty\": "
		+ "true}. It decides which NEW records start the case; omit to keep the filter.")]
	JsonElement? Filter = null,

	[property: JsonPropertyName("remove-filter")]
	[property: Description("Optional: true removes the case's filter, so it starts for every new record; refused while "
		+ "the object's cases are split by a filter column (filter-required).")]
	bool? RemoveFilter = null,

	[property: JsonPropertyName("enabled")]
	[property: Description("Optional: false disables the case (running cases continue, new records start none), true "
		+ "enables it. Send it alone, with no other change.")]
	bool? Enabled = null,

	[property: JsonPropertyName("confirm-running-instances")]
	[property: Description("LEAVE IT OUT unless the user said yes to changing a case that has running cases; never "
		+ "set it on your own. Without it such a change is refused (running-instances-confirmation-required) with "
		+ "the counts to show the user.")]
	bool? ConfirmRunningInstances = null,

	[property: JsonPropertyName("dry-run")]
	[property: Description("Run every check and return the planned diff without writing anything; default false.")]
	bool? DryRun = null) {

	/// <summary>
	/// Overflow bag for top-level keys the SDK could not bind to a declared argument. Inspected by the tool so a
	/// mis-keyed argument is named back to the caller.
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement> ExtensionData { get; init; }
}
