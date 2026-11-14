using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools.ProcessDesigner;

/// <summary>
/// MCP tool that creates a minimal dynamic case (DCM) for an object from one of its lookup columns and adds the
/// stage progress bar to the object's Freedom UI record page.
/// </summary>
public class CreateDynamicCaseTool(
	CreateDynamicCaseCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<CreateDynamicCaseOptions>(command, logger, commandResolver) {

	internal const string CreateDynamicCaseToolName = "create-dynamic-case";

	/// <summary>The canonical field list echoed back when an unknown argument key is refused.</summary>
	internal const string ValidArgsHint = "Valid: environment-name, package-name, entity-schema-name, "
		+ "stage-column-name, stages, filter, deactivate-unfiltered, order-column, name, caption, add-progress-bar, "
		+ "save-on-change, page-schema-name, add-package-dependencies, dry-run.";

	/// <summary>Refusal for a call whose whole argument object is absent.</summary>
	/// <remarks>
	/// Inline rather than in the shared helper for the reason <see cref="CreateBusinessProcessTool.NullArgsError"/>
	/// gives: it runs before any field can be read.
	/// </remarks>
	internal const string NullArgsError = "args is required: the call carried no argument object. " + ValidArgsHint;

	/// <summary>Minimal valid <c>args</c> object, used by the mini-contract.</summary>
	internal const string MinimalExample =
		"{\"environment-name\":\"<env>\",\"package-name\":\"<package>\",\"entity-schema-name\":\"UsrOrder\","
		+ "\"stage-column-name\":\"UsrStatus\"}";

	/// <summary>The stage item shape named back when an item carries an undeclared key.</summary>
	internal const string StageShape =
		"{\"value\": \"<lookup value Id or exact display value>\", \"group\"?: \"<parent stage value>\", "
		+ "\"successful\"?: false}";

	private static readonly string[] StageKeys = [
		DynamicCaseStageArguments.ValueKey, DynamicCaseStageArguments.GroupKey, DynamicCaseStageArguments.SuccessfulKey
	];

	/// <summary>
	/// Creates the case, or plans it on a dry run, on the specified environment.
	/// </summary>
	/// <param name="args">The tool arguments; see <see cref="CreateDynamicCaseArgs"/>.</param>
	/// <returns>The command execution result; the result document is in the log output.</returns>
	// Destructive: it writes a schema, two data rows, their package bindings, package dependencies and a page.
	// Idempotent: an equivalent re-run answers already-present and writes nothing to the case, which is what makes a
	// re-run after a page failure safe. Destructive also keeps it out of the read-response deadline.
	[McpServerTool(Name = CreateDynamicCaseToolName, ReadOnly = false, Destructive = true, Idempotent = true,
		 OpenWorld = false),
	 Description("Create a minimal dynamic case (DCM): one stage per value of an object's lookup column, plus a "
		 + "record-page progress bar. Read get-guidance name=process-dynamic-case first. "
		 + "One call writes the case into package-name with its SysDcmSettings and SysDcmSchemaInSettings rows and "
		 + "bindings, adds the package dependencies it needs (add-package-dependencies=false refuses instead), and appends "
		 + "a crt.EntityStageProgressBar to the object's Freedom UI record page (add-progress-bar=false skips it; "
		 + "page-schema-name is required for an object with a page per record type). save-on-change=true makes a stage "
		 + "click save the record at once. "
		 + "stages: 2 to 20 lookup values (Ids or exact display values) in order, items may add group and successful; "
		 + "values left out are not stages. Without it, every value in the lookup's order, reported in case.orderSource. "
		 + "filter {column, values | empty} splits an object's cases by one lookup column (e.g. B2B and B2C orders); "
		 + "filters of one object never share a value (filter-overlap). Next to an enabled case without a filter it is "
		 + "refused (unfiltered-case-exists): give that case a filter with modify-dynamic-case, or pass "
		 + "deactivate-unfiltered=true ONLY after the user agreed to disable it. "
		 + "dry-run=true runs every check, the page write's dry run included, and writes nothing. Nothing is written until "
		 + "every check passes; a refusal answers outcome refused with a stable errorCode. An equivalent enabled "
		 + "case answers already-present and writes nothing, so a re-run is safe; another enabled case without a filter "
		 + "is refused (case-exists): change it with modify-dynamic-case. If the bar cannot be added, "
		 + "outcome created-page-failed keeps the case and page.retry is the exact update-page call. "
		 + "A case is interpreted: no compile-creatio and no restart, but open pages of the object need a reload "
		 + "(F5). Requires a CrtProcessBuilder that carries BuildCase (install-process-builder, after "
		 + "user approval). Steps inside stages are not supported.")]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	public CommandExecutionResult CreateDynamicCase(
		[Description("create-dynamic-case parameters")] [Required] CreateDynamicCaseArgs args) {
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
				missing, ForCreateDynamicCase);
		}
		if (missing.Count > 0) {
			return ToolMiniContract.Decorate(
				CommandExecutionResult.FromValidationError($"{missing[0]} is required and cannot be empty."),
				missing, ForCreateDynamicCase);
		}
		if (!DynamicCaseStageArguments.TryRead(args.Stages ?? default, StageKeys, StageShape,
				out List<DynamicCaseStageItem> stages, out CommandExecutionResult stagesRefusal)) {
			return stagesRefusal;
		}
		if (!DynamicCaseFilterArguments.TryRead(args.Filter ?? default, out DynamicCaseFilterArgument filter,
				out CommandExecutionResult filterRefusal)) {
			return filterRefusal;
		}
		if (args.DeactivateUnfiltered == true && filter is null) {
			return CommandExecutionResult.FromValidationError("deactivate-unfiltered goes with filter: it disables the "
				+ "cases without a filter so that a case with one can start next to them. [invalid-request]");
		}

		CreateDynamicCaseOptions options = new() {
			Environment = args.EnvironmentName,
			PackageName = args.PackageName.Trim(),
			EntitySchemaName = args.EntitySchemaName.Trim(),
			StageColumnName = args.StageColumnName.Trim(),
			Stages = stages ?? [],
			Filter = filter,
			DeactivateUnfiltered = args.DeactivateUnfiltered ?? false,
			OrderColumn = args.OrderColumn,
			Name = args.Name,
			Caption = args.Caption,
			AddProgressBar = args.AddProgressBar ?? true,
			SaveOnChange = args.SaveOnChange,
			PageSchemaName = args.PageSchemaName,
			AddPackageDependencies = args.AddPackageDependencies ?? true,
			DryRun = args.DryRun ?? false
		};
		// A case, its settings rows and a Freedom UI page are all interpreted, so the deterministic note can never
		// contradict the response; it rides success only, like on every other process-designer write.
		CommandExecutionResult result = InternalExecute<CreateDynamicCaseCommand>(options);
		return result.ExitCode != 0 ? result : result.WithCompileNotRequiredNote();
	}

	/// <summary>The mini-contract returned when a required argument is missing.</summary>
	/// <param name="missing">The required arguments the call lacked.</param>
	internal static string ForCreateDynamicCase(IReadOnlyList<string> missing) => ToolMiniContract.Build(
		CreateDynamicCaseToolName,
		missing,
		[
			"environment-name (registered environment)", "package-name (editable package that receives the case)",
			"entity-schema-name (the object, published)", "stage-column-name (a lookup column of the object)",
			"optional: stages, page-schema-name, dry-run and the rest in the tool description"
		],
		MinimalExample);

	private static List<string> MissingRequired(CreateDynamicCaseArgs args) {
		List<string> missing = [];
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			missing.Add("environment-name");
		}
		if (string.IsNullOrWhiteSpace(args.PackageName)) {
			missing.Add("package-name");
		}
		if (string.IsNullOrWhiteSpace(args.EntitySchemaName)) {
			missing.Add("entity-schema-name");
		}
		if (string.IsNullOrWhiteSpace(args.StageColumnName)) {
			missing.Add("stage-column-name");
		}
		return missing;
	}
}

/// <summary>
/// MCP arguments for the <c>create-dynamic-case</c> tool (kebab-case wire keys, repo convention).
/// </summary>
public sealed record CreateDynamicCaseArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("package-name")]
	[property: Description("Editable package that receives the case and its two settings bindings.")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("entity-schema-name")]
	[property: Description("The object the case belongs to; its published schema must carry the stage column.")]
	[property: Required]
	string EntitySchemaName,

	[property: JsonPropertyName("stage-column-name")]
	[property: Description("A lookup column of the object; its values become the stages.")]
	[property: Required]
	string StageColumnName,

	// JsonElement, not a typed list: an array of stage objects, an array of strings and a string holding either are
	// all accepted, and an undeclared item key is refused instead of being dropped by the binder.
	// Nullable because the schema exporter serializes a parameter's default value, and default(JsonElement) has none.
	[property: JsonPropertyName("stages")]
	[property: Description("Optional stages in order: an array of {\"value\": \"<lookup value Id or exact display value>\"} "
		+ "(bare strings are accepted), 2 to 20 values; a value left out is not a stage of this case. Omit for every "
		+ "value in the default order. An item "
		+ "may add \"group\": the parent stage's value, a stage of the list in no group itself (one level), and "
		+ "\"successful\": false for a stage of the final group (the last top-level stage and its members) that ends "
		+ "the case unsuccessfully.")]
	JsonElement? Stages = null,

	[property: JsonPropertyName("order-column")]
	[property: Description("Optional integer column of the lookup that orders the stages; ignored when stages is given.")]
	string OrderColumn = null,

	// JsonElement for the reason stages gives: an object or a string holding one, and an undeclared key refused.
	[property: JsonPropertyName("filter")]
	[property: Description("Optional case filter, for several cases on one object: {\"column\": \"<lookup column of the "
		+ "object, not the stage column>\", \"values\": [\"<value Id or exact display value>\", ...]} - the case starts "
		+ "only for records whose column holds one of them - or {\"column\": \"...\", \"empty\": true} for records whose "
		+ "column is empty. Omit for a case that starts for every record.")]
	JsonElement? Filter = null,

	[property: JsonPropertyName("deactivate-unfiltered")]
	[property: Description("LEAVE IT OUT unless the user agreed to disable the object's enabled case without a filter, "
		+ "as the case designer does. Goes with filter. The disabled case's running cases are cancelled at their "
		+ "record's next status change.")]
	bool? DeactivateUnfiltered = null,

	[property: JsonPropertyName("name")]
	[property: Description("Optional case schema name; an invalid or taken name is refused, never suffixed.")]
	string Name = null,

	[property: JsonPropertyName("caption")]
	[property: Description("Optional case caption; defaults to '<object caption> case'.")]
	string Caption = null,

	[property: JsonPropertyName("add-progress-bar")]
	[property: Description("Add the stage progress bar to the record page; default true.")]
	bool? AddProgressBar = null,

	[property: JsonPropertyName("page-schema-name")]
	[property: Description("Optional Freedom UI record page to decorate; required when the object opens a different "
		+ "page per record type.")]
	string PageSchemaName = null,

	[property: JsonPropertyName("add-package-dependencies")]
	[property: Description("Add the package dependencies the case needs; default true. false refuses instead, "
		+ "listing them in dependencies.missing.")]
	bool? AddPackageDependencies = null,

	[property: JsonPropertyName("dry-run")]
	[property: Description("Run every check, the page write's dry run included, and write nothing; default false.")]
	bool? DryRun = null,

	[property: JsonPropertyName("save-on-change")]
	[property: Description("A stage click on the bar saves the record at once; default false: the click only sets "
		+ "the field and the user saves. A value that cannot apply is reported in a note: a bar already on the page "
		+ "keeps its own mode, and add-progress-bar=false adds no bar.")]
	bool? SaveOnChange = null) {

	/// <summary>
	/// Overflow bag for top-level keys the SDK could not bind to a declared argument. Inspected by the tool so a
	/// mis-keyed argument is named back to the caller.
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement> ExtensionData { get; init; }
}
