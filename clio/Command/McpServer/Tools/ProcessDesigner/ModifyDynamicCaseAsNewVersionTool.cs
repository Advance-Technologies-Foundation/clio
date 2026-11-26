using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools.ProcessDesigner;

/// <summary>
/// MCP tool that saves a change of a dynamic case (DCM) as a NEW, inactive version of it, the way the case designer's
/// "Save new version" does. The active case stays in force; only a confirmed stage value rename reaches its records at
/// once.
/// </summary>
public class ModifyDynamicCaseAsNewVersionTool(
	ModifyDynamicCaseAsNewVersionCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<ModifyDynamicCaseAsNewVersionOptions>(command, logger, commandResolver) {

	internal const string ModifyDynamicCaseAsNewVersionToolName = "modify-dynamic-case-as-new-version";

	/// <summary>The canonical field list echoed back when an unknown argument key is refused.</summary>
	internal const string ValidArgsHint = "Valid: environment-name, case-name, case-uid, entity-schema-name, "
		+ "package-name, stages, caption, filter, remove-filter, confirm-stage-records, confirm-value-rename, dry-run.";

	/// <summary>Refusal for a call whose whole argument object is absent.</summary>
	/// <remarks>
	/// Inline rather than in the shared helper for the reason <see cref="CreateBusinessProcessTool.NullArgsError"/>
	/// gives: it runs before any field can be read.
	/// </remarks>
	internal const string NullArgsError = "args is required: the call carried no argument object. " + ValidArgsHint;

	/// <summary>Minimal valid <c>args</c> object, used by the mini-contract: a dry run that writes nothing.</summary>
	internal const string MinimalExample =
		"{\"environment-name\":\"<env>\",\"case-name\":\"UsrOrderCase\",\"stages\":[\"New\",\"In progress\",\"Done\"],"
		+ "\"dry-run\":true}";

	/// <summary>
	/// The stage item shape named back when an item carries an undeclared key: modify-dynamic-case's without
	/// <c>steps</c> and <c>transitionType</c>, which the package refuses on a version (the version copies the active
	/// case's steps with their conditions). They stay in the shared key list, so the agent meets that refusal and not unknown-keys.
	/// </summary>
	internal const string StageShape =
		"{\"value\": \"<lookup value Id or exact display value>\", \"caption\"?: \"...\", \"color\"?: \"#RRGGBB\", "
		+ "\"group\"?: \"<parent stage value>\" | null, \"successful\"?: false}";

	/// <summary>
	/// Saves the change as a new version of the case, or plans it on a dry run, on the specified environment.
	/// </summary>
	/// <param name="args">The tool arguments; see <see cref="ModifyDynamicCaseAsNewVersionArgs"/>.</param>
	/// <returns>The command execution result; the result document is in the log output.</returns>
	// Destructive=false: everything the call writes goes to a new, inactive member of the family and its own link row,
	// so what the environment runs is unchanged when it returns. The one live effect is a stage VALUE RENAME
	// (confirm-value-rename): the stage shows its lookup value's name, the value is one row every record shares, so
	// the rename takes effect at once, for the active case and every record, not when the version is activated. It is
	// refused without the user's yes and the description says so; the annotation stays false for the reason
	// ModifyProcessAsNewVersionTool gives for setTracing - flipping it would put every version write behind destructive
	// consent for an effect only an explicit confirmation reaches. The facts ADR (D9) names this exception and
	// McpToolFactsCoverageTests.ConfirmedLiveOverwrites keeps it listed. Idempotent=false: each write makes one more
	// version, and no case tool deletes one (an inactive version is removed the way a case is undone).
	[McpServerTool(Name = ModifyDynamicCaseAsNewVersionToolName, ReadOnly = false, Destructive = false,
		 Idempotent = false, OpenWorld = false),
	 Description("Saves a case change as a version. The dynamic case (DCM) designer's \"Save new version\": the "
		 + "family's ACTIVE case is copied, the change is applied to the copy, and the copy is saved INACTIVE: the "
		 + "active case stays the one new records its filter fits start (every record when it has none), and its "
		 + "running cases go on as they are; only a confirmed stage value rename (below) reaches them at once. Read "
		 + "get-guidance name=process-dynamic-case-versions first. Call describe-dynamic-case, then this tool with "
		 + "dry-run=true, show "
		 + "the user the planned diff and the notes and ask before writing. Name the active case by case-name or "
		 + "case-uid, or by entity-schema-name when the object has one case. package-name: where the version is saved; "
		 + "left out, the active case's package, refused package-name-required when that package does not accept "
		 + "changes. stages, caption, filter and remove-filter work as on modify-dynamic-case, for the version only; "
		 + "setting the object's first filter column is refused (not-in-version). The version copies the active "
		 + "case's steps as they are, with their conditions; steps-not-supported means steps or transitionType were sent with a stage (change "
		 + "them on the version afterwards with modify-dynamic-case), or the active case has steps and package-name "
		 + "names another package (save the version in the case's own package). A stage caption renames the stage's "
		 + "lookup value AT ONCE, for the active case and every record too, not at activation (confirm-value-rename). "
		 + "Removing a stage that records hold needs confirm-stage-records. Never activates: after the user has "
		 + "checked the version and said yes, activate it with set-active-dynamic-case-version. Not idempotent: each "
		 + "write makes one more version; no case tool or Case-library action deletes one (an inactive version goes "
		 + "as a case does, get-guidance name=process-dynamic-case undo steps 1-2, after the user's yes); a saved "
		 + "version runs for no record until it is activated, and a later change to it is made in place with modify-dynamic-case by its case-name. "
		 + "After failed-leftover or unknown, run "
		 + $"describe-dynamic-case before any retry. No compile, no restart. Requires CrtProcessBuilder {DynamicCaseChangeRequirement.MinimumProcessBuilderVersion} or "
		 + "later (install-process-builder, after user approval).")]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	public CommandExecutionResult ModifyDynamicCaseAsNewVersion(
		[Description("modify-dynamic-case-as-new-version parameters")] [Required]
		ModifyDynamicCaseAsNewVersionArgs args) {
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
				missing, ForModifyDynamicCaseAsNewVersion);
		}
		if (missing.Count > 0) {
			return ToolMiniContract.Decorate(
				CommandExecutionResult.FromValidationError(
					"One of case-name, case-uid or entity-schema-name is required: name the active case to copy."),
				missing, ForModifyDynamicCaseAsNewVersion);
		}
		// The server takes the UId and ignores the name, so both together would copy whichever case the UId names,
		// silently; this write is not idempotent, so it is refused rather than guessed.
		if (!string.IsNullOrWhiteSpace(args.CaseName) && !string.IsNullOrWhiteSpace(args.CaseUId)) {
			return DynamicCaseStageArguments.Refuse("Provide only one of case-name or case-uid, not both.",
				DynamicCaseStageArguments.InvalidRequestCode);
		}
		if (args.Caption is not null && string.IsNullOrWhiteSpace(args.Caption)) {
			return DynamicCaseStageArguments.Refuse(
				"caption cannot be empty: leave it out to keep the case caption in the version.",
				DynamicCaseStageArguments.InvalidRequestCode);
		}
		if (args.PackageName is not null && string.IsNullOrWhiteSpace(args.PackageName)) {
			return DynamicCaseStageArguments.Refuse(
				"package-name cannot be empty: leave it out to save the version in the active case's package.",
				DynamicCaseStageArguments.InvalidRequestCode);
		}
		// The stage keys of modify-dynamic-case: steps and transitionType travel to the package, whose own
		// steps-not-supported refusal says what a version does with them, rather than being answered unknown-keys here.
		if (!DynamicCaseStageArguments.TryRead(args.Stages ?? default, ModifyDynamicCaseTool.StageKeys,
				StageShape,
				out List<DynamicCaseStageItem> stages, out CommandExecutionResult stagesRefusal)) {
			return stagesRefusal;
		}
		if (!DynamicCaseFilterArguments.TryRead(args.Filter ?? default, out DynamicCaseFilterArgument filter,
				out CommandExecutionResult filterRefusal)) {
			return filterRefusal;
		}
		if (filter is not null && args.RemoveFilter == true) {
			return DynamicCaseStageArguments.Refuse("filter and remove-filter=true contradict each other: send one of them.",
				DynamicCaseStageArguments.InvalidRequestCode);
		}

		ModifyDynamicCaseAsNewVersionOptions options = new() {
			Environment = args.EnvironmentName,
			CaseName = args.CaseName?.Trim(),
			CaseUId = args.CaseUId?.Trim(),
			EntitySchemaName = args.EntitySchemaName?.Trim(),
			PackageName = args.PackageName?.Trim(),
			Stages = stages,
			Caption = args.Caption?.Trim(),
			Filter = filter,
			RemoveFilter = args.RemoveFilter ?? false,
			ConfirmStageRecords = args.ConfirmStageRecords ?? false,
			ConfirmValueRename = args.ConfirmValueRename ?? false,
			DryRun = args.DryRun ?? false
		};
		// A case is interpreted, so the deterministic note can never contradict the response; it rides success only,
		// like on every other process-designer write.
		CommandExecutionResult result = InternalExecute<ModifyDynamicCaseAsNewVersionCommand>(options);
		return result.ExitCode != 0 ? result : result.WithCompileNotRequiredNote();
	}

	/// <summary>The mini-contract returned when a required argument is missing.</summary>
	/// <param name="missing">The required arguments the call lacked.</param>
	internal static string ForModifyDynamicCaseAsNewVersion(IReadOnlyList<string> missing) => ToolMiniContract.Build(
		ModifyDynamicCaseAsNewVersionToolName,
		missing,
		[
			"environment-name (registered environment)",
			"case-name, case-uid or entity-schema-name (the active case, or its object when the object has one case)",
			"optional: package-name, stages, caption, filter, remove-filter, confirm-stage-records, "
				+ "confirm-value-rename, dry-run (see the tool description)"
		],
		MinimalExample);

	private static List<string> MissingRequired(ModifyDynamicCaseAsNewVersionArgs args) {
		List<string> missing = [];
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			missing.Add("environment-name");
		}
		if (string.IsNullOrWhiteSpace(args.CaseName) && string.IsNullOrWhiteSpace(args.CaseUId)
			&& string.IsNullOrWhiteSpace(args.EntitySchemaName)) {
			missing.Add(ModifyDynamicCaseTool.CaseIdentityArgument);
		}
		return missing;
	}
}

/// <summary>
/// MCP arguments for the <c>modify-dynamic-case-as-new-version</c> tool (kebab-case wire keys, repo convention).
/// </summary>
public sealed record ModifyDynamicCaseAsNewVersionArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("case-name")]
	[property: Description("The active case's schema name; another member of its family is refused, naming the "
		+ "active one.")]
	string CaseName = null,

	[property: JsonPropertyName("case-uid")]
	[property: Description("The active case's schema UId; not together with case-name.")]
	string CaseUId = null,

	[property: JsonPropertyName("entity-schema-name")]
	[property: Description("The object's schema name (e.g. UsrOrder); a unique caption is resolved. Accepted only "
		+ "when it has exactly one case (else case-ambiguous with the list).")]
	string EntitySchemaName = null,

	[property: JsonPropertyName("package-name")]
	[property: Description("Optional package to save the version in; it must accept changes. Left out: the active "
		+ "case's package, refused package-name-required when that package does not accept changes. When the active "
		+ "case has steps, only its own package (another one is refused steps-not-supported).")]
	string PackageName = null,

	// JsonElement for the reasons ModifyDynamicCaseArgs.Stages gives: several accepted shapes, undeclared item keys
	// refused, an explicit "group": null kept apart from an absent key.
	[property: JsonPropertyName("stages")]
	[property: Description("Optional WHOLE desired stage list of the version in order, as on modify-dynamic-case: "
		+ "an array of {\"value\": \"<lookup value Id or exact display value>\", \"caption\"?, \"color\"?: "
		+ "\"#RRGGBB\", \"group\"?, \"successful\"?} (a bare string is {\"value\"}). steps and transitionType "
		+ "are refused steps-not-supported: the version copies the active case's steps with their conditions. A stage left out is not in the "
		+ "version, its steps and their conditions with it; one a kept step's condition still moves records to is "
		+ "refused stage-is-condition-target, as on modify-dynamic-case. A member left out keeps the active case's value. caption renames the stage's lookup value AT "
		+ "ONCE for every record, only with confirm-value-rename. Omit to keep the stages.")]
	JsonElement? Stages = null,

	[property: JsonPropertyName("caption")]
	[property: Description("Optional caption of the version; omit to keep the active case's.")]
	string Caption = null,

	[property: JsonPropertyName("filter")]
	[property: Description("Optional filter of the version: {\"column\": \"<lookup column of the object, not the "
		+ "stage column>\", \"values\": [\"<value Id or exact display value>\", ...]} or {\"column\": \"...\", "
		+ "\"empty\": true}. The object's first filter column is refused (not-in-version); omit to keep the filter.")]
	JsonElement? Filter = null,

	[property: JsonPropertyName("remove-filter")]
	[property: Description("Optional: true removes the filter in the version.")]
	bool? RemoveFilter = null,

	[property: JsonPropertyName("confirm-stage-records")]
	[property: Description("LEAVE IT OUT unless the user said yes to a version without a stage that records hold "
		+ "(once it is active in an enabled family, records without a running case show no current stage; a record "
		+ "with a running case keeps its version while it runs); never set it on your own. Without it such a version "
		+ "is refused (stage-records-confirmation-required) with the counts.")]
	bool? ConfirmStageRecords = null,

	[property: JsonPropertyName("confirm-value-rename")]
	[property: Description("LEAVE IT OUT unless the user said yes to renaming the stage's lookup value, which every "
		+ "record shares and which takes effect NOW, for the active case too; never set it on your own. Without it a "
		+ "stage caption change is refused (value-rename-confirmation-required).")]
	bool? ConfirmValueRename = null,

	[property: JsonPropertyName("dry-run")]
	[property: Description("Run every check and return the planned version without writing anything; default "
		+ "false.")]
	bool? DryRun = null) {

	/// <summary>
	/// Overflow bag for top-level keys the SDK could not bind to a declared argument. Inspected by the tool so a
	/// mis-keyed argument is named back to the caller.
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement> ExtensionData { get; init; }
}
