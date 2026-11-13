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
		+ "stage-column-name, stages, order-column, name, caption, add-progress-bar, save-on-change, "
		+ "page-schema-name, add-package-dependencies, dry-run.";

	/// <summary>Refusal for a call whose whole argument object is absent.</summary>
	/// <remarks>
	/// Inline rather than in the shared helper for the reason <see cref="CreateBusinessProcessTool.NullArgsError"/>
	/// gives: it runs before any field can be read.
	/// </remarks>
	internal const string NullArgsError = "args is required: the call carried no argument object. " + ValidArgsHint;

	/// <summary>Error code of a stage item that carries steps; the guidance names it with its remedy.</summary>
	internal const string StepsNotSupportedCode = "steps-not-supported";

	/// <summary>Error code of a stage item that carries a key other than <c>value</c>.</summary>
	internal const string UnknownKeysCode = "unknown-keys";

	/// <summary>Error code of a stage item of the wrong shape.</summary>
	internal const string InvalidRequestCode = "invalid-request";

	/// <summary>Minimal valid <c>args</c> object, used by the mini-contract.</summary>
	internal const string MinimalExample =
		"{\"environment-name\":\"<env>\",\"package-name\":\"<package>\",\"entity-schema-name\":\"UsrOrder\","
		+ "\"stage-column-name\":\"UsrStatus\"}";

	private const string StageValueKey = "value";
	private const string StageStepsKey = "steps";

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
		 + "One call writes the case into package-name (one stage per lookup value, every stage reachable from every "
		 + "other, enabled), its SysDcmSettings and SysDcmSchemaInSettings rows with their package bindings, the "
		 + "package dependencies the case needs (add-package-dependencies=false refuses instead, listing them in "
		 + "dependencies.missing), and appends a crt.EntityStageProgressBar to the object's Freedom UI record "
		 + "page in that page's design package (add-progress-bar=false skips it; page-schema-name names the page, "
		 + "required when the object opens a different page per record type). The bar's default mode: a stage click "
		 + "only sets the field and the user saves; save-on-change=true makes a click save the record at once. "
		 + "The default stage order comes from the lookup (an integer Position, StageOrder, Number or StageNumber "
		 + "column, else the display value) and is reported in case.orderSource: pass stages (lookup value Ids or "
		 + "exact display values, every value exactly once) when the business order matters. "
		 + "dry-run=true runs every check, the page write's dry run included, and writes nothing. Nothing is written until "
		 + "every check passes, and a refusal answers outcome refused with a stable errorCode. An equivalent enabled "
		 + "case answers already-present and writes nothing to it, so a re-run is safe; a different enabled case on "
		 + "the object is refused (case-exists). When the case is created but the bar cannot be added, outcome "
		 + "created-page-failed keeps the case and page.retry is the exact update-page call. "
		 + "A case is interpreted: no compile-creatio and no restart, but open pages of the object need a reload "
		 + "(F5) to show the bar. Requires a CrtProcessBuilder that carries BuildCase (install-process-builder, after "
		 + "user approval). Steps inside stages, stage conditions and changing an existing case are not supported.")]
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
		if (!TryReadStages(args.Stages ?? default, out List<string> stages,
				out CommandExecutionResult stagesRefusal)) {
			return stagesRefusal;
		}

		CreateDynamicCaseOptions options = new() {
			Environment = args.EnvironmentName,
			PackageName = args.PackageName.Trim(),
			EntitySchemaName = args.EntitySchemaName.Trim(),
			StageColumnName = args.StageColumnName.Trim(),
			Stages = stages,
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

	/// <summary>
	/// Normalizes <c>stages</c>: an array of <c>{"value": ...}</c> objects, an array of strings, or a string holding
	/// either. A key other than <c>value</c> is refused here because the normalized list carries values only, and a
	/// dropped key would be a stage built differently from the one described.
	/// </summary>
	/// <param name="value">The bound argument.</param>
	/// <param name="stages">The stage values in order; empty when the argument was not supplied.</param>
	/// <param name="refusal">The refusal, when the argument cannot be read.</param>
	internal static bool TryReadStages(JsonElement value, out List<string> stages, out CommandExecutionResult refusal) {
		stages = [];
		refusal = null;
		if (McpToolArgumentSupport.IsAbsentJsonDocument(value)) {
			return true;
		}
		if (!McpToolArgumentSupport.TryReadJsonDocumentArgument(value, JsonValueKind.Array, "stages",
				out string json, out refusal)) {
			return false;
		}
		JsonNode parsed;
		try {
			parsed = JsonNode.Parse(json);
		} catch (JsonException exception) {
			refusal = Refuse($"stages is not valid JSON: {exception.Message}", InvalidRequestCode);
			return false;
		}
		if (parsed is not JsonArray items) {
			refusal = Refuse("stages must be a JSON array.", InvalidRequestCode);
			return false;
		}
		for (int index = 0; index < items.Count; index++) {
			if (!TryReadStage(items[index], index, out string stage, out refusal)) {
				return false;
			}
			stages.Add(stage);
		}
		return true;
	}

	private static bool TryReadStage(JsonNode item, int index, out string stage, out CommandExecutionResult refusal) {
		stage = null;
		refusal = null;
		string text;
		switch (item) {
			case JsonValue scalar when scalar.TryGetValue(out text):
				break;
			case JsonObject entry: {
				if (entry.ContainsKey(StageStepsKey)) {
					refusal = Refuse($"stages[{index}] carries steps: steps inside stages are not supported yet. "
						+ "Create the case without them.", StepsNotSupportedCode);
					return false;
				}
				string[] unknown = entry.Select(property => property.Key)
					.Where(key => !string.Equals(key, StageValueKey, StringComparison.Ordinal))
					.Select(McpToolArgumentSupport.DescribeCallerKey)
					.ToArray();
				if (unknown.Length > 0) {
					refusal = Refuse($"stages[{index}] carries unknown keys: {McpToolArgumentSupport.JoinCallerKeys(unknown)}. "
						+ "A stage is {\"value\": \"<lookup value Id or exact display value>\"}.", UnknownKeysCode);
					return false;
				}
				if (entry[StageValueKey] is not JsonValue valueNode || !valueNode.TryGetValue(out text)) {
					refusal = Refuse($"stages[{index}].value must be a string: the lookup value Id or its exact "
						+ "display value.", InvalidRequestCode);
					return false;
				}
				break;
			}
			default:
				refusal = Refuse($"stages[{index}] must be a string or an object {{\"value\": \"...\"}}.",
					InvalidRequestCode);
				return false;
		}
		if (string.IsNullOrWhiteSpace(text)) {
			refusal = Refuse($"stages[{index}] is empty.", InvalidRequestCode);
			return false;
		}
		stage = text.Trim();
		return true;
	}

	private static CommandExecutionResult Refuse(string message, string code) =>
		CommandExecutionResult.FromValidationError($"{message} [{code}]");

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

	// JsonElement, not a typed list: an array of {"value": ...} objects, an array of strings and a string holding
	// either are all accepted, and a key other than value is refused instead of being dropped by the binder.
	// Nullable because the schema exporter serializes a parameter's default value, and default(JsonElement) has none.
	[property: JsonPropertyName("stages")]
	[property: Description("Optional stage order: an array of {\"value\": \"<lookup value Id or exact display value>\"} "
		+ "(bare strings are accepted), naming every lookup value exactly once. Omit for the default order.")]
	JsonElement? Stages = null,

	[property: JsonPropertyName("order-column")]
	[property: Description("Optional integer column of the lookup that orders the stages; ignored when stages is given.")]
	string OrderColumn = null,

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
		+ "the field and the user saves. A bar already on the page keeps its own mode; a value passed there is "
		+ "reported in a note, not applied.")]
	bool? SaveOnChange = null) {

	/// <summary>
	/// Overflow bag for top-level keys the SDK could not bind to a declared argument. Inspected by the tool so a
	/// mis-keyed argument is named back to the caller.
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement> ExtensionData { get; init; }
}
