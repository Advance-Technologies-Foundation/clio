using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.BusinessRules;
using Clio.Command.McpServer;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class ToolContractGetTool {
	internal const string ToolName = "get-tool-contract";

	/// <summary>
	/// In-band discovery hint appended to "tool not found" / "unknown tool" error messages so an agent
	/// whose guessed name (and any "did you mean" shortlist) missed still has an explicit path to the full
	/// catalog: the compact index returned by <c>get-tool-contract</c> with no arguments. Wording mirrors
	/// <c>McpServerInstructions</c> ("compact index of every tool") for a single consistent phrasing.
	/// </summary>
	internal const string DiscoveryHint =
		"Call get-tool-contract with no arguments for a compact index of every tool.";

	private readonly IMcpToolInvokerRegistry? _toolInvokerRegistry;

	/// <summary>
	/// Initializes the tool without a registry. Curated contracts and the lossy reflection fallback
	/// still resolve; only registry-derived contracts for uncurated tools are unavailable.
	/// </summary>
	public ToolContractGetTool()
		: this(null) {
	}

	/// <summary>
	/// Initializes the tool with the invoker registry so contracts for UNCURATED tools are derived from
	/// the same MCP tool input schema <c>clio-run</c> / <c>clio-run-destructive</c> dispatch against,
	/// keeping the advertised contract aligned with the real invokable argument shape.
	/// </summary>
	/// <param name="toolInvokerRegistry">
	/// The MCP invoker registry; resolved from DI by the SDK per call (may be <c>null</c> in tests that
	/// only exercise curated contracts).
	/// </param>
	public ToolContractGetTool(IMcpToolInvokerRegistry? toolInvokerRegistry) {
		_toolInvokerRegistry = toolInvokerRegistry;
	}

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Returns clio MCP tool contracts. Omit tool-names for a compact index of ALL tools (names + one-line purpose + safety flags) — cheap discovery without full schemas; pass tool-names to expand those tools' full contracts (parameter schema, aliases, defaults, examples, and preferred or fallback workflow hints); pass detail=full (with no tool-names) to expand every tool's full contract at once.")]
	public ToolContractGetResponse GetToolContracts(
		[Description("Parameters: tool-names (optional array of tool names) and detail (optional 'index' | 'full'). Omit entirely for a compact index of all tools; pass tool-names for full contracts; pass detail=full to expand all full contracts.")]
		ToolContractGetArgs? args = null) {
		// A natural no-arguments discovery call (the first call an agent makes) sends no args object at all.
		// Treat a missing args object exactly like an omitted-tool-names call so it yields the compact index:
		// new ToolContractGetArgs() has ToolNames=null / Detail=null, which the downstream logic resolves to
		// the index path unchanged. All recovery/alias/detail handling below then operates on a non-null args.
		args ??= new ToolContractGetArgs();
		try {
			// Field-test defect #4: an agent that omits the SDK's nested `args` wrapper and calls flat
			// (e.g. {"tool-names":[...]} or {"name":"x"}) has those keys land in the [JsonExtensionData]
			// overflow bag rather than binding to ToolNames. Recover a flat tool-names / name (and the
			// known kebab/camel/snake spellings) from the overflow when ToolNames was not bound and the
			// overflow contains ONLY name-bearing keys, so the natural flat shape resolves contracts
			// instead of failing. A genuinely-unknown key still falls through to the helpful alias error.
			// Resolve detail first so the flat path below can honor a co-present flat `detail` key, and so
			// {"tool-names":[...],"detail":"full"} flat calls resolve named contracts instead of failing.
			string? detail = TryRecoverFlatDetail(args) ?? args.Detail;
			IReadOnlyList<string>? flatToolNames = TryRecoverFlatToolNames(args);
			if (flatToolNames is not null) {
				return ToolContractCatalog.GetContracts(flatToolNames, _toolInvokerRegistry, detail);
			}
			string? aliasError = CollectLegacyAliasError(args);
			if (aliasError is not null) {
				return new ToolContractGetResponse(
					false,
					Error: new ToolContractError(
						"invalid-parameter-alias",
						aliasError + " " + ExpectedArgsShapeHint));
			}
			return ToolContractCatalog.GetContracts(args.ToolNames, _toolInvokerRegistry, detail);
		} catch (Exception ex) {
			return new ToolContractGetResponse(
				false,
				Error: new ToolContractError(
					"internal-error",
					$"get-tool-contract failed: {ex.Message}. {ExpectedArgsShapeHint}"));
		}
	}

	private const string ExpectedArgsShapeHint =
		"Expected args shape: {\"tool-names\": [\"list-pages\", ...] } or omit tool-names to list all.";

	private static readonly Dictionary<string, string> LegacyAliases = new(StringComparer.Ordinal) {
		["toolNames"] = ToolNamesParam,
		["tool_names"] = ToolNamesParam,
		["toolName"] = ToolNamesParam,
		["tool-name"] = ToolNamesParam,
		["tool_name"] = ToolNamesParam,
		["name"] = ToolNamesParam,
		["names"] = ToolNamesParam
	};

	// Flat top-level keys that carry the tool name(s) when the nested `args` wrapper is omitted. Limited
	// to the canonical 'tool-names' (which lands in the overflow bag only when the call is flat) and the
	// natural 'name' spelling. The camelCase/snake_case legacy spellings (toolName, tool_names, ...) are
	// deliberately NOT recovered here — they keep returning the teaching alias error from LegacyAliases.
	private static readonly HashSet<string> FlatToolNameKeys = new(StringComparer.Ordinal) {
		ToolNamesParam,
		"name"
	};

	private const string ToolNamesParam = "tool-names";

	private const string DetailParam = "detail";

	/// <summary>
	/// Recovers a flat <c>detail</c> value from the overflow bag when the caller sent <c>detail</c> at the
	/// top level without the SDK's nested <c>args</c> wrapper. Returns the string value when the overflow
	/// carries a <c>detail</c> string key; otherwise <c>null</c> so <see cref="ToolContractGetArgs.Detail"/>
	/// (the bound nested value) is used instead.
	/// </summary>
	private static string? TryRecoverFlatDetail(ToolContractGetArgs args) {
		Dictionary<string, JsonElement>? overflow = args.ExtensionData;
		if (overflow is null
			|| !overflow.TryGetValue(DetailParam, out JsonElement value)
			|| value.ValueKind != JsonValueKind.String) {
			return null;
		}
		string? detail = value.GetString();
		return string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
	}

	/// <summary>
	/// Recovers a flat <c>tool-names</c> payload from the overflow bag when the caller sent the request
	/// without the SDK's nested <c>args</c> wrapper. Returns the recovered names only when <see
	/// cref="ToolContractGetArgs.ToolNames"/> was not bound and EVERY overflow key is a recognized
	/// name-bearing key; otherwise returns <c>null</c> so an unknown key still produces the helpful
	/// alias error. String and string-array overflow values are both accepted.
	/// </summary>
	private static IReadOnlyList<string>? TryRecoverFlatToolNames(ToolContractGetArgs args) {
		if (args.ToolNames is { Count: > 0 }) {
			return null;
		}
		Dictionary<string, JsonElement>? overflow = args.ExtensionData;
		if (overflow is null || overflow.Count == 0) {
			return null;
		}
		// A co-present flat `detail` key is recovered separately (TryRecoverFlatDetail) and is NOT a
		// name-bearing key, so exclude it before deciding whether the remaining keys are all flat
		// tool-name keys. This lets {"tool-names":[...],"detail":"full"} flat calls recover the names
		// instead of mis-reporting `tool-names` as an unknown arg.
		List<KeyValuePair<string, JsonElement>> nameEntries = overflow
			.Where(pair => !string.Equals(pair.Key, DetailParam, StringComparison.Ordinal))
			.ToList();
		if (nameEntries.Count == 0 || !nameEntries.All(pair => FlatToolNameKeys.Contains(pair.Key))) {
			return null;
		}
		List<string> recovered = [];
		// Every name-bearing value must contribute a valid name; .All short-circuits on the first
		// malformed value (mirrors the original early-return) so recovery is abandoned wholesale.
		if (!nameEntries.All(pair => TryAppendFlatToolName(pair.Value, recovered))) {
			return null;
		}
		return recovered.Count > 0 ? recovered : null;
	}

	/// <summary>
	/// Appends the tool name(s) carried by a single overflow <paramref name="value"/> to
	/// <paramref name="recovered"/>. A string value contributes itself; an array value contributes each
	/// of its string items. Blank entries are skipped. Returns <c>false</c> when the value is neither a
	/// string nor an array (a malformed name-bearing key), signalling the caller to abandon recovery so
	/// the request falls through to the alias error path; otherwise returns <c>true</c>.
	/// </summary>
	private static bool TryAppendFlatToolName(JsonElement value, List<string> recovered) {
		switch (value.ValueKind) {
			case JsonValueKind.String:
				AddIfNotBlank(value.GetString(), recovered);
				return true;
			case JsonValueKind.Array:
				AppendStringArrayItems(value, recovered);
				return true;
			default:
				// A name-bearing key carrying a non-string/non-array value is malformed; do not
				// recover — let it fall through to the alias error path with the shape hint.
				return false;
		}
	}

	/// <summary>
	/// Appends every string item of a JSON array <paramref name="array"/> to <paramref name="recovered"/>,
	/// skipping non-string items and blank values.
	/// </summary>
	private static void AppendStringArrayItems(JsonElement array, List<string> recovered) {
		foreach (JsonElement item in array.EnumerateArray()) {
			if (item.ValueKind == JsonValueKind.String) {
				AddIfNotBlank(item.GetString(), recovered);
			}
		}
	}

	/// <summary>
	/// Adds <paramref name="name"/> (trimmed) to <paramref name="recovered"/> when it is neither
	/// <c>null</c> nor whitespace.
	/// </summary>
	private static void AddIfNotBlank(string? name, List<string> recovered) {
		if (!string.IsNullOrWhiteSpace(name)) {
			recovered.Add(name.Trim());
		}
	}

	private static string? CollectLegacyAliasError(ToolContractGetArgs args) {
		// A recognized flat `detail` key is already recovered by TryRecoverFlatDetail, so drop it from the
		// overflow before the alias check to avoid reporting it as an unknown arg. Any OTHER unknown key
		// still produces the helpful teaching error.
		IReadOnlyDictionary<string, JsonElement>? overflow = args.ExtensionData;
		if (overflow is not null && overflow.ContainsKey(DetailParam)) {
			overflow = overflow
				.Where(pair => !string.Equals(pair.Key, DetailParam, StringComparison.Ordinal))
				.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
		}
		return McpToolArgumentSupport.BuildLegacyAliasError(
			overflow, LegacyAliases, ". tool-names must be an array of strings.",
			"Valid: tool-names (array of strings). Omit args to list all tools.");
	}
}

public sealed record ToolContractGetArgs(
	[property: JsonPropertyName("tool-names")]
	[property: Description("Optional array of tool names. Omit to return a compact index of all clio MCP tools (names + one-line purpose); pass names to expand their full contracts.")]
	IReadOnlyList<string>? ToolNames = null,
	[property: JsonPropertyName("detail")]
	[property: Description("Optional detail level used only when tool-names is omitted: 'index' (default) returns the compact index of all tools; 'full' returns the full contracts of all tools (legacy behavior).")]
	string? Detail = null
) {
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record ToolContractGetResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("tools")] IReadOnlyList<ToolContractDefinition>? Tools = null,
	[property: JsonPropertyName("error")] ToolContractError? Error = null,
	[property: JsonPropertyName("index")] IReadOnlyList<ToolContractIndexEntry>? Index = null
);

/// <summary>
/// A compact, discovery-only entry for one clio MCP tool. It carries the tool name, a one-line purpose
/// distilled from the tool's full description, and lightweight safety/availability flags so an agent can
/// see WHAT tools exist without paying for the heavy full contract (input schema, examples, flows, error
/// contract). This is the Anthropic <c>defer_loading</c> shape — names resident, schemas on demand. Call
/// <c>get-tool-contract</c> with the specific <c>tool-names</c> to expand any entry into its full contract.
/// </summary>
/// <param name="Name">The stable MCP tool name (kebab-case), matching the full contract's name.</param>
/// <param name="Purpose">A one-line purpose distilled from the first sentence of the tool's description.</param>
/// <param name="ContractAvailable">Whether a full curated contract is reachable by naming this tool.</param>
/// <param name="Resident">
/// Whether the tool is present in <c>tools/list</c> and therefore called natively (<c>true</c>), or hidden
/// from <c>tools/list</c> (<c>false</c>). A non-resident tool is reachable through <c>clio-run</c> /
/// <c>clio-run-destructive</c>, and on the stdio transport also via forgiving direct invocation
/// (the durable unmatched-name handler). Derived from <see cref="McpCoreToolProfile.IsResident"/>, independent of whether an
/// invoker registry is supplied. Never wrap a resident tool in <c>clio-run</c>.
/// </param>
/// <param name="Destructive">
/// Whether the tool is destructive (modifies/deletes data) when this is cheaply known from the MCP tool
/// annotation; <c>null</c> when the destructive hint is unavailable (for example when no invoker registry
/// is supplied).
/// </param>
public sealed record ToolContractIndexEntry(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("purpose")] string Purpose,
	[property: JsonPropertyName("contract-available")] bool ContractAvailable,
	[property: JsonPropertyName("resident")] bool Resident,
	[property: JsonPropertyName("destructive")] bool? Destructive = null,
	[property: JsonPropertyName("aliases")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	IReadOnlyList<string> Aliases = null
);

[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "This serialized contract record mirrors the external MCP wire shape and grouping fields would make the contract harder to inspect and evolve.")]
public sealed record ToolContractDefinition(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("description")] string Description,
	[property: JsonPropertyName("input-schema")] ToolInputSchemaContract InputSchema,
	[property: JsonPropertyName("output-contract")] ToolOutputContract OutputContract,
	[property: JsonPropertyName("error-contract")] ToolErrorContract ErrorContract,
	[property: JsonPropertyName("aliases")] IReadOnlyList<ToolContractAlias> Aliases,
	[property: JsonPropertyName("defaults")] IReadOnlyList<ToolContractDefaultValue> Defaults,
	[property: JsonPropertyName("examples")] IReadOnlyList<ToolContractExample> Examples,
	[property: JsonPropertyName("preferred-flow")] ToolFlowHint PreferredFlow,
	[property: JsonPropertyName("fallback-flow")] IReadOnlyList<ToolFlowHint> FallbackFlow,
	[property: JsonPropertyName("deprecations")] IReadOnlyList<ToolDeprecation> Deprecations,
	[property: JsonPropertyName("anti-patterns")] IReadOnlyList<ToolAntiPattern>? AntiPatterns = null,
	[property: JsonPropertyName("preconditions")] IReadOnlyList<string>? Preconditions = null
);

public sealed record ToolInputSchemaContract(
	[property: JsonPropertyName("required")] IReadOnlyList<string> Required,
	[property: JsonPropertyName("properties")] IReadOnlyList<ToolContractField> Properties,
	[property: JsonPropertyName("any-of")] IReadOnlyList<IReadOnlyList<string>>? AnyOf = null,
	[property: JsonPropertyName("validators")] IReadOnlyList<ToolContractValidator>? Validators = null
);

public sealed record ToolContractField(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("type")] string Type,
	[property: JsonPropertyName("description")] string Description
);

public sealed record ToolOutputContract(
	[property: JsonPropertyName("kind")] string Kind,
	[property: JsonPropertyName("success-field")] string? SuccessField,
	[property: JsonPropertyName("failure-signals")] IReadOnlyList<string> FailureSignals,
	[property: JsonPropertyName("fields")] IReadOnlyList<ToolContractField> Fields
);

public sealed record ToolErrorContract(
	[property: JsonPropertyName("codes")] IReadOnlyList<ToolErrorCodeContract> Codes
);

public sealed record ToolErrorCodeContract(
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("message")] string Message
);

public sealed record ToolContractAlias(
	[property: JsonPropertyName("scope")] string Scope,
	[property: JsonPropertyName("canonical-name")] string CanonicalName,
	[property: JsonPropertyName("alias")] string Alias,
	[property: JsonPropertyName("status")] string Status,
	[property: JsonPropertyName("message")] string Message
);

public sealed record ToolContractDefaultValue(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("value")] string Value,
	[property: JsonPropertyName("reason")] string Reason
);

public sealed record ToolContractExample(
	[property: JsonPropertyName("summary")] string Summary,
	[property: JsonPropertyName("arguments")] IReadOnlyDictionary<string, object?> Arguments
);

public sealed record ToolFlowHint(
	[property: JsonPropertyName("tools")] IReadOnlyList<string> Tools,
	[property: JsonPropertyName("notes")] string Notes
);

public sealed record ToolDeprecation(
	[property: JsonPropertyName("message")] string Message,
	[property: JsonPropertyName("replacement-tools")] IReadOnlyList<string> ReplacementTools
);

public sealed record ToolAntiPattern(
	[property: JsonPropertyName("pattern")] string Pattern,
	[property: JsonPropertyName("why")] string Why
);

public sealed record ToolContractValidator(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("field")] string? Field = null,
	[property: JsonPropertyName("fields")] IReadOnlyList<string>? Fields = null,
	[property: JsonPropertyName("context")] string? Context = null,
	[property: JsonPropertyName("required")] bool? Required = null
);

public sealed record ToolContractError(
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("message")] string Message,
	[property: JsonPropertyName("suggestions")] IReadOnlyList<string>? Suggestions = null,
	[property: JsonPropertyName("field-errors")] IReadOnlyList<ToolContractFieldError>? FieldErrors = null
);

public sealed record ToolContractFieldError(
	[property: JsonPropertyName("field")] string Field,
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("message")] string Message
);

internal static class ToolContractCatalog {
	private const string ActionFieldName = "action";
	private const string AppCodeFieldName = "app-code";
	private const string AppNameFieldName = "app-name";
	private const string ApplicationCodeFieldName = "application-code";
	private const string ApplicationIdFieldName = "application-id";
	private const string ArrayType = "array";
	private const string BindingNameFieldName = "binding-name";
	private const string BooleanFalseLiteral = "false";
	private const string BooleanType = "boolean";
	private const string ColumnNameFieldName = "column-name";
	private const string ColumnsFieldName = "columns";
	private const string CountFieldName = "count";
	private const string ConstDefaultValueSourceName = nameof(Terrasoft.Core.Entities.EntitySchemaColumnDefSource.Const);
	private const string DefaultValueConfigFieldName = "default-value-config";
	private const string DefaultValueConfigSourceKey = "source";
	private const string DescriptionLocalizationsFieldName = "description-localizations";
	private const string DryRunFieldName = "dry-run";
	private const string ConfirmFieldName = "confirm";
	private const string EntityFieldName = "entity";
	private const string EntitySchemaNameDescription = "Entity schema name.";
	private const string EntitySchemaNameFieldName = "entity-schema-name";
	private const string EnvironmentNameFieldName = "environment-name";
	private const string ErrorFieldName = "error";
	private const string ExampleAccountSchemaName = "Account";
	private const string ExampleContactSchemaName = "Contact";
	private const string ExampleEnvironmentName = "local";
	private const string ExampleLookupValueId = "00000000-0000-0000-0000-000000000001";
	private const string ExamplePackageName = "UsrTaskApp";
	private const string ExampleTaskStatusSchemaName = "UsrTaskStatus";
	private const string FailureMessageDescription = "Human-readable failure message.";
	private const string FieldFieldName = "field";
	private const string FiltersFieldName = "filters";
	private const string LogicalOperationFieldName = "logicalOperation";
	private const string ConditionFieldName = "condition";
	private const string ConditionsFieldName = "conditions";
	private const string ActionsFieldName = "actions";
	private const string ExampleEqualComparison = "EQUAL";
	private const string ExampleOwnerAttributeName = "Owner";
	private const string ExampleAssigneeAttributeName = "Assignee";
	private const string ExampleTaskSchemaName = "UsrTask";
	private const string ExampleEqualConditionComparison = "equal";
	private const string IconBackgroundFieldName = "icon-background";
	private const string InvalidLocalizationMapCode = "invalid-localization-map";
	private const string KeyValueFieldName = "key-value";
	private const string LimitFieldName = "limit";
	private const string LoginFieldName = "login";
	private const string NumberType = "number";
	private const string ObjectType = "object";
	private const string ComponentTypeFieldName = "component-type";
	private const string OperationsFieldName = "operations";
	private const string PackageNameFieldName = "package-name";
	private const string PasswordFieldName = "password";
	private const string PagesFieldName = "pages";
	private const string PageSchemaNameFieldName = "page-schema-name";
	private const string ParentSchemaNameFieldName = "parent-schema-name";
	private const string ParameterScope = "parameter";
	private const string QueryCorrelationIdentifierDescription = "Query correlation identifier when available.";
	private const string QueryFieldName = "query";
	private const string ReferenceSchemaNameFieldName = "reference-schema-name";
	private const string RegisteredEnvironmentNameDescription = "Registered clio environment name.";
	private const string RejectedStatus = "rejected";
	private const string SelectorCodeFieldName = "code";
	private const string SelectorIdFieldName = "id";
	private const string SchemaNameFieldName = "schema-name";
	private const string ResourcesFieldName = "resources";
	private const string SelectFieldName = "select";
	private const string SkipSamplingFieldName = "skip-sampling";
	private const string StringType = "string";
	private const string StatusFieldName = "status";
	private const string SuccessFalseSignal = "success == false";
	private const string SuccessFieldName = "success";
	private const string TemplateCodeFieldName = "template-code";
	private const string TitleLocalizationsFieldName = "title-localizations";
	private const string ToolSucceededDescription = "Whether the tool succeeded.";
	private const string ApplicationNameFieldName = "application-name";
	private const string ApplicationVersionFieldName = "application-version";
	private const string CaptionFieldName = "caption";
	private const string DescriptionFieldName = "description";
	private const string IconIdFieldName = "icon-id";
	private const string InstalledApplicationCodeDescription = "Installed application code.";
	private const string InstalledApplicationDisplayNameDescription = "Installed application display name.";
	private const string InstalledApplicationIdentifierDescription = "Installed application identifier.";
	private const string InstalledApplicationVersionDescription = "Installed application version.";
	private const string InvalidWorkflowShapeCode = "invalid-workflow-shape";
	private const string IsVirtualFieldName = "is-virtual";
	private const string MissingRequiredParameterCode = "missing-required-parameter";
	private const string PackageUIdFieldName = "package-u-id";
	private const string PackageNameDescription = "Target package name.";
	private const string PrimaryPackageIdentifierDescription = "Primary package identifier.";
	private const string PrimaryPackageNameDescription = "Primary package name.";
	private const string RuleFieldName = "rule";
	private const string RulesFieldName = "rules";
	private const string SectionCodeFieldName = "section-code";
	private const string DeleteEntitySchemaFieldName = "delete-entity-schema";
	private const string SearchPatternFieldName = "search-pattern";
	private const string EventNameFieldName = "event_name";
	private const string TelemetryConsentFieldName = "telemetry_consent";
	private const string ExampleOrderPageSchemaName = "UsrOrder_FormPage";
	private const string ExampleWorkspacePath = "<workspace>/UsrTaskApp";
	private const string MakeReadOnlyActionTypeName = "make-read-only";
	private const string MakeRequiredActionTypeName = "make-required";
	private const string ValueFieldName = "value";
	private const string ValuesFieldName = "values";
	private const string VerifyFieldName = "verify";
	private const string BindingNameDescription = "Binding name.";
	private const string WorkspacePathDescription = "Absolute local workspace path. Network-share paths are not supported.";
	private const string WorkspacePathFieldName = "workspace-path";
	private const string DataForgePlatformRequirementDescription =
		"Requires Creatio platform version 10.0.0 or later; CrtDataForge is included in supported platform versions.";
	private const string WithMobilePagesFieldName = "with-mobile-pages";

		private static readonly ToolErrorContract CommonErrorContract = new([
			new ToolErrorCodeContract("tool-not-found", "Requested tool name is not registered by clio MCP."),
			new ToolErrorCodeContract(MissingRequiredParameterCode, "A required parameter is missing."),
			new ToolErrorCodeContract("invalid-parameter-alias", "A legacy or unsupported parameter alias was used."),
			new ToolErrorCodeContract("invalid-parameter-type", "A parameter value type does not match the tool contract."),
			new ToolErrorCodeContract(InvalidLocalizationMapCode, "A localization map is malformed or missing en-US."),
		new ToolErrorCodeContract(InvalidWorkflowShapeCode, "The request shape is structurally invalid for the target tool.")
	]);

	private static readonly IReadOnlyDictionary<string, ToolContractDefinition> Contracts =
		new Dictionary<string, ToolContractDefinition>(StringComparer.OrdinalIgnoreCase) {
			[ToolContractGetTool.ToolName] = BuildToolContractGet(),
			[GuidanceGetTool.ToolName] = BuildGuidanceGet(),
			[ExecuteEsqTool.ToolName] = BuildExecuteEsq(),
			[SettingsHealthTool.ToolName] = BuildSettingsHealth(),
			[GetTelemetryConsentTool.ToolName] = BuildGetTelemetryConsent(),
			[SendTelemetryTool.ToolName] = BuildSendTelemetry(),
			[WithdrawTelemetryConsentTool.ToolName] = BuildWithdrawTelemetryConsent(),
			[ApplicationCreateTool.ApplicationCreateToolName] = BuildApplicationCreate(),
			[ApplicationSectionCreateTool.ApplicationSectionCreateToolName] = BuildApplicationSectionCreate(),
			[ApplicationSectionUpdateTool.ApplicationSectionUpdateToolName] = BuildApplicationSectionUpdate(),
			[ApplicationSectionDeleteTool.ApplicationSectionDeleteToolName] = BuildApplicationSectionDelete(),
			[ApplicationSectionGetListTool.ApplicationSectionGetListToolName] = BuildApplicationSectionGetList(),
			[ApplicationGetInfoTool.ApplicationGetInfoToolName] = BuildApplicationGetInfo(),
			[ApplicationGetListTool.ApplicationGetListToolName] = BuildApplicationGetList(),
			[DataForgeTool.DataForgeStatusToolName] = BuildDataForgeStatus(),
			[DataForgeTool.DataForgeFindTablesToolName] = BuildDataForgeFindTables(),
			[DataForgeTool.DataForgeFindLookupsToolName] = BuildDataForgeFindLookups(),
			[DataForgeTool.DataForgeGetRelationsToolName] = BuildDataForgeGetRelations(),
			[DataForgeTool.DataForgeGetTableColumnsToolName] = BuildDataForgeGetTableColumns(),
			[DataForgeTool.DataForgeContextToolName] = BuildDataForgeContext(),
			[DataForgeTool.DataForgeInitializeToolName] = BuildDataForgeInitialize(),
			[DataForgeTool.DataForgeUpdateToolName] = BuildDataForgeUpdate(),
			[ODataReadTool.ToolName] = BuildODataRead(),
			[ODataCreateTool.ToolName] = BuildODataCreate(),
			[ODataUpdateTool.ToolName] = BuildODataUpdate(),
			[ODataDeleteTool.ToolName] = BuildODataDelete(),
			[SchemaSyncTool.ToolName] = BuildSchemaSync(),
			[PageSyncTool.ToolName] = BuildPageSync(),
			[PageListTool.ToolName] = BuildPageList(),
			[PageGetTool.ToolName] = BuildPageGet(),
			[CreateLookupTool.CreateLookupToolName] = BuildCreateLookup(),
			[CreateEntitySchemaTool.CreateEntitySchemaToolName] = BuildCreateEntity(),
			[UpdateEntitySchemaTool.UpdateEntitySchemaToolName] = BuildUpdateEntity(),
			[CreateDataBindingTool.CreateDataBindingToolName] = BuildCreateDataBinding(),
			[AddDataBindingRowTool.AddDataBindingRowToolName] = BuildAddDataBindingRow(),
			[RemoveDataBindingRowTool.RemoveDataBindingRowToolName] = BuildRemoveDataBindingRow(),
			[CreateDataBindingDbTool.CreateDataBindingDbToolName] = BuildCreateDataBindingDb(),
			[UpsertDataBindingRowDbTool.UpsertDataBindingRowDbToolName] = BuildUpsertDataBindingRowDb(),
			[RemoveDataBindingRowDbTool.RemoveDataBindingRowDbToolName] = BuildRemoveDataBindingRowDb(),
			[GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName] = BuildGetEntitySchemaProperties(),
			[GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName] = BuildGetEntitySchemaColumnProperties(),
			[FindEntitySchemaTool.FindEntitySchemaToolName] = BuildFindEntitySchema(),
			[ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName] = BuildModifyEntitySchemaColumn(),
			[ComponentInfoTool.ToolName] = BuildComponentInfo(),
			[PageUpdateTool.ToolName] = BuildPageUpdate(),
			[PageValidateTool.ToolName] = BuildPageValidate(),
			[ApplicationDeleteTool.ToolName] = BuildApplicationDelete(),
			[CreateEntityBusinessRuleTool.BusinessRuleCreateToolName] = BuildEntityBusinessRuleCreate(),
			[CreatePageBusinessRuleTool.BusinessRuleCreateToolName] = BuildPageBusinessRuleCreate(),
			[SchemaNamePrefixTool.GetSchemaNamePrefixToolName] = BuildGetSchemaNamePrefix(),
			[CompileCreatioTool.CompileCreatioToolName] = BuildCompileCreatio(),
			[CreateUiProjectTool.CreateUiProjectToolName] = BuildNewUiProject(),
			[SysSettingGetTool.GetSysSettingToolName] = BuildGetSysSetting(),
			[SysSettingsListTool.ListSysSettingsToolName] = BuildListSysSettings(),
			[SysSettingCreateTool.CreateSysSettingToolName] = BuildCreateSysSetting(),
			[SysSettingUpdateTool.UpdateSysSettingToolName] = BuildUpdateSysSetting(),
			[InstallGateTool.InstallGateToolName] = BuildInstallGate(),
			[AssertInfrastructureTool.AssertInfrastructureToolName] = BuildAssertInfrastructure(),
			[ShowPassingInfrastructureTool.ShowPassingInfrastructureToolName] = BuildShowPassingInfrastructure(),
			[FindEmptyIisPortTool.FindEmptyIisPortToolName] = BuildFindEmptyIisPort(),
			[InstallerCommandTool.DeployCreatioToolName] = BuildDeployCreatio(),
			[DeployIdentityTool.DeployIdentityToolName] = BuildDeployIdentity(),
			[RestoreWorkspaceTool.RestoreWorkspaceToolName] = BuildRestoreWorkspace(),
			[PushWorkspaceTool.PushWorkspaceToolName] = BuildPushWorkspace(),
			[ListCreatioBuildsTool.ListCreatioBuildsToolName] = BuildListCreatioBuilds()
		};

	private static readonly string[] CanonicalToolNames = [
		GuidanceGetTool.ToolName,
		ExecuteEsqTool.ToolName,
		SettingsHealthTool.ToolName,
		GetTelemetryConsentTool.ToolName,
		SendTelemetryTool.ToolName,
		WithdrawTelemetryConsentTool.ToolName,
		ApplicationCreateTool.ApplicationCreateToolName,
		ApplicationSectionCreateTool.ApplicationSectionCreateToolName,
		ApplicationSectionUpdateTool.ApplicationSectionUpdateToolName,
		CreateEntityBusinessRuleTool.BusinessRuleCreateToolName,
		CreatePageBusinessRuleTool.BusinessRuleCreateToolName,
		ApplicationSectionDeleteTool.ApplicationSectionDeleteToolName,
		ApplicationSectionGetListTool.ApplicationSectionGetListToolName,
		ApplicationGetInfoTool.ApplicationGetInfoToolName,
		ApplicationGetListTool.ApplicationGetListToolName,
		DataForgeTool.DataForgeStatusToolName,
		DataForgeTool.DataForgeFindTablesToolName,
		DataForgeTool.DataForgeFindLookupsToolName,
		DataForgeTool.DataForgeGetRelationsToolName,
		DataForgeTool.DataForgeGetTableColumnsToolName,
		DataForgeTool.DataForgeContextToolName,
		ODataReadTool.ToolName,
		ODataCreateTool.ToolName,
		ODataUpdateTool.ToolName,
		ODataDeleteTool.ToolName,
		SchemaSyncTool.ToolName,
		PageSyncTool.ToolName,
		PageListTool.ToolName,
		PageGetTool.ToolName,
		CreateLookupTool.CreateLookupToolName,
		CreateEntitySchemaTool.CreateEntitySchemaToolName,
		UpdateEntitySchemaTool.UpdateEntitySchemaToolName,
		CreateDataBindingDbTool.CreateDataBindingDbToolName,
		UpsertDataBindingRowDbTool.UpsertDataBindingRowDbToolName,
		RemoveDataBindingRowDbTool.RemoveDataBindingRowDbToolName,
		FindEntitySchemaTool.FindEntitySchemaToolName,
		GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName,
		GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName,
		ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName,
		ComponentInfoTool.ToolName,
		PageUpdateTool.ToolName,
		PageValidateTool.ToolName,
		ApplicationDeleteTool.ToolName,
		SchemaNamePrefixTool.GetSchemaNamePrefixToolName,
		CompileCreatioTool.CompileCreatioToolName,
		CreateUiProjectTool.CreateUiProjectToolName,
		SysSettingGetTool.GetSysSettingToolName,
		SysSettingsListTool.ListSysSettingsToolName,
		SysSettingCreateTool.CreateSysSettingToolName,
		SysSettingUpdateTool.UpdateSysSettingToolName
	];

	/// <summary>The <c>detail</c> value that opts into the legacy full-contract dump for a no-names request.</summary>
	private const string FullDetail = "full";

	/// <summary>The one-line purpose is truncated to this many characters for the compact index.</summary>
	private const int MaxPurposeLength = 120;

	/// <summary>
	/// Resolves clio MCP tool contracts. When <paramref name="toolNames"/> is omitted the response depends
	/// on <paramref name="detail"/>: any value other than <c>full</c> (the default) returns the cheap
	/// compact INDEX of EVERY invokable tool — curated core plus the hidden long tail reachable through
	/// clio-run (names + one-line purpose + safety flags, <c>Tools</c> null);
	/// <c>full</c> returns every canonical tool's full contract (legacy behavior, <c>Index</c> null). When
	/// <paramref name="toolNames"/> is supplied the named tools' full contracts are returned via the
	/// curated → registry → reflection → not-found cascade (unchanged).
	/// </summary>
	/// <param name="toolNames">The requested tool names, or <c>null</c>/empty to discover all tools.</param>
	/// <param name="toolInvokerRegistry">Optional invoker registry used to derive uncurated contracts and the index destructive hint.</param>
	/// <param name="detail">Optional detail level for a no-names request: <c>index</c> (default) or <c>full</c>.</param>
	internal static ToolContractGetResponse GetContracts(
		IReadOnlyList<string>? toolNames,
		IMcpToolInvokerRegistry? toolInvokerRegistry = null,
		string? detail = null) {
		if (toolNames is null || toolNames.Count == 0) {
			if (string.Equals(detail, FullDetail, StringComparison.OrdinalIgnoreCase)) {
				return new ToolContractGetResponse(
					true,
					CanonicalToolNames.Select(name => Contracts[name]).ToArray());
			}
			return new ToolContractGetResponse(
				true,
				Index: BuildCompactIndex(toolInvokerRegistry));
		}
		List<string> normalizedNames = [];
		for (int index = 0; index < toolNames.Count; index++) {
			string? name = toolNames[index];
			if (string.IsNullOrWhiteSpace(name)) {
				return new ToolContractGetResponse(
					false,
					Error: new ToolContractError(
						MissingRequiredParameterCode,
						"tool-names must contain non-empty tool names.",
						FieldErrors: [
							new ToolContractFieldError($"tool-names[{index}]", MissingRequiredParameterCode,
								"Provide a non-empty tool name.")
						]));
			}
			normalizedNames.Add(name.Trim());
		}
		List<ToolContractDefinition> results = [];
		foreach (string normalizedName in normalizedNames.Distinct(StringComparer.OrdinalIgnoreCase)) {
			// Curated contracts take precedence over any derived contract.
			if (Contracts.TryGetValue(normalizedName, out ToolContractDefinition? contract)) {
				results.Add(contract);
				continue;
			}
			// No curated contract: derive the contract from the SAME registered MCP tool input schema that
			// clio-run / clio-run-destructive dispatch against, so the advertised contract matches the real
			// invokable argument shape (Codex review #1). This captures single-scalar params (e.g.
			// stop-creatio's environmentName) the lossy reflection fallback drops.
			if (McpToolRegistrySchemaContract.TryBuild(toolInvokerRegistry, normalizedName, out ToolContractDefinition registryContract)) {
				results.Add(registryContract);
				continue;
			}
			// Last resort: reflection over the options type. Only reached when the registry has no entry
			// (e.g. registry unavailable), and may be lossy for some tools.
			if (McpToolSchemaCatalog.TryGetSchemaContract(normalizedName, out ToolContractDefinition schemaContract)) {
				results.Add(schemaContract);
				continue;
			}
			return new ToolContractGetResponse(
				false,
				Error: new ToolContractError(
					"tool-not-found",
					$"Tool '{normalizedName}' is not registered by clio MCP. {ToolContractGetTool.DiscoveryHint}",
					BuildSuggestions(normalizedName)));
		}
		return new ToolContractGetResponse(true, results);
	}

	private static IReadOnlyList<string> BuildSuggestions(string requestedName) {
		return Contracts.Keys
			.Concat(McpToolSchemaCatalog.RegisteredToolNames)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(name => McpToolArgumentSupport.LevenshteinDistance(requestedName, name))
			.ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
			.Take(3)
			.ToArray();
	}

	/// <summary>
	/// Builds the compact discovery index over EVERY invokable MCP tool — the curated core (canonical
	/// contracts) plus the hidden long tail reachable only through <c>clio-run</c> /
	/// <c>clio-run-destructive</c> (for example <c>stop-creatio</c>, <c>add-package-dependency</c>). The
	/// name set is the case-insensitive union of <see cref="CanonicalToolNames"/>, the invoker registry's
	/// <see cref="IMcpToolInvokerRegistry.ToolNames"/>, and the reflection catalog's
	/// <see cref="McpToolSchemaCatalog.RegisteredToolNames"/>, deduplicated and ordered ordinally so the
	/// index is deterministic for prompt-cache prefix stability. Each entry carries the tool name, a
	/// one-line purpose (curated description, else the registry/reflection-derived description), the
	/// contract-available flag (true when a curated OR derived contract resolves), and the destructive
	/// hint when the invoker registry can cheaply supply it. The index stays compact — names + purpose +
	/// safety flags only, never full schemas.
	/// </summary>
	/// <param name="toolInvokerRegistry">Optional registry used to enumerate hidden tools and read each tool's destructive hint; <c>null</c> leaves the flag unset and limits the set to curated + reflection-discovered tools.</param>
	private static IReadOnlyList<ToolContractIndexEntry> BuildCompactIndex(
		IMcpToolInvokerRegistry? toolInvokerRegistry) {
		return BuildIndexToolNames(toolInvokerRegistry)
			.Select(name => BuildIndexEntry(name, toolInvokerRegistry))
			.ToArray();
	}

	/// <summary>
	/// Produces the deterministic, deduplicated (case-insensitive) union of every invokable MCP tool name:
	/// curated canonical tools, registry-invokable hidden tools, and reflection-discovered tools. Ordered
	/// ordinally by name so the index prefix stays stable for prompt caching. <c>get-tool-contract</c>
	/// itself is excluded so the discovery tool does not index itself (matching the curated-only behavior).
	/// </summary>
	/// <param name="toolInvokerRegistry">Optional registry contributing the hidden long-tail tool names.</param>
	private static IEnumerable<string> BuildIndexToolNames(IMcpToolInvokerRegistry? toolInvokerRegistry) {
		IEnumerable<string> registryNames = toolInvokerRegistry?.ToolNames ?? [];
		return CanonicalToolNames
			.Concat(registryNames)
			.Concat(McpToolSchemaCatalog.RegisteredToolNames)
			.Where(name => !string.IsNullOrWhiteSpace(name)
				&& !string.Equals(name, ToolContractGetTool.ToolName, StringComparison.OrdinalIgnoreCase))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(name => name, StringComparer.Ordinal);
	}

	/// <summary>
	/// Builds a single compact index entry for <paramref name="name"/>. The purpose is distilled from the
	/// curated contract when one exists, otherwise from the tool's RAW registry/reflection description
	/// (without the "no curated contract yet" note the full named contract carries); a missing description
	/// falls back to the tool name so the purpose is never empty.
	/// <c>contract-available</c> is true when a curated OR registry OR reflection contract resolves.
	/// <c>resident</c> is derived from <see cref="McpCoreToolProfile.IsResident"/> and does not depend on
	/// an invoker registry being supplied.
	/// </summary>
	/// <param name="name">The tool name to describe.</param>
	/// <param name="toolInvokerRegistry">Optional registry used to derive uncurated descriptions and the destructive hint.</param>
	private static ToolContractIndexEntry BuildIndexEntry(
		string name,
		IMcpToolInvokerRegistry? toolInvokerRegistry) {
		bool contractAvailable = TryResolveIndexDescription(name, toolInvokerRegistry, out string description);
		string purpose = BuildPurpose(description);
		return new ToolContractIndexEntry(
			name,
			string.IsNullOrEmpty(purpose) ? name : purpose,
			ContractAvailable: contractAvailable,
			Resident: McpCoreToolProfile.IsResident(name),
			Destructive: ResolveDestructive(toolInvokerRegistry, name),
			// Deprecated alias names are projected from the compatibility catalog (the single source of
			// truth for renames), so an agent scanning the index finds a legacy name next to its canonical
			// entry instead of concluding the tool disappeared.
			Aliases: McpToolCompatibilityCatalog.SeedAliasesByCanonical.TryGetValue(name, out IReadOnlyList<string> aliases)
				? aliases
				: null);
	}

	/// <summary>
	/// Resolves the description for an index entry through the same curated → registry → reflection cascade
	/// the NAMED contract path uses for AVAILABILITY, but reads the RAW functional description (no
	/// "Auto-generated … no curated contract yet" note) for the uncurated registry/reflection levels so the
	/// compact one-line purpose reflects only what the tool DOES. The note still appears in the FULL named
	/// contract (see <see cref="GetContracts"/>); only this index one-liner is noteless. Returns <c>true</c>
	/// with the resolved description when any source matches — preserving <c>contract-available</c> semantics
	/// (a tool with a raw description but no curated contract is still dispatchable via clio-run) — otherwise
	/// <c>false</c> with an empty description (the caller supplies a safe fallback to the tool name).
	/// </summary>
	/// <param name="name">The tool name to resolve.</param>
	/// <param name="toolInvokerRegistry">Optional registry holding hidden, dispatchable tools.</param>
	/// <param name="description">The resolved description when available; otherwise empty.</param>
	private static bool TryResolveIndexDescription(
		string name,
		IMcpToolInvokerRegistry? toolInvokerRegistry,
		out string description) {
		// Level 1 — curated descriptions are handwritten and carry NO note, so use them verbatim.
		if (Contracts.TryGetValue(name, out ToolContractDefinition? curated)) {
			description = curated.Description;
			return true;
		}
		// Level 2 — registry-derived: read the RAW tool description (not TryBuild, which appends the note).
		// A registered tool resolves here under the SAME condition TryBuild would, so contract-available
		// stays true even though the one-liner is now noteless.
		if (toolInvokerRegistry is not null
			&& McpToolRegistrySchemaContract.TryGetRawDescription(toolInvokerRegistry, name, out description)) {
			return true;
		}
		// Level 3 — reflection fallback: read the RAW reflected description (not TryGetSchemaContract, which
		// appends the note). Same availability condition as the note-appending path.
		if (McpToolSchemaCatalog.TryGetRawDescription(name, out description)) {
			return true;
		}
		description = string.Empty;
		return false;
	}

	/// <summary>
	/// Distills a one-line purpose from a full tool description: takes the first sentence (up to the first
	/// period followed by whitespace) or first line, collapses inner whitespace, and truncates to
	/// <see cref="MaxPurposeLength"/> characters with an ellipsis so the index stays compact.
	/// </summary>
	/// <param name="description">The full curated tool description.</param>
	private static string BuildPurpose(string description) {
		string normalized = string.Join(' ',
			(description ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
		if (normalized.Length == 0) {
			return string.Empty;
		}
		int sentenceEnd = FindFirstSentenceEnd(normalized);
		string firstSentence = sentenceEnd >= 0 ? normalized[..(sentenceEnd + 1)] : normalized;
		if (firstSentence.Length <= MaxPurposeLength) {
			return firstSentence;
		}
		return firstSentence[..(MaxPurposeLength - 1)].TrimEnd() + "…";
	}

	/// <summary>
	/// Returns the index of the first sentence-terminating period (a '.' followed by whitespace or the end
	/// of the text), or <c>-1</c> when the text has no sentence break. Abbreviation periods mid-word (for
	/// example <c>en-US</c> or version numbers) are kept because they are not followed by whitespace.
	/// </summary>
	/// <param name="text">The whitespace-normalized description.</param>
	private static int FindFirstSentenceEnd(string text) {
		for (int index = 0; index < text.Length; index++) {
			if (text[index] != '.') {
				continue;
			}
			if (index == text.Length - 1 || char.IsWhiteSpace(text[index + 1])) {
				return index;
			}
		}
		return -1;
	}

	/// <summary>
	/// Reads the destructive hint for a tool from the invoker registry when one is supplied. Returns
	/// <c>null</c> when no registry is available so the flag is omitted rather than guessed. The registry
	/// fails CLOSED for unknown names, so a registered-but-unmapped name reports destructive.
	/// </summary>
	/// <param name="toolInvokerRegistry">Optional registry exposing the per-tool destructive hint.</param>
	/// <param name="toolName">The tool name to resolve.</param>
	private static bool? ResolveDestructive(IMcpToolInvokerRegistry? toolInvokerRegistry, string toolName) {
		return toolInvokerRegistry?.IsDestructive(toolName);
	}

	private static ToolContractDefinition BuildToolContractGet() {
		return new ToolContractDefinition(
			ToolContractGetTool.ToolName,
			"Returns clio MCP tool contracts. Omit tool-names for a compact index of all tools (name + one-line purpose + safety flags) for cheap discovery; pass tool-names to expand those tools' full executable contracts; pass detail=full (with no tool-names) to expand every tool's full contract.",
			new ToolInputSchemaContract(
				[],
				[
					Field("tool-names", ArrayType, "Optional array of tool names. Omit for a compact index of all tools; pass names to expand their full contracts."),
					Field("detail", StringType, "Optional detail level used only when tool-names is omitted: 'index' (default) returns the compact index; 'full' returns every tool's full contract.")
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, "Whether the contract lookup succeeded."),
				Field("tools", ArrayType, "Full tool contract definitions; populated when tool-names are passed or detail=full."),
				Field("index", ArrayType, "Compact tool index (name, purpose, contract-available, resident, destructive); populated for a no-names request unless detail=full. resident=true tools are present in tools/list and are called natively; resident=false tools are reachable only via clio-run/clio-run-destructive — never wrap a resident tool in clio-run."),
				Field(ErrorFieldName, ObjectType, "Structured error payload when lookup fails.")
			),
			CommonErrorContract,
			[],
			[],
			[
				Example("Return the compact index of all clio MCP tools (cheap discovery)", new Dictionary<string, object?>()),
				Example("Return the full contracts of every tool (legacy behavior)", new Dictionary<string, object?> {
					["detail"] = "full"
				}),
				Example("Return the contract for list-apps, update-page, and modify-entity-schema-column", new Dictionary<string, object?> {
					["tool-names"] = new[] { "list-apps", "update-page", "modify-entity-schema-column" }
				})
			],
			Flow(["get-tool-contract"], "Call with no args first for the compact index of all tools, then call with specific tool-names for full schemas before execution."),
			[],
			[]);
	}

	private static ToolContractDefinition BuildSettingsHealth() {
		return new ToolContractDefinition(
			SettingsHealthTool.ToolName,
			"Reports the clio bootstrap health for appsettings.json, including auto-repairs, active environment resolution, and whether environment-scoped tools can execute.",
			new ToolInputSchemaContract(
				[],
				[]),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, "Whether the check-settings-health lookup succeeded."),
				Field(StatusFieldName, StringType, "Bootstrap health status: healthy, repaired, degraded, or broken."),
				Field("settings-file-path", StringType, "Absolute path to clio appsettings.json."),
				Field("active-environment-key", StringType, "Configured ActiveEnvironmentKey before fallback resolution."),
				Field("resolved-active-environment-key", StringType, "Environment key resolved for bootstrap use after repair or fallback."),
				Field("environment-count", NumberType, "Number of configured environments after bootstrap processing."),
				Field("issues", ArrayType, "Detected bootstrap issues."),
				Field("repairs-applied", ArrayType, "Safe automatic repairs applied during bootstrap."),
				Field("can-start-bootstrap-tools", BooleanType, "Whether bootstrap-safe tools can start."),
				Field("can-execute-env-tools", BooleanType, "Whether commands that depend on named environments can execute."),
				Field(ErrorFieldName, ObjectType, "Structured error payload when lookup fails.")
			),
			CommonErrorContract,
			[],
			[],
			[
				Example("Report current clio bootstrap health", new Dictionary<string, object?>())
			],
			Flow(
				[
					SettingsHealthTool.ToolName
				],
				"Use before environment-scoped commands when local clio settings may be stale, missing, or unreadable."),
			[
				Flow(
					[
						SettingsHealthTool.ToolName,
						ToolContractGetTool.ToolName
					],
					"Follow with get-tool-contract when the caller must choose a bootstrap-safe recovery or inspection tool.")
			],
			[]);
	}

	private static ToolContractDefinition BuildSendTelemetry() {
		return BuildSendTelemetryContract(SendTelemetryTool.ToolName, "Use at product workflow milestones after the user has granted consent; until consent is granted nothing is stored, so events sent earlier are silently dropped. The set of events and their order is owned by the consuming skill/contract. Delivery is non-blocking and fire-and-forget.");
	}

	private static ToolContractDefinition BuildGetTelemetryConsent() {
		return BuildGetTelemetryConsentContract(GetTelemetryConsentTool.ToolName,
			"Use before sending the first product telemetry event to check whether consent is already stored. When telemetry_consent is unknown, the consuming workflow obtains the user's decision and persists it once via send-telemetry; until consent is granted, send-telemetry stores nothing, so events sent earlier are silently dropped.");
	}

	private static ToolContractDefinition BuildWithdrawTelemetryConsent() {
		return new ToolContractDefinition(
			WithdrawTelemetryConsentTool.ToolName,
			"Withdraws product telemetry consent: sets the locally stored decision to denied and deletes any not-yet-uploaded local telemetry events, so collection stops and no further uploads start. Forward-looking — it does not delete events already uploaded to Creatio (those expire on the server-side retention timer). Idempotent and safe to call from any prior state.",
			new ToolInputSchemaContract([], []),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, ToolSucceededDescription),
				Field(StatusFieldName, StringType, "Withdrawal status: withdrawn (consent set to denied) or withdraw-failed (a local I/O fault left consent unchanged)."),
				Field(TelemetryConsentFieldName, StringType, "Local consent value after the call: denied on success."),
				Field("events_purged", NumberType, "Count of not-yet-uploaded local telemetry event files deleted by the withdrawal.")),
			CommonErrorContract,
			[],
			[],
			[
				Example("Withdraw telemetry consent when the developer opts out", new Dictionary<string, object?>())
			],
			Flow([WithdrawTelemetryConsentTool.ToolName], "Call when the developer asks to stop, turn off, opt out of, or withdraw product telemetry. Idempotent and safe from any state; after success get-telemetry-consent returns denied and the workflow continues without telemetry."),
			[],
			[]);
	}

	private static ToolContractDefinition BuildGetTelemetryConsentContract(string toolName, string flowNotes) {
		return new ToolContractDefinition(
			toolName,
			"Reads locally persisted product telemetry consent without storing any telemetry event. Telemetry covers an AI-assisted Creatio app-development session run through this MCP server, driven by a consuming skill/contract; if no such skill is active, do not call this tool or prompt for consent.",
			new ToolInputSchemaContract([], []),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, ToolSucceededDescription),
				Field(StatusFieldName, StringType, "Consent lookup status: known or unknown."),
				Field(TelemetryConsentFieldName, StringType, "Local consent value: granted, denied, or unknown.")),
			CommonErrorContract,
			[],
			[],
			[
				Example("Read local telemetry consent", new Dictionary<string, object?>())
			],
			Flow([toolName], flowNotes),
			[],
			[],
			[
				new ToolAntiPattern("Sending a telemetry event before consent is established", "Use this read-only consent tool before any telemetry event. Do not call send-telemetry until consent is granted or the first-run answer must be persisted.")
			]);
	}

	private static ToolContractDefinition BuildSendTelemetryContract(string toolName, string flowNotes) {
		return new ToolContractDefinition(
			toolName,
			"Stores a single product telemetry event (about an AI-assisted Creatio app-development session run through this MCP server, driven by a consuming skill/contract) as a local OpenTelemetry-shaped JSON file after user consent. If no such skill is active, do not call this tool. When a telemetry endpoint is configured, stored events are uploaded in the background and removed locally on success; no agent action is needed.",
			new ToolInputSchemaContract(
				["session_id", EventNameFieldName, "coding_agent", "plugin_version"],
				[
					Field("session_id", StringType, "Stable product workflow session identifier reused across all events in one app-creation conversation."),
					Field(EventNameFieldName, StringType,
						$"Product event name. Allowed values: {string.Join(", ", Clio.Common.Telemetry.TelemetryService.AllowedEventNames)}."),
					Field("coding_agent", StringType, "Agent or host name, for example Claude Code, Codex, GitHub Copilot CLI, or Cursor."),
					Field("plugin_version", StringType, "Product plugin version."),
					Field(TelemetryConsentFieldName, StringType, "Optional first-use consent value after asking the user: granted or denied."),
					Field("duration_ms", NumberType, "Optional elapsed time in milliseconds for the step this event represents, where applicable. Omit it and clio infers the duration from local session timing when it can.")
				],
				Validators: [
					new ToolContractValidator("enum", "unknown-event-name", EventNameFieldName,
						Context: "event_name must be one of the documented product event names.")
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal,
					"status == rejected"
				],
				Field(SuccessFieldName, BooleanType, ToolSucceededDescription),
				Field(StatusFieldName, StringType, "Telemetry status: recorded (clio accepted the event; any upload to a collector happens separately and is not confirmed by this call), consent-denied, record-failed, or rejected."),
				Field("event_id", StringType, "Generated event identifier when an event is recorded."),
				Field(ErrorFieldName, ObjectType, "Structured validation or persistence error when rejected.")),
			new ToolErrorContract([
				..CommonErrorContract.Codes,
				new ToolErrorCodeContract("telemetry-consent-required",
					"Telemetry consent is not yet established. Ask the user and retry with telemetry_consent set to granted or denied."),
				new ToolErrorCodeContract("record-unavailable",
					"clio could not record the event because of a local I/O fault; it was not retained."),
				new ToolErrorCodeContract("unsupported-fields",
					"The payload contains fields outside the documented product telemetry fields."),
				new ToolErrorCodeContract("missing-required-field",
					"A required telemetry field (session_id, event_name, coding_agent, or plugin_version) is blank."),
				new ToolErrorCodeContract("unknown-event-name",
					"event_name is not one of the documented product event names."),
				new ToolErrorCodeContract("unknown-consent",
					"telemetry_consent is set to a value other than granted or denied."),
				new ToolErrorCodeContract("invalid-duration",
					"duration_ms must be a non-negative value when supplied."),
				new ToolErrorCodeContract("invalid-session-id",
					"session_id must be 1-128 characters of letters, digits, '.', '_', ':' or '-'."),
				new ToolErrorCodeContract("field-too-long",
					"A scalar metadata field (coding_agent or plugin_version) exceeds the 64-character limit.")
			]),
			[],
			[
				new ToolContractDefaultValue(TelemetryConsentFieldName, "omitted after first run", "Consent is persisted locally after the first granted or denied value.")
			],
			[
				Example("Store a Business Plan generated event after consent", new Dictionary<string, object?> {
					["session_id"] = "018f6e4a-0000-7000-9000-000000000001",
					[EventNameFieldName] = "business_plan_generated",
					["coding_agent"] = "Codex",
					["plugin_version"] = "0.1.0"
				})
			],
			Flow([toolName], flowNotes),
			[],
			[],
			[
				new ToolAntiPattern("Adding custom telemetry fields", "The send-telemetry tool accepts only the documented product telemetry fields listed in this contract (including the optional duration_ms); any other field is rejected as unsupported-fields.")
			]);
	}

	private static ToolContractDefinition BuildGuidanceGet() {
		return new ToolContractDefinition(
			GuidanceGetTool.ToolName,
			"Returns canonical clio MCP guidance text by stable guide name so clients can consume workflows and page-authoring rules without fetching docs:// resources directly.",
			new ToolInputSchemaContract(
				["name"],
				[
					Field("name", StringType, "Stable guidance name. Known values include app-modeling, data-bindings, existing-app-maintenance, dataforge-orchestration, page-modification, page-schema-handlers, page-schema-creatio-devkit-common, page-schema-validators, business-rules, esq, and esq-filters. The list is illustrative, not exhaustive; a failed lookup returns the full set in available-guides.")
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, ToolSucceededDescription),
				Field("guidance", ObjectType, "Resolved guidance article with name, uri, mime-type, description, and text."),
				Field(ErrorFieldName, StringType, FailureMessageDescription),
				Field("available-guides", ArrayType, "Known guidance names returned on lookup failure.")),
			CommonErrorContract,
			[],
			[],
			[
				Example("Read handler authoring guidance", new Dictionary<string, object?> {
					["name"] = "page-schema-handlers"
				}),
				Example("Read validator authoring guidance", new Dictionary<string, object?> {
					["name"] = "page-schema-validators"
				}),
				Example("Read general page modification guidance", new Dictionary<string, object?> {
					["name"] = "page-modification"
				}),
				Example("Read SDK common page-schema guidance", new Dictionary<string, object?> {
					["name"] = "page-schema-creatio-devkit-common"
				}),
				Example("Read canonical existing-app maintenance guidance", new Dictionary<string, object?> {
					["name"] = "existing-app-maintenance"
				})
			],
			new ToolFlowHint(
				[
					GuidanceGetTool.ToolName
				],
				"Call this tool before workflows that require canonical clio MCP guidance text, especially when page prompts or app prompts reference a named guide."),
			[],
			[]);
	}

	private static ToolContractDefinition BuildExecuteEsq() {
		return new ToolContractDefinition(
			ExecuteEsqTool.ToolName,
			"Runs a raw EntitySchemaQuery (ESQ) SelectQuery against a Creatio environment via the DataService SelectQuery endpoint and returns the rows. The primary way to read Creatio data with a raw ESQ query; also used to confirm an ESQ filter is valid before saving it into a page. ESQ is a proprietary format: call get-guidance for 'esq' and 'esq-filters' before composing a query rather than guessing the shape. A requested columns.items alias whose columnPath does not resolve fails the call with success:false instead of silently omitting that column from the rows.",
			new ToolInputSchemaContract(
				[QueryFieldName, EnvironmentNameFieldName],
				[
					Field(QueryFieldName, ObjectType, "Raw ESQ SelectQuery object with 'rootSchemaName' and usually 'columns' (an 'items' map) and/or 'filters'. Read the 'esq' guidance for the SelectQuery envelope and 'esq-filters' for the filter tree."),
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription),
					Field("timeout", NumberType, "Request timeout in milliseconds (1000-120000, default 30000).")
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, ToolSucceededDescription),
				Field(CountFieldName, NumberType, "Number of rows returned."),
				Field("rows", ArrayType, "Rows returned by the SelectQuery."),
				Field(ErrorFieldName, StringType, FailureMessageDescription)),
			CommonErrorContract,
			[],
			[],
			[
				Example("Count all contacts", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[QueryFieldName] = new Dictionary<string, object?> {
						["rootSchemaName"] = ExampleContactSchemaName,
						["operationType"] = 0,
						["allColumns"] = false,
						["columns"] = new Dictionary<string, object?> {
							["items"] = new Dictionary<string, object?> {
								["ContactCount"] = new Dictionary<string, object?> {
									["expression"] = new Dictionary<string, object?> {
										["expressionType"] = 1,
										["functionType"] = 2,
										["aggregationType"] = 1,
										["aggregationEvalType"] = 2,
										["functionArgument"] = new Dictionary<string, object?> {
											["expressionType"] = 0,
											["columnPath"] = "Id"
										}
									}
								}
							}
						}
					}
				})
			],
			Flow(
				[
					GuidanceGetTool.ToolName,
					ExecuteEsqTool.ToolName
				],
				"Read the 'esq' and 'esq-filters' guidance with get-guidance before composing a SelectQuery, then run it with execute-esq."),
			[],
			[],
			null,
			[
				"Read the 'esq' and 'esq-filters' guidance via get-guidance before composing a query — ESQ is a proprietary format and hand-guessed enum values, expression shapes, and date encodings fail."
			]);
	}

	private static ToolContractDefinition BuildApplicationCreate() {
		return new ToolContractDefinition(
			ApplicationCreateTool.ApplicationCreateToolName,
			"Creates a Creatio application and returns installed application identity plus the created application context envelope and Data Forge enrichment diagnostics.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, "name", "code", TemplateCodeFieldName],
				[
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription),
					Field("name", StringType, "Application display name."),
					Field("code", StringType, "Application code (business-meaningful part; SchemaNamePrefix is auto-applied by clio)."),
					Field(TemplateCodeFieldName, StringType, "Technical template code such as AppFreedomUI."),
					Field(IconBackgroundFieldName, StringType, "Optional hex color in #RRGGBB format from the Freedom UI palette. A random palette color is assigned when omitted."),
					Field(DescriptionFieldName, StringType, "Optional application description."),
					Field(IconIdFieldName, StringType, "Optional icon GUID or 'auto'."),
					Field("client-type-id", StringType, "Optional client type identifier."),
					Field("optional-template-data-json", StringType, "Optional JSON object for advanced template configuration."),
					Field(WithMobilePagesFieldName, BooleanType, "Create mobile pages (_MobileFormPage, _MobileListPage) for the main entity in addition to web pages. Set false for a web-only application.")
				],
				Validators: [
					new ToolContractValidator(
						"forbid-fields",
						InvalidWorkflowShapeCode,
						Fields: [
							TitleLocalizationsFieldName,
							DescriptionLocalizationsFieldName,
							"name-localizations",
							"app-section-description-localizations",
							"titleLocalizations",
							"descriptionLocalizations",
							"nameLocalizations",
							"appSectionDescriptionLocalizations"
						],
						Context: "create-app stays scalar-only; localized captions belong to follow-up schema tools.")
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, ToolSucceededDescription),
				Field(PackageUIdFieldName, StringType, PrimaryPackageIdentifierDescription),
				Field(PackageNameFieldName, StringType, PrimaryPackageNameDescription),
				Field("canonical-main-entity-name", StringType, "Canonical main entity name created by the app template. Pass directly to sync-schemas as the mutation target. Do not create a new entity via create-app-section unless a second independent section is required."),
				Field(ApplicationIdFieldName, StringType, InstalledApplicationIdentifierDescription),
				Field(ApplicationNameFieldName, StringType, InstalledApplicationDisplayNameDescription),
				Field(ApplicationCodeFieldName, StringType, InstalledApplicationCodeDescription),
				Field(ApplicationVersionFieldName, StringType, InstalledApplicationVersionDescription),
				Field("entities", ArrayType, "Application entities. Each entity includes `virtual`, and each entity `columns` item carries a vocabulary unified with the sync-schemas write surfaces so it round-trips without translation: `name`, `caption`, canonical `type` (with `data-value-type` kept as a legacy alias), canonical `reference-schema-name` (with `reference-schema` kept as a legacy alias), and `required`. Send a column back to sync-schemas update-entity by adding the `action` verb."),
				Field(PagesFieldName, ArrayType, "Primary-package Freedom UI pages using list-pages item shape (`schema-name`, `uId`, `packageName`, `parentSchemaName`)."),
				Field("schema-name-prefix", StringType, "Active SchemaNamePrefix resolved from the environment. Use as the prefix for all subsequent custom schema codes (lookups, columns, supporting entities). Empty string means no prefix is configured."),
				Field("dataforge", ObjectType, "Optional Data Forge enrichment diagnostics including health/status/coverage, warnings, and a compact context-summary."),
				Field(ErrorFieldName, StringType, FailureMessageDescription)
			),
			CommonErrorContract,
			[
				Alias(ParameterScope, "code", AppCodeFieldName, RejectedStatus, $"Use 'code' instead of '{AppCodeFieldName}'."),
				Alias(ParameterScope, "name", AppNameFieldName, RejectedStatus, $"Use 'name' instead of '{AppNameFieldName}'."),
				Alias(ParameterScope, TemplateCodeFieldName, "templateCode", RejectedStatus, $"Use '{TemplateCodeFieldName}' instead of 'templateCode'."),
				Alias(ParameterScope, IconBackgroundFieldName, "iconBackground", RejectedStatus, $"Use '{IconBackgroundFieldName}' instead of 'iconBackground'.")
			],
			[
				Default(TemplateCodeFieldName, "AppFreedomUI", "Default template for standard Freedom UI app shells."),
				Default(WithMobilePagesFieldName, "true", "Create both web and mobile pages unless the caller explicitly disables mobile pages.")
			],
			[
				Example("Create a new Freedom UI application with the minimal top-level payload", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					["name"] = "Task App",
					["code"] = ExamplePackageName,
					[TemplateCodeFieldName] = "AppFreedomUI",
					[IconBackgroundFieldName] = "#1F5F8B"
				})
			],
			Flow(
				[
					ApplicationCreateTool.ApplicationCreateToolName,
					SchemaSyncTool.ToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName
				],
				"Use this direct create flow for simple greenfield app shells. create-app performs built-in Data Forge enrichment, then sync-schemas handles entity mutations, and get-app-info refreshes the resulting app context. Do NOT call create-app-section directly after create-app for a single-section app — use canonical-main-entity-name from the response as the sync-schemas target instead."),
			[
				Flow(
					[
						ApplicationGetListTool.ApplicationGetListToolName,
						ApplicationGetInfoTool.ApplicationGetInfoToolName
					],
					"Fallback when the app already exists and the flow must switch to existing-app discovery.")
			],
			[],
			[
				new ToolAntiPattern(
					"create-app → create-app-section → delete-app-section",
					"create-app always creates a starter section with canonical-main-entity-name. Calling create-app-section immediately after wastes two round-trips and requires a cleanup delete. Use sync-schemas on canonical-main-entity-name instead.")
			]);
	}

	private static ToolContractDefinition BuildApplicationSectionCreate() {
		return new ToolContractDefinition(
			ApplicationSectionCreateTool.ApplicationSectionCreateToolName,
			"Creates a section inside an existing installed application and returns structured section, entity, and page readback data.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, ApplicationCodeFieldName, CaptionFieldName],
				[
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription),
					Field(ApplicationCodeFieldName, StringType, InstalledApplicationCodeDescription),
					Field(CaptionFieldName, StringType, "Section caption."),
					Field(DescriptionFieldName, StringType, "Optional section description."),
					Field("entity-schema-name", StringType, "Optional existing entity schema name. When provided, the section reuses that entity. The object must exist (validated before creation, with a clear error otherwise); several sections may target the same object, so reuse is allowed."),
					Field("code", StringType, "Optional explicit section code (Latin identifier). When omitted, the code is generated from the caption; required when the caption has no Latin letters or digits — for a non-Latin caption such as 'Контакти' pass an English code like 'Contacts'."),
					Field(WithMobilePagesFieldName, BooleanType, "Create mobile pages in addition to web pages.")
				],
				Validators: [
					new ToolContractValidator(
						"forbid-fields",
						InvalidWorkflowShapeCode,
						Fields: [
							TitleLocalizationsFieldName,
							DescriptionLocalizationsFieldName,
							"caption-localizations",
							"name-localizations",
							"titleLocalizations",
							"descriptionLocalizations",
							"captionLocalizations",
							"nameLocalizations"
						],
						Context: "create-app-section stays scalar-only; localized captions belong to follow-up schema tools.")
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, ToolSucceededDescription),
				Field(PackageUIdFieldName, StringType, PrimaryPackageIdentifierDescription),
				Field(PackageNameFieldName, StringType, PrimaryPackageNameDescription),
				Field(ApplicationIdFieldName, StringType, InstalledApplicationIdentifierDescription),
				Field(ApplicationNameFieldName, StringType, InstalledApplicationDisplayNameDescription),
				Field(ApplicationCodeFieldName, StringType, InstalledApplicationCodeDescription),
				Field(ApplicationVersionFieldName, StringType, InstalledApplicationVersionDescription),
				Field("section", ObjectType, "Created section metadata."),
				Field(EntityFieldName, ObjectType, "Created or targeted entity metadata when available."),
				Field(PagesFieldName, ArrayType, "Created page summaries using list-pages item shape (`schema-name`, `uId`, `packageName`, `parentSchemaName`)."),
				Field(ErrorFieldName, StringType, FailureMessageDescription),
				Field("error-class", StringType, "Failure classification, present on classified errors only: 'transport' (request never reached Creatio — retry is safe), 'creatio-timeout' (no response within the budget — side effects unknown, verify with list-app-sections before retrying), 'contention' (insert aborted without a detailed reason — may be parallel creation in one app OR a server-side rejection unrelated to concurrency; no section created (verified); run list-app-sections, create sections one at a time if you were creating them concurrently (clio serializes and auto-retries once), and if a single sequential create still fails treat it as server-side), 'server-error' (Creatio rejected the operation with a real, detailed reason — fix inputs or server state first)."),
				Field("section-created", StringType, "Side-effect verification outcome on classified errors: 'true', 'false', 'unknown', or 'in-progress'. 'in-progress' is not a verification outcome — it means the section is still being created server-side after the MCP response deadline returned early; do NOT retry create-app-section, poll list-app-sections / get-app-info until the section appears."),
				Field("retry-guidance", StringType, "Actionable next step for the classified failure. Follow it instead of blind retries.")
			),
			CommonErrorContract,
			[
				Alias(ParameterScope, ApplicationCodeFieldName, SelectorCodeFieldName, RejectedStatus, $"Use '{ApplicationCodeFieldName}' instead of '{SelectorCodeFieldName}'."),
				Alias(ParameterScope, ApplicationCodeFieldName, AppCodeFieldName, RejectedStatus, $"Use '{ApplicationCodeFieldName}' instead of '{AppCodeFieldName}'."),
				Alias(ParameterScope, ApplicationCodeFieldName, ApplicationIdFieldName, RejectedStatus, $"Use '{ApplicationCodeFieldName}' instead of '{ApplicationIdFieldName}'."),
				Alias(ParameterScope, "entity-schema-name", "use-existing-entity-schema", RejectedStatus, "Use 'entity-schema-name' alone to reuse an existing entity; the boolean flag is not supported.")
			],
			[
				Default(WithMobilePagesFieldName, "true", "Create both web and mobile pages unless the caller explicitly disables mobile pages.")
			],
			[
				Example("Create a new-object section in an existing app", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[ApplicationCodeFieldName] = ExamplePackageName,
					[CaptionFieldName] = "Orders",
					[DescriptionFieldName] = "Order processing workspace"
				}),
				Example("Create a section from an existing entity with mobile pages", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[ApplicationCodeFieldName] = ExamplePackageName,
					[CaptionFieldName] = "Task statuses",
					["entity-schema-name"] = ExampleTaskStatusSchemaName,
					[WithMobilePagesFieldName] = true
				})
			],
			Flow(
				[
					ApplicationGetListTool.ApplicationGetListToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName,
					ApplicationSectionCreateTool.ApplicationSectionCreateToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName
				],
				"Use application discovery and inspection first, then create the section, then refresh app context once for verification. Do NOT use immediately after create-app for the primary section — that entity already exists under canonical-main-entity-name. Use only when adding a second or subsequent section to an existing app."),
			[
				Flow(
					[
						ApplicationGetInfoTool.ApplicationGetInfoToolName,
						ApplicationSectionCreateTool.ApplicationSectionCreateToolName
					],
					"Use this shorter flow when the target existing app is already known and inspected.")
			],
			[]);
	}

	private static ToolContractDefinition BuildApplicationSectionUpdate() {
		return new ToolContractDefinition(
			ApplicationSectionUpdateTool.ApplicationSectionUpdateToolName,
			"Updates metadata of an existing installed application section and returns structured section readback data before and after the update.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, ApplicationCodeFieldName, SectionCodeFieldName],
				[
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription),
					Field(ApplicationCodeFieldName, StringType, InstalledApplicationCodeDescription),
					Field(SectionCodeFieldName, StringType, "Existing section code inside the installed application."),
					Field(CaptionFieldName, StringType, "Optional updated section caption."),
					Field(DescriptionFieldName, StringType, "Optional updated section description."),
					Field(IconIdFieldName, StringType, "Optional updated icon GUID."),
					Field(IconBackgroundFieldName, StringType, "Optional updated icon background color in #RRGGBB format.")
				],
				Validators: [
					new ToolContractValidator(
						"forbid-fields",
						InvalidWorkflowShapeCode,
						Fields: [
							TitleLocalizationsFieldName,
							DescriptionLocalizationsFieldName,
							"caption-localizations",
							"name-localizations",
							"titleLocalizations",
							"descriptionLocalizations",
							"captionLocalizations",
							"nameLocalizations"
						],
						Context: "update-app-section stays scalar-only; localized captions belong to follow-up schema tools.")
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, ToolSucceededDescription),
				Field(PackageUIdFieldName, StringType, PrimaryPackageIdentifierDescription),
				Field(PackageNameFieldName, StringType, PrimaryPackageNameDescription),
				Field(ApplicationIdFieldName, StringType, InstalledApplicationIdentifierDescription),
				Field(ApplicationNameFieldName, StringType, InstalledApplicationDisplayNameDescription),
				Field(ApplicationCodeFieldName, StringType, InstalledApplicationCodeDescription),
				Field(ApplicationVersionFieldName, StringType, InstalledApplicationVersionDescription),
				Field("previous-section", ObjectType, "Section metadata before the update."),
				Field("section", ObjectType, "Section metadata after the update."),
				Field(ErrorFieldName, StringType, FailureMessageDescription)
			),
			CommonErrorContract,
			[
				Alias(ParameterScope, ApplicationCodeFieldName, SelectorCodeFieldName, RejectedStatus, $"Use '{ApplicationCodeFieldName}' instead of '{SelectorCodeFieldName}'."),
				Alias(ParameterScope, ApplicationCodeFieldName, AppCodeFieldName, RejectedStatus, $"Use '{ApplicationCodeFieldName}' instead of '{AppCodeFieldName}'."),
				Alias(ParameterScope, SectionCodeFieldName, "sectionCode", RejectedStatus, "Use 'section-code' instead of 'sectionCode'."),
				Alias(ParameterScope, IconIdFieldName, "iconId", RejectedStatus, "Use 'icon-id' instead of 'iconId'."),
				Alias(ParameterScope, IconBackgroundFieldName, "iconBackground", RejectedStatus, "Use 'icon-background' instead of 'iconBackground'.")
			],
			[],
			[
				Example("Update a broken section heading with a plain-text caption", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[ApplicationCodeFieldName] = ExamplePackageName,
					[SectionCodeFieldName] = "UsrOrders",
					[CaptionFieldName] = "Orders"
				}),
				Example("Update section description and icon metadata", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[ApplicationCodeFieldName] = ExamplePackageName,
					[SectionCodeFieldName] = "UsrOrders",
					[DescriptionFieldName] = "Order processing workspace",
					[IconIdFieldName] = "11111111-1111-1111-1111-111111111111",
					[IconBackgroundFieldName] = "#1F5F8B"
				})
			],
			Flow(
				[
					ApplicationGetListTool.ApplicationGetListToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName,
					ApplicationSectionUpdateTool.ApplicationSectionUpdateToolName
				],
				"Use application discovery and inspection first, then update the target section metadata with a partial top-level payload."),
			[
				Flow(
					[
						ApplicationGetInfoTool.ApplicationGetInfoToolName,
						ApplicationSectionUpdateTool.ApplicationSectionUpdateToolName
					],
					"Use this shorter flow when the target app is already known and inspected.")
			],
			[]);
	}

	private static ToolContractDefinition BuildApplicationSectionDelete() {
		return new ToolContractDefinition(
			ApplicationSectionDeleteTool.ApplicationSectionDeleteToolName,
			"Deletes a section from an existing installed application and returns structured readback of the deleted section.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, ApplicationCodeFieldName, SectionCodeFieldName],
				[
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription),
					Field(ApplicationCodeFieldName, StringType, InstalledApplicationCodeDescription),
					Field(SectionCodeFieldName, StringType, "Existing section code inside the installed application."),
					Field(DeleteEntitySchemaFieldName, BooleanType,
						"When true, also deletes the entity schema record. Requires explicit opt-in. WARNING: destructive and irreversible. Omit or set to false to keep the entity schema and its data intact.")
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, ToolSucceededDescription),
				Field(PackageUIdFieldName, StringType, PrimaryPackageIdentifierDescription),
				Field(PackageNameFieldName, StringType, PrimaryPackageNameDescription),
				Field(ApplicationIdFieldName, StringType, InstalledApplicationIdentifierDescription),
				Field(ApplicationNameFieldName, StringType, InstalledApplicationDisplayNameDescription),
				Field(ApplicationCodeFieldName, StringType, InstalledApplicationCodeDescription),
				Field(ApplicationVersionFieldName, StringType, InstalledApplicationVersionDescription),
				Field("deleted-section", ObjectType, "Deleted section metadata."),
				Field(ErrorFieldName, StringType, FailureMessageDescription)
			),
			CommonErrorContract,
			[
				Alias(ParameterScope, ApplicationCodeFieldName, SelectorCodeFieldName, RejectedStatus, $"Use '{ApplicationCodeFieldName}' instead of '{SelectorCodeFieldName}'."),
				Alias(ParameterScope, ApplicationCodeFieldName, AppCodeFieldName, RejectedStatus, $"Use '{ApplicationCodeFieldName}' instead of '{AppCodeFieldName}'."),
				Alias(ParameterScope, SectionCodeFieldName, "sectionCode", RejectedStatus, "Use 'section-code' instead of 'sectionCode'.")
			],
			[],
			[
				Example("Delete a section from an existing app", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[ApplicationCodeFieldName] = ExamplePackageName,
					[SectionCodeFieldName] = "UsrOrders"
				})
			],
			Flow(
				[
					ApplicationGetListTool.ApplicationGetListToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName,
					ApplicationSectionDeleteTool.ApplicationSectionDeleteToolName
				],
				"Use application discovery and inspection first, then delete the target section."),
			[
				Flow(
					[
						ApplicationGetInfoTool.ApplicationGetInfoToolName,
						ApplicationSectionDeleteTool.ApplicationSectionDeleteToolName
					],
					"Use this shorter flow when the target app is already known and inspected.")
			],
			[]);
	}

	private static ToolContractDefinition BuildApplicationSectionGetList() {
		return new ToolContractDefinition(
			ApplicationSectionGetListTool.ApplicationSectionGetListToolName,
			"Returns the list of sections of an existing installed application and their metadata.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, ApplicationCodeFieldName],
				[
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription),
					Field(ApplicationCodeFieldName, StringType, InstalledApplicationCodeDescription)
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, ToolSucceededDescription),
				Field(PackageUIdFieldName, StringType, PrimaryPackageIdentifierDescription),
				Field(PackageNameFieldName, StringType, PrimaryPackageNameDescription),
				Field(ApplicationIdFieldName, StringType, InstalledApplicationIdentifierDescription),
				Field(ApplicationNameFieldName, StringType, InstalledApplicationDisplayNameDescription),
				Field(ApplicationCodeFieldName, StringType, InstalledApplicationCodeDescription),
				Field(ApplicationVersionFieldName, StringType, InstalledApplicationVersionDescription),
				Field("sections", ArrayType, "List of section metadata objects in the application."),
				Field(ErrorFieldName, StringType, FailureMessageDescription)
			),
			CommonErrorContract,
			[
				Alias(ParameterScope, ApplicationCodeFieldName, SelectorCodeFieldName, RejectedStatus, $"Use '{ApplicationCodeFieldName}' instead of '{SelectorCodeFieldName}'."),
				Alias(ParameterScope, ApplicationCodeFieldName, AppCodeFieldName, RejectedStatus, $"Use '{ApplicationCodeFieldName}' instead of '{AppCodeFieldName}'.")
			],
			[],
			[
				Example("List sections of an existing app", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[ApplicationCodeFieldName] = ExamplePackageName
				})
			],
			Flow(
				[
					ApplicationGetListTool.ApplicationGetListToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName,
					ApplicationSectionGetListTool.ApplicationSectionGetListToolName
				],
				"Use application discovery and inspection first, then list sections."),
			[
				Flow(
					[
						ApplicationGetInfoTool.ApplicationGetInfoToolName,
						ApplicationSectionGetListTool.ApplicationSectionGetListToolName
					],
					"Use this shorter flow when the target app is already known.")
			],
			[]);
	}

	private static ToolContractDefinition BuildDataForgeStatus() {
		return BuildDataForgeContract(
			new DataForgeContractDescriptor {
				ToolName = DataForgeTool.DataForgeStatusToolName,
				Description = "Checks whether Data Forge is ready to provide schema, lookup, relation, and maintenance context for a Creatio environment. " + DataForgePlatformRequirementDescription,
				InputFields = DataForgeConnectionFields(),
				OutputFields = DataForgeEnvelopeFields(
					"Health probe correlation identifier.",
					Field("health", ObjectType, "Health payload with liveness/readiness and Data Forge readiness flags."),
					Field(StatusFieldName, ObjectType, "Maintenance status payload.")),
				Examples = [
					Example("Check Data Forge status for a configured environment", new Dictionary<string, object?> {
						[EnvironmentNameFieldName] = ExampleEnvironmentName
					})
				],
				PreferredFlow = Flow([DataForgeTool.DataForgeStatusToolName], "Use before longer schema-planning workflows when callers need to know whether Data Forge discovery is available.")
			});
	}

	private static ToolContractDefinition BuildDataForgeFindTables() {
		return BuildDataForgeContract(
			new DataForgeContractDescriptor {
				ToolName = DataForgeTool.DataForgeFindTablesToolName,
				Description = "Finds existing Creatio tables that semantically match a business concept, so callers can reuse or compare schemas before creating new ones. " + DataForgePlatformRequirementDescription,
				RequiredFields = [QueryFieldName],
				InputFields = DataForgeConnectionFields(
					Field(QueryFieldName, StringType, "Table search term."),
					Field(LimitFieldName, NumberType, "Optional result limit.")),
				OutputFields = DataForgeEnvelopeFields(
					QueryCorrelationIdentifierDescription,
					Field("similar-tables", ArrayType, "Similar table results with `name`, `caption`, and `description`.")),
				Examples = [
					Example("Find Contact-like tables for a configured environment", new Dictionary<string, object?> {
						[QueryFieldName] = "contact",
						[EnvironmentNameFieldName] = ExampleEnvironmentName
					})
				],
				PreferredFlow = Flow([DataForgeTool.DataForgeFindTablesToolName], "Use when app-modeling or schema discovery needs table reuse hints from semantic search."),
				FallbackFlow = [
					Flow(
						[DataForgeTool.DataForgeStatusToolName, DataForgeTool.DataForgeFindTablesToolName],
						"Fallback when callers want to confirm Data Forge readiness before semantic table search.")
				]
			});
	}

	private static ToolContractDefinition BuildDataForgeFindLookups() {
		return BuildDataForgeContract(
			new DataForgeContractDescriptor {
				ToolName = DataForgeTool.DataForgeFindLookupsToolName,
				Description = "Finds lookup values and lookup schemas that match a requested business value, useful for resolving lookup references before writing data bindings. " + DataForgePlatformRequirementDescription,
				RequiredFields = [QueryFieldName],
				InputFields = DataForgeConnectionFields(
					Field(QueryFieldName, StringType, "Lookup search term."),
					Field(SchemaNameFieldName, StringType, "Optional lookup schema name filter."),
					Field(LimitFieldName, NumberType, "Optional result limit.")),
				OutputFields = DataForgeEnvelopeFields(
					QueryCorrelationIdentifierDescription,
					Field("similar-lookups", ArrayType, "Similar lookup results with `lookup-id`, `schema-name`, `value`, and `score`.")),
				Aliases = [
					..DataForgeConnectionAliases(),
					SchemaNameParameterAlias()
				],
				Examples = [
					Example("Find status-like lookups for a configured environment", new Dictionary<string, object?> {
						[QueryFieldName] = "status",
						[EnvironmentNameFieldName] = ExampleEnvironmentName
					})
				],
				PreferredFlow = Flow([DataForgeTool.DataForgeFindLookupsToolName], "Use when app-modeling needs lookup reuse hints instead of creating a new lookup blindly."),
				FallbackFlow = [
					Flow(
						[DataForgeTool.DataForgeStatusToolName, DataForgeTool.DataForgeFindLookupsToolName],
						"Fallback when callers want readiness confirmation before semantic lookup search.")
				]
			});
	}

	private static ToolContractDefinition BuildDataForgeGetRelations() {
		return BuildDataForgeContract(
			new DataForgeContractDescriptor {
				ToolName = DataForgeTool.DataForgeGetRelationsToolName,
				Description = "Finds known relationship paths between two Creatio tables to help model references or understand existing entity links. " + DataForgePlatformRequirementDescription,
				RequiredFields = ["source-table", "target-table"],
				InputFields = DataForgeConnectionFields(
					Field("source-table", StringType, "Source table name."),
					Field("target-table", StringType, "Target table name."),
					Field(LimitFieldName, NumberType, "Optional relation path limit.")),
				OutputFields = DataForgeEnvelopeFields(
					QueryCorrelationIdentifierDescription,
					Field("relations", ArrayType, "Resolved relation paths as cypher-style strings.")),
				Examples = [
					Example("Read Contact-to-Account relations for a configured environment", new Dictionary<string, object?> {
						["source-table"] = ExampleContactSchemaName,
						["target-table"] = ExampleAccountSchemaName,
						[EnvironmentNameFieldName] = ExampleEnvironmentName
					})
				],
				PreferredFlow = Flow([DataForgeTool.DataForgeGetRelationsToolName], "Use when entity design needs semantic relation hints between candidate tables."),
				FallbackFlow = [
					Flow(
						[DataForgeTool.DataForgeFindTablesToolName, DataForgeTool.DataForgeGetRelationsToolName],
						"Fallback when callers first need similar table discovery before selecting the relation pair.")
				]
			});
	}

	private static ToolContractDefinition BuildDataForgeGetTableColumns() {
		return BuildDataForgeContract(
			new DataForgeContractDescriptor {
				ToolName = DataForgeTool.DataForgeGetTableColumnsToolName,
				Description = "Returns the logical columns of a Creatio table, including captions, data types, required flags, and lookup targets. " + DataForgePlatformRequirementDescription,
				RequiredFields = ["table-name"],
				InputFields = DataForgeConnectionFields(
					Field("table-name", StringType, "Target runtime entity schema name.")),
				OutputFields = DataForgeEnvelopeFields(
					QueryCorrelationIdentifierDescription,
					Field(ColumnsFieldName, ArrayType, "Runtime column projections with `name`, `caption`, `description`, `data-type`, `required`, and `reference-schema-name`.")),
				Examples = [
					Example("Read Contact runtime columns for a configured environment", new Dictionary<string, object?> {
						["table-name"] = ExampleContactSchemaName,
						[EnvironmentNameFieldName] = ExampleEnvironmentName
					})
				],
				PreferredFlow = Flow([DataForgeTool.DataForgeGetTableColumnsToolName], "Use after table discovery when callers need column hints for a selected Creatio table."),
				FallbackFlow = [
					Flow(
						[DataForgeTool.DataForgeFindTablesToolName, DataForgeTool.DataForgeGetTableColumnsToolName],
						"Fallback when callers first need similar table discovery before reading columns.")
				]
			});
	}

	private static ToolContractDefinition BuildDataForgeContext() {
		return BuildDataForgeContract(
			new DataForgeContractDescriptor {
				ToolName = DataForgeTool.DataForgeContextToolName,
				Description = "Builds a compact Data Forge context package for planning schema work: similar tables, lookup matches, relation paths, table columns, and readiness status. " + DataForgePlatformRequirementDescription,
				InputFields = DataForgeConnectionFields(
					Field("requirement-summary", StringType, "Optional free-text summary used when candidate-terms are omitted."),
					Field("candidate-terms", ArrayType, "Optional table-search terms."),
					Field("lookup-hints", ArrayType, "Optional lookup-search hints."),
					Field("relation-pairs", ArrayType, "Optional source-target table pairs.")),
				OutputFields = DataForgeEnvelopeFields(
					"Health probe correlation identifier.",
					Field("health", ObjectType, "Health payload with liveness/readiness and Data Forge readiness flags."),
					Field(StatusFieldName, ObjectType, "Maintenance status payload."),
					Field("similar-tables", ArrayType, "Similar table results."),
					Field("similar-lookups", ArrayType, "Similar lookup results."),
					Field("relations", ObjectType, "Resolved relation paths keyed by source-target pair."),
					Field(ColumnsFieldName, ObjectType, "Resolved runtime column projections keyed by table name."),
					Field("coverage", ObjectType, "Coverage flags for health, tables, lookups, relations, and table-columns.")),
				Examples = [
					Example("Aggregate app-modeling context for a configured environment", new Dictionary<string, object?> {
						["requirement-summary"] = "Task registry for customer follow-up",
						["candidate-terms"] = new[] { "task", "activity" },
						["lookup-hints"] = new[] { StatusFieldName },
						[EnvironmentNameFieldName] = ExampleEnvironmentName
					})
				],
				PreferredFlow = Flow([DataForgeTool.DataForgeContextToolName], "Use when one aggregated Data Forge read is preferable to multiple table/lookup/relation/column calls."),
				FallbackFlow = [
					Flow(
						[
							DataForgeTool.DataForgeStatusToolName,
							DataForgeTool.DataForgeContextToolName
						],
						"Fallback when callers want to check Data Forge readiness explicitly before aggregation.")
				]
			});
	}

	private static ToolContractDefinition BuildDataForgeInitialize() {
		return BuildDataForgeContract(
			new DataForgeContractDescriptor {
				ToolName = DataForgeTool.DataForgeInitializeToolName,
				Description = "Schedules a full Data Forge initialization when the index is missing, stale, or not ready. " + DataForgePlatformRequirementDescription,
				InputFields = DataForgeConnectionFields(),
				OutputFields = DataForgeEnvelopeFields(
					"Mutation correlation identifier when available.",
					Field(StatusFieldName, ObjectType, "Maintenance scheduling status payload.")),
				Examples = [
					Example("Schedule Data Forge initialization for a configured environment", new Dictionary<string, object?> {
						[EnvironmentNameFieldName] = ExampleEnvironmentName
					})
				],
				PreferredFlow = Flow([DataForgeTool.DataForgeInitializeToolName], "Use only for explicit Data Forge remediation or initial maintenance setup.")
			});
	}

	private static ToolContractDefinition BuildDataForgeUpdate() {
		return BuildDataForgeContract(
			new DataForgeContractDescriptor {
				ToolName = DataForgeTool.DataForgeUpdateToolName,
				Description = "Schedules a Data Forge index refresh after schema changes or when discovery results appear stale. " + DataForgePlatformRequirementDescription,
				InputFields = DataForgeConnectionFields(),
				OutputFields = DataForgeEnvelopeFields(
					"Mutation correlation identifier when available.",
					Field(StatusFieldName, ObjectType, "Maintenance scheduling status payload.")),
				Examples = [
					Example("Schedule a Data Forge update for a configured environment", new Dictionary<string, object?> {
						[EnvironmentNameFieldName] = ExampleEnvironmentName
					})
				],
				PreferredFlow = Flow([DataForgeTool.DataForgeUpdateToolName], "Use only for explicit Data Forge remediation or refresh maintenance.")
			});
	}

	private static ToolContractDefinition BuildODataRead() {
		return new ToolContractDefinition(
			ODataReadTool.ToolName,
			"Reads Creatio records through OData v4. Use this to query records, resolve lookup primary values, verify records by Id, or inspect selected fields.",
			new ToolInputSchemaContract(
				[EntityFieldName, EnvironmentNameFieldName],
				[
					Field(EntityFieldName, StringType, "Creatio OData entity set name, usually the referenced lookup schema name such as Contact, Account, or a custom lookup schema."),
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription),
					Field(FiltersFieldName, ObjectType, "Structured filter. all conditions join with AND; any conditions join with OR. GUID values in Id-suffixed fields and navigation paths ending in Id are automatically unquoted. Use lookup traversal paths such as Account/Id when filtering records by lookup primary value. Example: { \"all\": [{ \"field\": \"Account/Id\", \"op\": \"eq\", \"value\": \"8ecab4a1-0ca3-4515-9399-efe0a19390bd\" }] }."),
					Field(SelectFieldName, ArrayType, "Fields to return. Use [\"Id\", \"Name\"] when resolving lookup records by display value."),
					Field("expand", ArrayType, "Navigation properties to expand."),
					Field("order-by", StringType, "OData $orderby clause, for example CreatedOn desc or Name asc."),
					Field("top", NumberType, "Maximum number of records to return, 1-100. Default: 25. An out-of-range top (including 0 or negative) is rejected with success:false, never silently changed.")
				],
				Validators: [
					new ToolContractValidator(LimitFieldName, "invalid-top", "top",
						Context: "top must be between 1 and 100; omitting it uses the default of 25, and an out-of-range value (including 0 or negative) is rejected with success:false.")
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, "Whether the OData read succeeded."),
				Field(ErrorFieldName, StringType, FailureMessageDescription),
				Field(CountFieldName, NumberType, "Number of records returned."),
				Field(ValueFieldName, ArrayType, "OData value array or single entity response."),
				Field("next-link", StringType, "OData next-link URL when more records are available.")
			),
			CommonErrorContract,
			[],
			[],
			[
				Example("Resolve a lookup row by display value", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[EntityFieldName] = ExampleTaskStatusSchemaName,
					[FiltersFieldName] = new Dictionary<string, object?> {
						["all"] = new object[] {
							new Dictionary<string, object?> { [FieldFieldName] = "Name", ["op"] = "eq", [ValueFieldName] = "In Progress" }
						}
					},
					[SelectFieldName] = new[] { "Id", "Name" },
					["top"] = 5
				}),
				Example("Verify a known lookup row exists by Id", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[EntityFieldName] = ExampleContactSchemaName,
					[FiltersFieldName] = new Dictionary<string, object?> {
						["all"] = new object[] {
							new Dictionary<string, object?> { [FieldFieldName] = "Id", ["op"] = "eq", [ValueFieldName] = ExampleLookupValueId }
						}
					},
					[SelectFieldName] = new[] { "Id" },
					["top"] = 1
				}),
				Example("Query records with multiple fields and ordering", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[EntityFieldName] = ExampleContactSchemaName,
					[SelectFieldName] = new[] { "Id", "Name", "AccountId" },
					["order-by"] = "Name asc",
					["top"] = 10
				}),
				Example("Query records where a text field contains a value", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[EntityFieldName] = ExampleAccountSchemaName,
					[FiltersFieldName] = new Dictionary<string, object?> {
						["all"] = new object[] {
							new Dictionary<string, object?> { [FieldFieldName] = "Name", ["op"] = "contains", [ValueFieldName] = "Acme" }
						}
					},
					[SelectFieldName] = new[] { "Id", "Name" },
					["top"] = 10
				}),
				Example("Query contacts by account lookup using structured filters", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[EntityFieldName] = ExampleContactSchemaName,
					[FiltersFieldName] = new Dictionary<string, object?> {
						["all"] = new object[] {
							new Dictionary<string, object?> {
								[FieldFieldName] = "Account/Id",
								["op"] = "eq",
								[ValueFieldName] = ExampleLookupValueId
							}
						}
					},
					[SelectFieldName] = new[] { "Id", "Name" },
					["top"] = 10
				})
			],
			Flow([ODataReadTool.ToolName], "Use when exact Creatio record values are needed from OData, including lookup primary values, record verification, filtered reads, or ordered record lists."),
			[
				Flow(
					[DataForgeTool.DataForgeFindTablesToolName, DataForgeTool.DataForgeGetTableColumnsToolName, ODataReadTool.ToolName],
					"Use when the entity name or column names are unknown and DataForge is available."),
				Flow(
					[FindEntitySchemaTool.FindEntitySchemaToolName, GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName, ODataReadTool.ToolName],
					"Alternative discovery path: use find-entity-schema to locate the schema by name, then get-entity-schema-properties to inspect its columns, then query.")
			],
			[]);
	}

	private static ToolContractDefinition BuildODataCreate() {
		return new ToolContractDefinition(
			ODataCreateTool.ToolName,
			"Creates one or more Creatio records through OData v4 (POST) in a single call. Pass all rows for the same entity in the 'rows' array rather than one call per row; each row is inserted sequentially and reported independently. Returns a created/failed summary and a per-row result array including each created record's Id.",
			new ToolInputSchemaContract(
				[EntityFieldName, "rows", EnvironmentNameFieldName],
				[
					Field(EntityFieldName, StringType, "Creatio OData entity set name such as Contact, Account, or a custom schema."),
					Field("rows", ArrayType, "Array of row objects to insert; each row is an object of field/value pairs for one new record. Lookup fields are set with their GUID, for example [ { \"Name\": \"Acme\", \"TypeId\": \"00000000-0000-0000-0000-000000000001\" } ]. Pass all rows in one call rather than one call per row."),
					Field("stop-on-error", BooleanType, "Optional. Stop after the first failed row. Default false: continue and report every row independently. When true, rows after a failure are not attempted and do not appear in results, so results may be shorter than rows."),
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription)
				]),
			ODataCreateBatchOutput(),
			CommonErrorContract,
			[],
			[],
			[
				Example("Create a contact", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[EntityFieldName] = ExampleContactSchemaName,
					["rows"] = new object[] { new Dictionary<string, object?> { ["Name"] = "Jane Doe", ["JobTitle"] = "CEO" } }
				})
			],
			Flow([ODataCreateTool.ToolName], "Use to insert a new Creatio record when field values are known."),
			[
				Flow(
					[DataForgeTool.DataForgeFindTablesToolName, DataForgeTool.DataForgeGetTableColumnsToolName, ODataCreateTool.ToolName],
					"Discover the entity and column names first when they are unknown, then create."),
				Flow(
					[ODataCreateTool.ToolName, ODataReadTool.ToolName],
					"Create the record, then read it back by the returned id to confirm persisted values.")
			],
			[]);
	}

	private static ToolContractDefinition BuildODataUpdate() {
		return new ToolContractDefinition(
			ODataUpdateTool.ToolName,
			"Updates a single Creatio record through OData v4 (PATCH). Requires the record GUID and confirm=true; only supplied fields change. Never performs a keyless mass update.",
			new ToolInputSchemaContract(
				[EntityFieldName, "id", "data", ConfirmFieldName, EnvironmentNameFieldName],
				[
					Field(EntityFieldName, StringType, "Creatio OData entity set name such as Contact or Account."),
					Field("id", StringType, "GUID of the record to update. Required; a keyless mass update is rejected."),
					Field("data", ObjectType, "Object of field/value pairs to change. Only supplied fields are updated."),
					Field(ConfirmFieldName, BooleanType, "Must be true to authorize this destructive update. When false or omitted the tool refuses without any remote call."),
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription)
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[SuccessFalseSignal],
				Field(SuccessFieldName, BooleanType, "Whether the OData update succeeded."),
				Field(ErrorFieldName, StringType, FailureMessageDescription),
				Field("id", StringType, "GUID of the updated record.")
			),
			CommonErrorContract,
			[],
			[],
			[
				Example("Rename a contact", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[EntityFieldName] = ExampleContactSchemaName,
					["id"] = ExampleLookupValueId,
					["data"] = new Dictionary<string, object?> { ["Name"] = "Jane Smith" },
					[ConfirmFieldName] = true
				})
			],
			Flow([ODataUpdateTool.ToolName], "Use to change fields of an existing Creatio record identified by its GUID."),
			[
				Flow(
					[ODataReadTool.ToolName, ODataUpdateTool.ToolName],
					"Read the record to obtain its Id, then update the desired fields by that Id.")
			],
			[]);
	}

	private static ToolContractDefinition BuildODataDelete() {
		return new ToolContractDefinition(
			ODataDeleteTool.ToolName,
			"Deletes a single Creatio record through OData v4 (DELETE). Requires the record GUID and confirm=true; never performs a keyless mass delete.",
			new ToolInputSchemaContract(
				[EntityFieldName, "id", ConfirmFieldName, EnvironmentNameFieldName],
				[
					Field(EntityFieldName, StringType, "Creatio OData entity set name such as Contact or Account."),
					Field("id", StringType, "GUID of the record to delete. Required; a keyless mass delete is rejected."),
					Field(ConfirmFieldName, BooleanType, "Must be true to authorize this destructive delete. When false or omitted the tool refuses without any remote call."),
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription)
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[SuccessFalseSignal],
				Field(SuccessFieldName, BooleanType, "Whether the OData delete succeeded."),
				Field(ErrorFieldName, StringType, FailureMessageDescription),
				Field("id", StringType, "GUID of the deleted record.")
			),
			CommonErrorContract,
			[],
			[],
			[
				Example("Delete a contact by id", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[EntityFieldName] = ExampleContactSchemaName,
					["id"] = ExampleLookupValueId,
					[ConfirmFieldName] = true
				})
			],
			Flow([ODataDeleteTool.ToolName], "Use to remove a single Creatio record identified by its GUID."),
			[
				Flow(
					[ODataReadTool.ToolName, ODataDeleteTool.ToolName],
					"Read to confirm the target record and obtain its Id, then delete by that Id.")
			],
			[]);
	}

	private static ToolContractDefinition BuildDataForgeContract(DataForgeContractDescriptor descriptor) {
		return new ToolContractDefinition(
			descriptor.ToolName,
			descriptor.Description,
			new ToolInputSchemaContract(
				descriptor.RequiredFields,
				descriptor.InputFields,
				AnyOf: DataForgeConnectionRequirements()),
			EnvelopeOutput(
				SuccessFieldName,
				[SuccessFalseSignal],
				[..descriptor.OutputFields]),
			CommonErrorContract,
			descriptor.Aliases ?? DataForgeConnectionAliases(),
			[],
			descriptor.Examples,
			descriptor.PreferredFlow,
			descriptor.FallbackFlow,
			[]);
	}

	private sealed record DataForgeContractDescriptor {
		public string ToolName { get; init; } = string.Empty;
		public string Description { get; init; } = string.Empty;
		public IReadOnlyList<string> RequiredFields { get; init; } = [];
		public IReadOnlyList<ToolContractField> InputFields { get; init; } = [];
		public IReadOnlyList<ToolContractField> OutputFields { get; init; } = [];
		public IReadOnlyList<ToolContractAlias>? Aliases { get; init; }
		public IReadOnlyList<ToolContractExample> Examples { get; init; } = [];
		public ToolFlowHint PreferredFlow { get; init; } = new([], string.Empty);
		public IReadOnlyList<ToolFlowHint> FallbackFlow { get; init; } = [];
	}

	private static IReadOnlyList<ToolContractField> DataForgeEnvelopeFields(
		string correlationDescription,
		params ToolContractField[] bodyFields) {
		return [
			Field(SuccessFieldName, BooleanType, ToolSucceededDescription),
			Field("source", StringType, "Response source identifier."),
			Field("correlation-id", StringType, correlationDescription),
			Field("warnings", ArrayType, "Non-fatal warnings."),
			..bodyFields,
			Field(ErrorFieldName, ObjectType, "Structured Data Forge error payload.")
		];
	}

	private static ToolContractDefinition BuildApplicationGetInfo() {
		return new ToolContractDefinition(
			ApplicationGetInfoTool.ApplicationGetInfoToolName,
			"Returns installed application identity plus current package and entity metadata so callers can inspect the right app before mutating it.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName],
				[
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription),
					Field(SelectorIdFieldName, StringType, "Application GUID."),
					Field(SelectorCodeFieldName, StringType, "Application code.")
				],
				AnyOf: [
					new[] { SelectorIdFieldName },
					[SelectorCodeFieldName]
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, ToolSucceededDescription),
				Field(PackageUIdFieldName, StringType, PrimaryPackageIdentifierDescription),
				Field(PackageNameFieldName, StringType, PrimaryPackageNameDescription),
				Field("canonical-main-entity-name", StringType, "Canonical main entity name."),
				Field(ApplicationIdFieldName, StringType, InstalledApplicationIdentifierDescription),
				Field(ApplicationNameFieldName, StringType, InstalledApplicationDisplayNameDescription),
				Field(ApplicationCodeFieldName, StringType, InstalledApplicationCodeDescription),
				Field(ApplicationVersionFieldName, StringType, InstalledApplicationVersionDescription),
				Field("entities", ArrayType, "Application entities. Each entity includes `virtual`, and each entity `columns` item carries a vocabulary unified with the sync-schemas write surfaces so it round-trips without translation: `name`, `caption`, canonical `type` (with `data-value-type` kept as a legacy alias), canonical `reference-schema-name` (with `reference-schema` kept as a legacy alias), and `required`. Send a column back to sync-schemas update-entity by adding the `action` verb."),
				Field(PagesFieldName, ArrayType, "Primary-package Freedom UI pages using list-pages item shape (`schema-name`, `uId`, `packageName`, `parentSchemaName`)."),
				Field("schema-name-prefix", StringType, "Active SchemaNamePrefix system setting for the environment. Use as the prefix for all subsequent custom schema codes. Empty string means no prefix is configured."),
				Field(ErrorFieldName, StringType, FailureMessageDescription)
			),
			CommonErrorContract,
			[],
			[],
			[
				Example("Refresh app context by code", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[SelectorCodeFieldName] = ExamplePackageName
				})
			],
			Flow(
				[
					ApplicationGetListTool.ApplicationGetListToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName,
					ApplicationSectionCreateTool.ApplicationSectionCreateToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName
				],
				"Use after list-apps when the target app is not fully known, or refresh again after section or schema mutations when app context must be re-read."),
			[],
			[]);
	}

	private static ToolContractDefinition BuildApplicationGetList() {
		return new ToolContractDefinition(
			ApplicationGetListTool.ApplicationGetListToolName,
			"Lists installed applications from the target Creatio environment so the caller can discover the right existing app first.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName],
				[
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription)
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, ToolSucceededDescription),
				Field("applications", ArrayType, "Installed applications."),
				Field(ErrorFieldName, StringType, FailureMessageDescription)
			),
			CommonErrorContract,
			[],
			[],
			[
				Example("List installed applications with top-level environment-name", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName
				})
			],
			Flow(
				[
					ApplicationGetListTool.ApplicationGetListToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName
				],
				"Use when the workflow must branch into existing-app discovery."),
			[
				Flow(
					[
						ApplicationGetListTool.ApplicationGetListToolName,
						ApplicationGetInfoTool.ApplicationGetInfoToolName,
						ApplicationSectionCreateTool.ApplicationSectionCreateToolName,
						ApplicationGetInfoTool.ApplicationGetInfoToolName
					],
					"Extend the existing-app discovery flow with section creation when the task is to add a section to an installed app.")
			],
			[]);
	}

	private static ToolContractDefinition BuildEntityBusinessRuleCreate() {
		return new ToolContractDefinition(
			CreateEntityBusinessRuleTool.BusinessRuleCreateToolName,
			"Creates an entity-level Freedom UI business rule with equality, filled-in, numeric or date/time relational comparisons, Set values actions from constants, formulas, or attributes, and dynamic apply-filter lookup actions. Read get-guidance business-rules and this get-tool-contract entry before calling.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, PackageNameFieldName, EntitySchemaNameFieldName, RulesFieldName],
				[
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription),
					Field(PackageNameFieldName, StringType, "Target package name."),
					Field(EntitySchemaNameFieldName, StringType, "Target entity schema name."),
					Field(RulesFieldName, ArrayType, "Array of one or more entity business-rule definitions saved together in a single batch (one configuration rebuild for the whole array; prefer one call over many). A failed rule does not abort the others. Each item is a rule with caption, one top-level condition group, and one or more actions. Unary filled-in comparisons omit rightExpression. EITHER side of a condition may be an attribute (type AttributeValue), a constant (type Const), or a system variable (type SysValue with sysValueName such as CurrentDate, CurrentDateTime, CurrentTime, CurrentUser, CurrentUserContact, CurrentUserAccount, CurrentUserRoles) — any pairing is allowed; type/reference-schema compatibility is the only constraint. Role-based logic: CurrentUserRoles (left) comparisonType contain/not-contain a Const SysAdminUnit role id. Relational comparisons only support numeric and date/time operands. Set values actions support Const assignments for text, number, boolean, Date, DateTime, Time, and Lookup targets, Formula assignments with simple numeric direct-field expressions such as Field1 + Field2, and AttributeValue assignments from same-typed direct or forward reference paths such as Owner.Age. Apply-filter actions target one lookup field and may use an empty condition group because the filter logic is expressed inside the action itself.")
				],
				Validators: [
					.. BusinessRuleConditionValidators(),
					new ToolContractValidator("enum", "unsupported-action", "rules[*].actions[*].type",
						Context: $"Supported values: {BusinessRuleConstants.SupportedActionTypesDescription}."),
					new ToolContractValidator("set-values-shape", "invalid-set-values-item", "rules[*].actions[*].items[*]",
						Context: "When rule.actions[*].type is set-values, each item must provide expression { type: AttributeValue, path } and value { type: Const, value }, { type: Formula, expression }, or { type: AttributeValue, path }. Formula expression must be a string using a simple numeric direct-field arithmetic expression, for example Field1 + Field2. Formula target and source attributes must be numeric; date/time arithmetic is not supported. AttributeValue source paths may be direct columns or forward reference paths like LookupColumn.SourceColumn; the final source attribute and target attribute must have the same data value type. Formula functions, comparison operators, and string literals are not supported in formula scope."),
					new ToolContractValidator("set-values-constant", "unsupported-set-values-constant", "rules[*].actions[*].items[*].value.value",
						Context: "Set values supports JSON string constants for text targets, JSON number constants for numeric targets, JSON booleans for Boolean targets, yyyy-MM-dd strings for Date targets, ISO 8601 strings with timezone suffix for DateTime targets, ISO 8601 time strings with timezone suffix for Time targets, and GUID string constants for Lookup targets."),
					new ToolContractValidator("set-values-formula", "invalid-set-values-formula", "rules[*].actions[*].items[*].value.expression",
						Context: "Formula expressions are translated after payload parsing into expression-schema PowerFx metadata, checked locally against a numeric arithmetic whitelist, then validated remotely through ServiceModel/ExpressionService.svc/Validate before saving. Referenced direct numeric source fields are added as business-rule triggers. AttributeValue sources are serialized as business-rule attribute expressions; direct sources trigger on that source column, and forward sources trigger on the root lookup column."),
					new ToolContractValidator("apply-filter-shape", "invalid-apply-filter-action", "rules[*].actions[*]",
						Context: "When rule.actions[*].type is apply-filter, provide target, targetFilterPath, source, optional sourceFilterPath, clearValue, and populateValue. Target and source must be direct lookup attributes on the root entity. targetFilterPath and sourceFilterPath resolve inside the referenced lookup schemas and must themselves resolve to Lookup attributes, not Guid columns. apply-filter rules support exactly one action and may use an empty condition group."),
					new ToolContractValidator("apply-filter-lookup", "unsupported-apply-filter-lookup", "rules[*].actions[*].target",
						Context: "apply-filter only supports lookup targets and lookup sources. The final targetFilterPath and source/sourceFilterPath endpoints must both resolve to Lookup attributes that reference the same schema; Guid endpoints are not supported. If sourceFilterPath is provided, populateValue must be false."),
					new ToolContractValidator("apply-static-filter-shape", "invalid-apply-static-filter-action", "rules[*].actions[*]",
						Context: "When rule.actions[*].type is apply-static-filter, provide targetAttribute (a direct Lookup column on the root entity) and filter (a friendly filter group). rootSchemaName is inferred from the target lookup's reference schema and must never be sent by the caller. apply-static-filter rules support exactly one action and may use an empty condition group."),
					new ToolContractValidator("apply-static-filter-group", "invalid-apply-static-filter-group", "rules[*].actions[*].filter",
						Context: "filter requires logicalOperation (AND or OR) and may include filters[], groups[] for nested logical compositions, and backwardReferenceFilters[]. Leaf comparisonType uses UPPER_SNAKE_CASE tokens (distinct from the kebab-case condition comparisons): EQUAL, NOT_EQUAL, IS_NULL, IS_NOT_NULL, GREATER, GREATER_OR_EQUAL, LESS, LESS_OR_EQUAL, CONTAIN, NOT_CONTAIN, START_WITH, NOT_START_WITH, END_WITH, NOT_END_WITH. columnPath is rooted at the target lookup's reference schema (not the rule entity) and supports forward paths through Lookup chains. To test a field is filled use IS_NOT_NULL on that column directly, not a backward EXISTS workaround. backwardReferenceFilters[].referenceColumnPath MUST be the bare `[ChildSchema:LinkColumn]` form (no `.Id` suffix, no trailing column); the builder appends `.Id` and stamps platform-canonical metadata. A backward clause is EITHER an existence check (comparisonType EXISTS/NOT_EXISTS) OR an aggregation: set aggregationType (COUNT/SUM/AVG/MIN/MAX), a relational/equality comparisonType (GREATER, GREATER_OR_EQUAL, LESS, LESS_OR_EQUAL, EQUAL, NOT_EQUAL) and a numeric aggregationValue — e.g. 'more than 10 activities' → { referenceColumnPath: '[Activity:Contact]', aggregationType: 'COUNT', comparisonType: 'GREATER', aggregationValue: 10 }. COUNT omits aggregationColumnPath; SUM/AVG/MIN/MAX require aggregationColumnPath (numeric child column). This is NOT the page DataSource staticFilters/filterConfig in body.js — never hand-edit body.js to restrict a lookup; use this action. Lookup values accept GUID strings or display names (resolved against the lookup's primary display column). JSON array of strings on a Lookup column with EQUAL/NOT_EQUAL produces a multi-value IN. For dynamic values use valueMacros (mutually exclusive with value): date macros (Today, Yesterday, Tomorrow, Previous/Current/Next Week/Month/Quarter/HalfYear/Year/Hour) on Date/DateTime/Time columns; 'birthday today/tomorrow' → DayOfYearTodayPlusDaysOffset with valueMacrosArgument 0/1 on the birth-date column; CurrentUser/CurrentUserContact on Lookup columns with EQUAL/NOT_EQUAL; N-style macros (NextNDays, PreviousNDays, NextNHours, PreviousNHours) also require valueMacrosArgument (positive integer). For a FIXED clock time or calendar part (NOT a relative period) use datePart on a Date/DateTime/Time column (mutually exclusive with valueMacros): Day/Week/Month/Year/Weekday/Hour take an integer value, HourMinute (alias Time) takes an 'HH:mm[:ss]' string — e.g. a fixed time of day → { columnPath: '<DateColumn>', datePart: 'HourMinute', comparisonType: 'EQUAL', value: '<HH:mm:ss>' }; a fixed calendar year → { columnPath: '<DateColumn>', datePart: 'Year', comparisonType: 'EQUAL', value: <YYYY> }. Exact time-of-day IS expressible via datePart — do not report it as unsupported. For 'age = N'/'aged between X and Y', resolve the schema first: filter a numeric age column directly when it exists, otherwise translate to a birth-date range with computed ISO date constants. See guidance resource business-rule-filters for the full contract."),
					new ToolContractValidator("lookup-record", "missing-lookup-record", "rules[*].actions[*].items[*].value.value",
						Context: $"Lookup set-values constants must be GUID strings for existing records in the target attribute reference schema. Use {ODataReadTool.ToolName} or {ExecuteEsqTool.ToolName} to resolve or verify the lookup record Id before calling create-entity-business-rules; with odata-read, filter records by a lookup value using traversal paths such as Account/Id.")
				]),
			BusinessRuleBatchOutput(),
			CommonErrorContract,
			[
			],
			[],
			[
				BusinessRuleExample("Create a required-field rule when owner equals a lookup constant",
					ExampleTaskSchemaName, "Require status for a specific owner", ExampleOwnerAttributeName, ExampleEqualConditionComparison,
					MakeRequiredActionTypeName, ["Status"], ExampleLookupValueId),
				BusinessRuleExample("Create a readonly rule when a text field is filled in",
					ExampleTaskSchemaName, "Lock planned date when name is filled", "Name", "is-filled-in",
					MakeReadOnlyActionTypeName, ["PlannedDate"]),
				BusinessRuleExample("Create a readonly rule when completed is true",
					ExampleTaskSchemaName, "Lock name and description when completed", "Completed", ExampleEqualConditionComparison,
					MakeReadOnlyActionTypeName, ["Name", "Description"], true),
				BusinessRuleExample("Create a required-field rule when annual revenue reaches a numeric threshold",
					ExampleAccountSchemaName, "Require owner for high-revenue accounts", "AnnualRevenue", "greater-than-or-equal",
					MakeRequiredActionTypeName, [ExampleOwnerAttributeName], 1000000),
				BusinessRuleExample("Create a required-field rule when created date is before a cutoff",
					ExampleTaskSchemaName, "Require owner before the 2025 cutoff", "CreatedOn", "less-than-or-equal",
					MakeRequiredActionTypeName, [ExampleOwnerAttributeName], "2025-01-01T00:00:00Z"),
				BusinessRuleExample("Create a readonly rule when reminder time is after a timezone-aware cutoff",
					ExampleTaskSchemaName, "Lock reminder note after local noon", "ReminderTime", "greater-than",
					MakeReadOnlyActionTypeName, ["ReminderNote"], "12:00:00+02:00"),
				SysValueBusinessRuleExample("Create a required-field rule when owner equals the current user contact (SysValue)",
					EntitySchemaNameFieldName, ExampleTaskSchemaName, "Require status when owner is the current user",
					ExampleOwnerAttributeName, ExampleEqualConditionComparison, "CurrentUserContact",
					MakeRequiredActionTypeName, ["Status"]),
				SysValueBusinessRuleExample("Create a readonly rule when due date is on or before the current date (SysValue)",
					EntitySchemaNameFieldName, ExampleTaskSchemaName, "Lock owner when due on or before today",
					"DueDate", "less-than-or-equal", "CurrentDate",
					MakeReadOnlyActionTypeName, [ExampleOwnerAttributeName]),
				RoleGateBusinessRuleExample("Require a field only for users in a role (CurrentUserRoles CONTAIN role)",
					EntitySchemaNameFieldName, ExampleTaskSchemaName, "Require status for administrators",
					"CurrentUserRoles", "contain", ExampleLookupValueId,
					MakeRequiredActionTypeName, ["Status"]),
				BusinessRuleExample("Create a Set values rule with text number boolean Date DateTime and Time constants",
					ExampleTaskSchemaName, "Populate defaults when name is filled", "Name", "is-filled-in",
					"set-values", [
						BusinessRuleSetValueItem("UsrTextResult", "Ready"),
						BusinessRuleSetValueItem("UsrScore", 42),
						BusinessRuleSetValueItem("UsrCompleted", true),
						BusinessRuleSetValueItem("UsrStartDate", "2025-01-01"),
						BusinessRuleSetValueItem("UsrPlannedOn", "2025-01-01T00:00:00Z"),
						BusinessRuleSetValueItem("UsrReminderTime", "12:00:00+02:00"),
						BusinessRuleSetValueItem("UsrOwner", ExampleLookupValueId)
					]),
				BusinessRuleExample("Create a Set values rule with a formula that sums two number fields",
					ExampleTaskSchemaName, "Calculate total effort when name is filled", "Name", "is-filled-in",
					"set-values", [
						BusinessRuleFormulaSetValueItem("UsrTotalEffort", "UsrEstimatedEffort + UsrExtraEffort")
					]),
				BusinessRuleExample("Create a Set values rule from a forward reference attribute",
					ExampleTaskSchemaName, "Copy creator age when name changes", "Name", "is-filled-in",
					"set-values", [
						BusinessRuleAttributeSetValueItem("UsrCreatorAge", "CreatedBy.Age")
					]),
				ApplyFilterBusinessRuleExample(
					"Create a dynamic lookup filter that limits City by Country",
					"UsrAddress",
					"Filter city by selected country",
					"City",
					"Country",
					"Country",
					null,
					true,
					true),
				ApplyFilterBusinessRuleExample(
					"Create a dynamic lookup filter that compares deep lookup paths",
					"UsrAddress",
					"Filter city by country time zone",
					"City",
					"Country.TimeZone",
					"Country",
					"TimeZone",
					true,
					false),
				ApplyStaticFilterBusinessRuleExample(
					"Apply a static filter limiting a City lookup to cities in a country resolved by display name (forward path)",
					"UsrAddress",
					"Limit city to USA cities",
					"City",
					new Dictionary<string, object?> {
						[LogicalOperationFieldName] = "AND",
						[FiltersFieldName] = new object[] {
							StaticFilterLeaf("Country.Name", ExampleEqualComparison, "USA")
						}
					}),
				ApplyStaticFilterBusinessRuleExample(
					"Apply a static filter limiting an Owner lookup to contacts whose type is one of several lookup values (multi-value IN)",
					ExampleTaskSchemaName,
					"Limit owner to selected contact types",
					ExampleOwnerAttributeName,
					new Dictionary<string, object?> {
						[LogicalOperationFieldName] = "AND",
						[FiltersFieldName] = new object[] {
							StaticFilterLeaf("Type", ExampleEqualComparison, new[] {
								ExampleLookupValueId,
								"00000000-0000-0000-0000-000000000002"
							})
						}
					}),
				ApplyStaticFilterBusinessRuleExample(
					"Apply a static filter limiting an Owner lookup to contacts that have an email address (IS_NOT_NULL on the column directly)",
					ExampleTaskSchemaName,
					"Limit owner to contacts with an email",
					ExampleOwnerAttributeName,
					new Dictionary<string, object?> {
						[LogicalOperationFieldName] = "AND",
						[FiltersFieldName] = new object[] {
							StaticFilterLeaf("Email", "IS_NOT_NULL")
						}
					}),
				ApplyStaticFilterBusinessRuleExample(
					"Apply a static filter limiting an Account lookup to accounts that have at least one related contact (backward EXISTS)",
					"UsrOrder",
					"Limit account to accounts with contacts",
					ExampleAccountSchemaName,
					new Dictionary<string, object?> {
						[LogicalOperationFieldName] = "AND",
						["backwardReferenceFilters"] = new object[] {
							new Dictionary<string, object?> {
								["referenceColumnPath"] = "[Contact:Account]",
								["comparisonType"] = "EXISTS"
							}
						}
					}),
				ApplyStaticFilterBusinessRuleExample(
					"Apply a static filter limiting an Assignee lookup to contacts whose Age equals 30 ('show the Assignee field only for contacts where Age = 30' is a lookup restriction, NOT field visibility; filter the Age column directly when it exists)",
					ExampleTaskSchemaName,
					"Limit assignee to contacts aged 30",
					ExampleAssigneeAttributeName,
					new Dictionary<string, object?> {
						[LogicalOperationFieldName] = "AND",
						[FiltersFieldName] = new object[] {
							StaticFilterLeaf("Age", ExampleEqualComparison, 30)
						}
					}),
				ApplyStaticFilterBusinessRuleExample(
					"Apply a static filter limiting an Assignee lookup to contacts that have more than 10 activities (backward COUNT aggregation)",
					ExampleTaskSchemaName,
					"Limit assignee to contacts with more than 10 activities",
					ExampleAssigneeAttributeName,
					new Dictionary<string, object?> {
						[LogicalOperationFieldName] = "AND",
						["backwardReferenceFilters"] = new object[] {
							new Dictionary<string, object?> {
								["referenceColumnPath"] = "[Activity:Contact]",
								["aggregationType"] = "COUNT",
								["comparisonType"] = "GREATER",
								["aggregationValue"] = 10
							}
						}
					}),
				ApplyStaticFilterBusinessRuleExample(
					"Apply a static filter limiting an Assignee lookup to contacts whose account was created this year (forward path + relative-date macros)",
					ExampleTaskSchemaName,
					"Limit assignee to contacts whose account is created this year",
					ExampleAssigneeAttributeName,
					new Dictionary<string, object?> {
						[LogicalOperationFieldName] = "AND",
						[FiltersFieldName] = new object[] {
							StaticFilterMacrosLeaf("Account.CreatedOn", "GREATER_OR_EQUAL", "CurrentYear")
						}
					}),
				ApplyStaticFilterBusinessRuleExample(
					"Apply a static filter limiting an Assignee lookup by a day-of-year anniversary match (DayOfYearTodayPlusDaysOffset macros)",
					ExampleTaskSchemaName,
					"Limit assignee by a day-of-year anniversary",
					ExampleAssigneeAttributeName,
					new Dictionary<string, object?> {
						[LogicalOperationFieldName] = "AND",
						[FiltersFieldName] = new object[] {
							StaticFilterMacrosLeaf("BirthDate", ExampleEqualComparison, "DayOfYearTodayPlusDaysOffset", 1)
						}
					}),
				ApplyStaticFilterBusinessRuleExample(
					"Apply a static filter limiting an Owner lookup to the current user's contact (CurrentUserContact macros)",
					ExampleTaskSchemaName,
					"Limit owner to current user",
					ExampleOwnerAttributeName,
					new Dictionary<string, object?> {
						[LogicalOperationFieldName] = "AND",
						[FiltersFieldName] = new object[] {
							StaticFilterMacrosLeaf("Id", ExampleEqualComparison, "CurrentUserContact")
						}
					}),
				ApplyStaticFilterBusinessRuleExample(
					"Apply a static filter limiting an Activity lookup to records due within the next 5 days (NextNDays date macros with argument)",
					"UsrOrder",
					"Limit activity to next 5 days",
					"Activity",
					new Dictionary<string, object?> {
						[LogicalOperationFieldName] = "AND",
						[FiltersFieldName] = new object[] {
							StaticFilterMacrosLeaf("DueDate", "LESS_OR_EQUAL", "NextNDays", 5)
						}
					})
			],
			Flow(
				[
					ApplicationGetListTool.ApplicationGetListToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName,
					ToolContractGetTool.ToolName,
					GuidanceGetTool.ToolName,
					CreateEntityBusinessRuleTool.BusinessRuleCreateToolName
				],
				"When the application exists and the entity is a part of it. Read the business-rules guidance and the create-entity-business-rules contract before calling the mutation tool. Successful rule creation writes add-on metadata directly, so do not add compile-creatio as a routine post-step."),
			[
				Flow(
					[
						ApplicationGetListTool.ApplicationGetListToolName,
						ApplicationGetInfoTool.ApplicationGetInfoToolName,
						FindEntitySchemaTool.FindEntitySchemaToolName,
						DataForgeTool.DataForgeFindTablesToolName,
						GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName,
						ODataReadTool.ToolName,
						CreateEntityBusinessRuleTool.BusinessRuleCreateToolName
					],
					"When the application exists but the entity is not a part of it. Find entity using find-entity or dataforge-find-tables. Resolve lookup constants to real record Ids before rule creation with odata-read or execute-esq; with odata-read, filter records by lookup values using traversal paths such as Account/Id."),
				Flow(
					[
						ApplicationCreateTool.ApplicationCreateToolName,
						FindEntitySchemaTool.FindEntitySchemaToolName,
						DataForgeTool.DataForgeFindTablesToolName,
						GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName,
						CreateEntityBusinessRuleTool.BusinessRuleCreateToolName
					],
					"When application does not exist yet. Suggest user to create new empty application and create business rule there."),

			],
			[],
			null,
			[
				"Call get-guidance with name business-rules before calling create-entity-business-rules.",
				"For an apply-static-filter action, also call get-guidance with name business-rule-filters to load the full filter contract.",
				"Call get-tool-contract for create-entity-business-rules before building the final payload.",
				"When any lookup condition or lookup set-values constant is needed, resolve it with odata-read or execute-esq first and use an existing record Id."
			]);
	}

	private static ToolContractDefinition BuildPageBusinessRuleCreate() {
		return new ToolContractDefinition(
			CreatePageBusinessRuleTool.BusinessRuleCreateToolName,
			"Creates a page-level Freedom UI business rule that changes visibility, editability, or required state of named page elements using datasource-bound page attributes and constants. Read get-guidance business-rules and this get-tool-contract entry before calling.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, PackageNameFieldName, PageSchemaNameFieldName, RulesFieldName],
				[
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription),
					Field(PackageNameFieldName, StringType, "Target package name where the page BusinessRule add-on will be saved."),
					Field(PageSchemaNameFieldName, StringType, "Target Freedom UI page schema name."),
					Field(RulesFieldName, ArrayType, "Array of one or more page business-rule definitions saved together in a single batch (one configuration rebuild for the whole array; prefer one call over many). A failed rule does not abort the others. Each item is a rule with caption, one top-level condition group, and one or more page actions. AttributeValue paths must be declared page attribute names from get-page bundle.viewModelConfig.attributes, not datasource paths like PDS.Priority. EITHER side of a condition may be a page attribute (type AttributeValue), a constant (type Const), or a system variable (type SysValue with sysValueName such as CurrentDate, CurrentDateTime, CurrentTime, CurrentUser, CurrentUserContact, CurrentUserAccount, CurrentUserRoles). For role-based or current-user visibility (e.g. 'show field only for administrators / for the supervisor') put CurrentUserRoles (left) comparisonType contain/not-contain a Const SysAdminUnit role id, or compare CurrentUser/CurrentUserContact/CurrentUserAccount to a Const id — use this instead of a HandleViewModelInitRequest handler. Action items must be page element names from recursive get-page bundle.viewConfig. Lookup constants are supported when supplied as stable GUID strings.")
				],
				Validators: [
					.. BusinessRuleConditionValidators(),
					new ToolContractValidator("page-attribute", "unsupported-condition-attribute", "rules[*].condition.conditions[*].leftExpression.path",
						Context: "Use declared datasource-bound page attribute names from bundle.viewModelConfig.attributes, for example PDS_Priority. Do not use datasource paths like PDS.Priority."),
					new ToolContractValidator("page-attribute", "unsupported-right-attribute", "rules[*].condition.conditions[*].rightExpression.path",
						Context: "Right-side AttributeValue is supported only when it is also a declared datasource-bound page attribute and resolves to the same data value type as the left attribute."),
					new ToolContractValidator("enum", "unsupported-action", "rules[*].actions[*].type",
						Context: $"Supported values: {BusinessRuleConstants.SupportedPageActionTypesDescription}."),
					new ToolContractValidator("page-element", "unknown-page-element", "rules[*].actions[*].items",
						Context: "Use any named element from recursive get-page bundle.viewConfig.")
				]),
			BusinessRuleBatchOutput(),
			CommonErrorContract,
			[],
			[],
			[
				PageBusinessRuleExample(
					"Make priority editable when page status is filled",
					"Case_FormPage",
					"Make priority editable when status is filled",
					"PDS_Status",
					"is-filled-in",
					"make-editable",
					["PriorityInput"]),
				PageBusinessRuleExample(
					"Make amount read-only when amount exceeds threshold",
					ExampleOrderPageSchemaName,
					"Make amount read-only over threshold",
					"PDS_UsrAmount",
					"greater-than",
					MakeReadOnlyActionTypeName,
					["AmountInput"],
					100000),
				PageBusinessRuleExample(
					"Make close date required when stage is closed",
					ExampleOrderPageSchemaName,
					"Require close date for closed stage",
					"PDS_UsrStage",
					ExampleEqualConditionComparison,
					MakeRequiredActionTypeName,
					["CloseDateInput"],
					"Closed"),
				PageBusinessRuleExample(
					"Make comment optional when page flag is false",
					ExampleOrderPageSchemaName,
					"Make comment optional when flag is false",
					"PDS_UsrFlag",
					ExampleEqualConditionComparison,
					"make-optional",
					["CommentInput"],
					false),
				PageBusinessRuleExample(
					"Hide Escalate when priority matches a lookup constant",
					"Case_FormPage",
					"Hide Escalate when priority matches",
					"PDS_Priority",
					ExampleEqualConditionComparison,
					"hide-element",
					["EscalateButton"],
					ExampleLookupValueId),
				PageBusinessRuleExample(
					"Show a warning label when amount exceeds threshold",
					ExampleOrderPageSchemaName,
					"Show warning for high amount",
					"PDS_UsrAmount",
					"greater-than",
					"show-element",
					["HighAmountWarningLabel"],
					100000),
				SysValueBusinessRuleExample("Hide a control when due date is on or before the current date (SysValue)",
					PageSchemaNameFieldName, ExampleOrderPageSchemaName, "Hide reminder when due on or before today",
					"PDS_UsrDueDate", "less-than-or-equal", "CurrentDate",
					"hide-element", ["ReminderLabel"]),
				RoleGateBusinessRuleExample("Show a control only for users in a role (CurrentUserRoles CONTAIN role)",
					PageSchemaNameFieldName, "Cases_FormPage", "Show Resolved for administrators",
					"CurrentUserRoles", "contain", ExampleLookupValueId,
					"show-element", ["ResolvedCheckbox"]),
				RoleGateBusinessRuleExample("Hide a control for users NOT in a role (inverse rule; CurrentUserRoles NOT_CONTAIN role)",
					PageSchemaNameFieldName, "Cases_FormPage", "Hide Resolved for non-administrators",
					"CurrentUserRoles", "not-contain", ExampleLookupValueId,
					"hide-element", ["ResolvedCheckbox"]),
				RoleGateBusinessRuleExample("Show a control only for a specific current user contact (CurrentUserContact EQUAL contact)",
					PageSchemaNameFieldName, "Cases_FormPage", "Show Assignee group for the supervisor",
					"CurrentUserContact", ExampleEqualConditionComparison, ExampleLookupValueId,
					"show-element", ["AssigneeGroupInput"]),
				PageBusinessRuleAttributeComparisonExample()
			],
			Flow(
				[
					PageListTool.ToolName,
					PageGetTool.ToolName,
					ToolContractGetTool.ToolName,
					GuidanceGetTool.ToolName,
					CreatePageBusinessRuleTool.BusinessRuleCreateToolName
				],
				"Use list-pages or application discovery to choose the page, call get-page to inspect bundle.viewConfig and bundle.viewModelConfig.attributes, then read the business-rules guidance and create-page-business-rules contract before creating the page rule. Successful rule creation writes add-on metadata directly, so do not add compile-creatio as a routine post-step."),
			[
				Flow(
					[
						ApplicationGetListTool.ApplicationGetListToolName,
						ApplicationGetInfoTool.ApplicationGetInfoToolName,
						PageGetTool.ToolName,
						ODataReadTool.ToolName,
						CreatePageBusinessRuleTool.BusinessRuleCreateToolName
					],
					"When the target page belongs to a known application, inspect the application first and then fetch the page bundle before creating the rule. Resolve lookup constants to real record Ids before rule creation with odata-read or execute-esq; with odata-read, filter records by lookup values using traversal paths such as Account/Id.")
			],
			[],
			[
				new ToolAntiPattern(
					"Using datasource paths like PDS.Priority in rule.condition.conditions[*].leftExpression.path.",
					"Page business rules use declared view-model attribute names from bundle.viewModelConfig.attributes so the generated metadata and triggers match the page runtime.")
			],
			[
				"Call get-guidance with name business-rules before calling create-page-business-rules.",
				"Call get-tool-contract for create-page-business-rules before building the final payload.",
				"When any lookup condition constant is needed, resolve it with odata-read or execute-esq first and use an existing record Id. With odata-read, filter records by a lookup value using a structured-filter traversal path such as Account/Id."
			]);
	}

	private static ToolContractValidator[] BusinessRuleConditionValidators() =>
		[
			new ToolContractValidator("enum", "unsupported-operator", "rules[*].condition.logicalOperation",
				Context: "Supported values: AND, OR."),
			new ToolContractValidator("enum", "unsupported-comparison", "rules[*].condition.conditions[*].comparisonType",
				Context: $"Supported values: {BusinessRuleConstants.SupportedComparisonTypesDescription}."),
			new ToolContractValidator("conditional-field", "invalid-right-expression-shape", "rules[*].condition.conditions[*].rightExpression",
				Context: "Required for equal, not-equal, greater-than, greater-than-or-equal, less-than, and less-than-or-equal. Omit or null for is-filled-in and is-not-filled-in."),
			new ToolContractValidator("comparison-family", "unsupported-relational-operands", "rules[*].condition.conditions[*]",
				Context: "greater-than, greater-than-or-equal, less-than, and less-than-or-equal only support numeric and date/time left attributes (Date, DateTime, Time). Attribute-to-attribute relational comparisons must use matching data value types."),
			new ToolContractValidator("comparison-family", "unsupported-equality-operands", "rules[*].condition.conditions[*]",
				Context: "equal and not-equal are not supported when the left attribute data value type is RichText or Image. Use is-filled-in or is-not-filled-in for those attributes."),
			new ToolContractValidator("date-time-constant", "invalid-date-time-constant", "rules[*].condition.conditions[*].rightExpression.value",
				Context: "Date constants must be JSON strings in yyyy-MM-dd format. DateTime constants must be JSON strings in ISO 8601 date-time format with a timezone suffix ('Z' or '+/-HH:mm'). Time constants must be JSON strings in ISO 8601 time format with a timezone suffix ('Z' or '+/-HH:mm')."),
			new ToolContractValidator("lookup-record", "missing-lookup-record", "rules[*].condition.conditions[*].rightExpression.value",
				Context: $"Lookup constants must be GUID strings for existing records in the attribute reference schema. Use {ODataReadTool.ToolName} or {ExecuteEsqTool.ToolName} to resolve or verify the lookup record Id before calling the business-rule creation tool; with odata-read, filter records by a lookup value using traversal paths such as Account/Id."),
			new ToolContractValidator("sys-value", "unsupported-system-variable", "rules[*].condition.conditions[*].leftExpression|rightExpression.sysValueName",
				Context: $"A SysValue may be on EITHER side of a condition. sysValueName must be one of: {BusinessRuleConstants.SupportedSystemVariablesDescription}. Types: CurrentDate=Date, CurrentTime=Time, CurrentDateTime=DateTime, CurrentUser/CurrentUserContact/CurrentUserAccount=Lookup, CurrentUserRoles=ObjectList (a collection of SysAdminUnit roles). Both operands must resolve to the same data value type and, for lookups, the same reference schema (CurrentUserContact=Contact, CurrentUserAccount=Account, CurrentUser/CurrentUserRoles=SysAdminUnit). Role-based visibility: CurrentUserRoles on the left, comparisonType contain/not-contain, and a Const SysAdminUnit role id on the right."),
			new ToolContractValidator("comparison-operand", "incompatible-condition-operands", "rules[*].condition.conditions[*]",
				Context: "Either side may be AttributeValue, Const, or SysValue, in any pairing. comparisonType contain/not-contain requires the left operand to be an ObjectList (for example CurrentUserRoles) or a text type. A Const operand inherits its data value type and reference schema from the operand it is compared against.")
		];

	private static ToolContractExample BusinessRuleExample(
		string summary,
		string entitySchemaName,
		string caption,
		string leftPath,
		string comparisonType,
		string actionType,
		object[] actionItems,
		object? constantValue = null) =>
		BusinessRuleExample(summary, EntitySchemaNameFieldName, entitySchemaName, caption, leftPath,
			comparisonType, actionType, actionItems, constantValue);

	private const string BusinessRuleValueKey = ValueFieldName;

	private static Dictionary<string, object?> BusinessRuleSetValueItem(string path, object value) {
		return new Dictionary<string, object?> {
			["expression"] = new Dictionary<string, object?> {
				["type"] = "AttributeValue",
				["path"] = path
			},
			[BusinessRuleValueKey] = new Dictionary<string, object?> {
				["type"] = "Const",
				[BusinessRuleValueKey] = value
			}
		};
	}

	private static Dictionary<string, object?> BusinessRuleFormulaSetValueItem(string path, string formula) {
		return new Dictionary<string, object?> {
			["expression"] = new Dictionary<string, object?> {
				["type"] = "AttributeValue",
				["path"] = path
			},
			[BusinessRuleValueKey] = new Dictionary<string, object?> {
				["type"] = "Formula",
				["expression"] = formula
			}
		};
	}

	private static Dictionary<string, object?> BusinessRuleAttributeSetValueItem(string path, string sourcePath) {
		return new Dictionary<string, object?> {
			["expression"] = new Dictionary<string, object?> {
				["type"] = "AttributeValue",
				["path"] = path
			},
			[BusinessRuleValueKey] = new Dictionary<string, object?> {
				["type"] = "AttributeValue",
				["path"] = sourcePath
			}
		};
	}

	private static ToolContractExample ApplyFilterBusinessRuleExample(
		string summary,
		string entitySchemaName,
		string caption,
		string target,
		string targetFilterPath,
		string source,
		string? sourceFilterPath,
		bool clearValue,
		bool populateValue) {
		Dictionary<string, object?> action = new() {
			["type"] = BusinessRuleConstants.ApplyFilterActionTypeName,
			["target"] = target,
			["targetFilterPath"] = targetFilterPath,
			["source"] = source,
			["clearValue"] = clearValue,
			["populateValue"] = populateValue
		};
		if (!string.IsNullOrWhiteSpace(sourceFilterPath)) {
			action["sourceFilterPath"] = sourceFilterPath;
		}

		return Example(summary, new Dictionary<string, object?> {
			[EnvironmentNameFieldName] = ExampleEnvironmentName,
			[PackageNameFieldName] = ExamplePackageName,
			[EntitySchemaNameFieldName] = entitySchemaName,
			[RulesFieldName] = new object[] { new Dictionary<string, object?> {
				["caption"] = caption,
				[ConditionFieldName] = new Dictionary<string, object?> {
					[LogicalOperationFieldName] = "AND",
					[ConditionsFieldName] = System.Array.Empty<object>()
				},
				[ActionsFieldName] = new object[] { action }
			} }
		});
	}

	private static ToolContractExample ApplyStaticFilterBusinessRuleExample(
		string summary,
		string entitySchemaName,
		string caption,
		string targetAttribute,
		Dictionary<string, object?> filter) {
		Dictionary<string, object?> action = new() {
			["type"] = BusinessRuleConstants.ApplyStaticFilterActionTypeName,
			["targetAttribute"] = targetAttribute,
			["filter"] = filter
		};

		return Example(summary, new Dictionary<string, object?> {
			[EnvironmentNameFieldName] = ExampleEnvironmentName,
			[PackageNameFieldName] = ExamplePackageName,
			[EntitySchemaNameFieldName] = entitySchemaName,
			[RulesFieldName] = new object[] { new Dictionary<string, object?> {
				["caption"] = caption,
				[ConditionFieldName] = new Dictionary<string, object?> {
					[LogicalOperationFieldName] = "AND",
					[ConditionsFieldName] = System.Array.Empty<object>()
				},
				[ActionsFieldName] = new object[] { action }
			} }
		});
	}

	private static Dictionary<string, object?> StaticFilterLeaf(
		string columnPath, string comparisonType, object? value = null) {
		Dictionary<string, object?> leaf = new() {
			["columnPath"] = columnPath,
			["comparisonType"] = comparisonType
		};
		if (value is not null) {
			leaf[ValueFieldName] = value;
		}
		return leaf;
	}

	private static Dictionary<string, object?> StaticFilterMacrosLeaf(
		string columnPath, string comparisonType, string valueMacros, int? valueMacrosArgument = null) {
		Dictionary<string, object?> leaf = new() {
			["columnPath"] = columnPath,
			["comparisonType"] = comparisonType,
			["valueMacros"] = valueMacros
		};
		if (valueMacrosArgument is not null) {
			leaf["valueMacrosArgument"] = valueMacrosArgument;
		}
		return leaf;
	}

	private static ToolContractExample PageBusinessRuleExample(
		string summary,
		string pageSchemaName,
		string caption,
		string leftPath,
		string comparisonType,
		string actionType,
		object[] actionItems,
		object? constantValue = null) =>
		BusinessRuleExample(summary, PageSchemaNameFieldName, pageSchemaName, caption, leftPath,
			comparisonType, actionType, actionItems, constantValue);

	private static ToolContractExample BusinessRuleExample(
		string summary,
		string schemaFieldName,
		string schemaName,
		string caption,
		string leftPath,
		string comparisonType,
		string actionType,
		object[] actionItems,
		object? constantValue = null) {
		Dictionary<string, object?> condition = new() {
			["leftExpression"] = new Dictionary<string, object?> {
				["type"] = "AttributeValue",
				["path"] = leftPath
			},
			["comparisonType"] = comparisonType
		};
		if (constantValue is not null) {
			condition["rightExpression"] = new Dictionary<string, object?> {
				["type"] = "Const",
				[BusinessRuleValueKey] = constantValue
			};
		}

		return Example(summary, new Dictionary<string, object?> {
			[EnvironmentNameFieldName] = ExampleEnvironmentName,
			[PackageNameFieldName] = ExamplePackageName,
			[schemaFieldName] = schemaName,
			[RulesFieldName] = new object[] { new Dictionary<string, object?> {
				["caption"] = caption,
				[ConditionFieldName] = new Dictionary<string, object?> {
					[LogicalOperationFieldName] = "AND",
					[ConditionsFieldName] = new object[] {
						condition
					}
				},
				[ActionsFieldName] = new object[] {
					new Dictionary<string, object?> {
						["type"] = actionType,
						["items"] = actionItems
					}
				}
			} }
		});
	}

	private static ToolContractExample PageBusinessRuleAttributeComparisonExample() {
		return Example("Hide a warning when two datasource-bound page attributes match", new Dictionary<string, object?> {
			[EnvironmentNameFieldName] = ExampleEnvironmentName,
			[PackageNameFieldName] = ExamplePackageName,
			[PageSchemaNameFieldName] = ExampleOrderPageSchemaName,
			[RulesFieldName] = new object[] { new Dictionary<string, object?> {
				["caption"] = "Hide warning when planned and actual dates match",
				[ConditionFieldName] = new Dictionary<string, object?> {
					[LogicalOperationFieldName] = "AND",
					[ConditionsFieldName] = new object[] {
						new Dictionary<string, object?> {
							["leftExpression"] = new Dictionary<string, object?> {
								["type"] = "AttributeValue",
								["path"] = "PDS_UsrPlannedDate"
							},
							["comparisonType"] = ExampleEqualConditionComparison,
							["rightExpression"] = new Dictionary<string, object?> {
								["type"] = "AttributeValue",
								["path"] = "PDS_UsrActualDate"
							}
						}
					}
				},
				[ActionsFieldName] = new object[] {
					new Dictionary<string, object?> {
						["type"] = "hide-element",
						["items"] = new object[] { "DateMismatchWarningLabel" }
					}
				}
			} }
		});
	}

	private static ToolContractExample SysValueBusinessRuleExample(
		string summary,
		string schemaFieldName,
		string schemaName,
		string caption,
		string leftPath,
		string comparisonType,
		string sysValueName,
		string actionType,
		object[] actionItems) {
		Dictionary<string, object?> condition = new() {
			["leftExpression"] = new Dictionary<string, object?> {
				["type"] = "AttributeValue",
				["path"] = leftPath
			},
			["comparisonType"] = comparisonType,
			["rightExpression"] = new Dictionary<string, object?> {
				["type"] = BusinessRuleConstants.SysValueExpressionType,
				["sysValueName"] = sysValueName
			}
		};

		return Example(summary, new Dictionary<string, object?> {
			[EnvironmentNameFieldName] = ExampleEnvironmentName,
			[PackageNameFieldName] = ExamplePackageName,
			[schemaFieldName] = schemaName,
			[RulesFieldName] = new object[] { new Dictionary<string, object?> {
				["caption"] = caption,
				[ConditionFieldName] = new Dictionary<string, object?> {
					[LogicalOperationFieldName] = "AND",
					[ConditionsFieldName] = new object[] {
						condition
					}
				},
				[ActionsFieldName] = new object[] {
					new Dictionary<string, object?> {
						["type"] = actionType,
						["items"] = actionItems
					}
				}
			} }
		});
	}

	private static ToolContractExample RoleGateBusinessRuleExample(
		string summary,
		string schemaFieldName,
		string schemaName,
		string caption,
		string sysValueName,
		string comparisonType,
		string roleOrRecordId,
		string actionType,
		object[] actionItems) {
		Dictionary<string, object?> condition = new() {
			["leftExpression"] = new Dictionary<string, object?> {
				["type"] = BusinessRuleConstants.SysValueExpressionType,
				["sysValueName"] = sysValueName
			},
			["comparisonType"] = comparisonType,
			["rightExpression"] = new Dictionary<string, object?> {
				["type"] = "Const",
				[ValueFieldName] = roleOrRecordId
			}
		};

		return Example(summary, new Dictionary<string, object?> {
			[EnvironmentNameFieldName] = ExampleEnvironmentName,
			[PackageNameFieldName] = ExamplePackageName,
			[schemaFieldName] = schemaName,
			[RulesFieldName] = new object[] { new Dictionary<string, object?> {
				["caption"] = caption,
				[ConditionFieldName] = new Dictionary<string, object?> {
					[LogicalOperationFieldName] = "AND",
					[ConditionsFieldName] = new object[] {
						condition
					}
				},
				[ActionsFieldName] = new object[] {
					new Dictionary<string, object?> {
						["type"] = actionType,
						["items"] = actionItems
					}
				}
			} }
		});
	}

	private static ToolContractDefinition BuildSchemaSync() {
		return new ToolContractDefinition(
			SchemaSyncTool.ToolName,
			"Batches create-lookup, create-entity, update-entity, and inline seed operations in one call. Requests use operations[*].type; do not send operations[*].operation.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, PackageNameFieldName, OperationsFieldName],
				EnvironmentPackageFields(
					Field(OperationsFieldName, ArrayType, "Ordered schema operations. For create-entity, set `is-virtual` to true to create a virtual schema without a physical table; it defaults to false and cannot be combined with `seed-rows`. For update-entity, supply `update-operations` (add/modify/remove) or a `columns` add-batch. Column fields are unified with get-app-info: `column-name` (alias `name`), `type` (alias `data-value-type`), `reference-schema-name` (alias `reference-schema`), `required` (alias `is-required`) — so a column read from get-app-info can be sent back by adding the `action` verb. For an add, `title-localizations` is OPTIONAL: when omitted, `en-US` is auto-derived from a scalar `title`/`caption` or the column name (the `en-US` value must be English when supplied).")),
				Validators: [
					new ToolContractValidator(
						"sync-schemas-operations-localizations",
						InvalidLocalizationMapCode,
						Field: OperationsFieldName),
					new ToolContractValidator(
						"sync-schemas-virtual-entity-seed-rows",
						InvalidWorkflowShapeCode,
						Fields: [$"{OperationsFieldName}[*].{IsVirtualFieldName}", $"{OperationsFieldName}[*].seed-rows"],
						Context: "A create-entity operation cannot combine is-virtual=true with seed-rows because a virtual entity has no physical table.")
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, "Whether every sync-schemas operation succeeded."),
				Field("results", ArrayType, "Per-operation execution results keyed by canonical `type`.")
			),
			CommonErrorContract,
			EnvironmentPackageAliases(),
			[
				Default($"{OperationsFieldName}[*].{IsVirtualFieldName}", "false", "Create-entity operations create persistent schemas unless explicitly marked virtual.")
			],
			[
				Example("Create a lookup and extend the main entity", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[PackageNameFieldName] = ExamplePackageName,
					[OperationsFieldName] = new object[] {
						new Dictionary<string, object?> {
							["type"] = "create-lookup",
							[SchemaNameFieldName] = ExampleTaskStatusSchemaName,
							[TitleLocalizationsFieldName] = LocalizationMap("Task Status")
						},
						new Dictionary<string, object?> {
							["type"] = "update-entity",
							[SchemaNameFieldName] = ExamplePackageName,
							["update-operations"] = new object[] {
								new Dictionary<string, object?> {
									[ActionFieldName] = "add",
									[ColumnNameFieldName] = "UsrStatus",
									["type"] = "Lookup",
									[TitleLocalizationsFieldName] = LocalizationMap("Status"),
									[ReferenceSchemaNameFieldName] = ExampleTaskStatusSchemaName
								}
							}
						}
					}
				}),
				Example("Round-trip a get-app-info column: modify and remove using the read shape", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[PackageNameFieldName] = ExamplePackageName,
					[OperationsFieldName] = new object[] {
						new Dictionary<string, object?> {
							["type"] = "update-entity",
							[SchemaNameFieldName] = ExamplePackageName,
							["update-operations"] = new object[] {
								// modify a column read from get-app-info: identity as `name`, type as `data-value-type`
								new Dictionary<string, object?> {
									[ActionFieldName] = "modify",
									["name"] = "UsrStatus",
									["data-value-type"] = "Lookup",
									["reference-schema"] = ExampleTaskStatusSchemaName
								},
								// remove a column echoing only the read-shape `name`
								new Dictionary<string, object?> {
									[ActionFieldName] = "remove",
									["name"] = "UsrObsolete"
								}
							}
						}
					}
				})
			],
			Flow(
				[
					ApplicationCreateTool.ApplicationCreateToolName,
					SchemaSyncTool.ToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName
				],
				"Preferred over individual entity mutation tools for ordered create and update work."),
			[
				Flow(
					[
						CreateLookupTool.CreateLookupToolName,
						CreateDataBindingDbTool.CreateDataBindingDbToolName,
						UpdateEntitySchemaTool.UpdateEntitySchemaToolName,
						ApplicationGetInfoTool.ApplicationGetInfoToolName
					],
					"Fallback when the caller must execute individual entity mutation tools.")
			],
			[]);
	}

	private static ToolContractDefinition BuildPageSync() {
		return new ToolContractDefinition(
			PageSyncTool.ToolName,
			"Canonical page write path that batches page body validation, save, and optional read-back verification for one or more pages. Before editing page bodies or resource payloads, call get-guidance with name `page-modification` and use its checklist to choose specialized guidance.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, PagesFieldName],
				[
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription),
					Field(PagesFieldName, ArrayType, "Page update requests built from `get-page.raw.body`. Each page item requires `schema-name` and full `body`; optional `resources` is a JSON object string of localizable string key-value pairs the platform does NOT auto-provide (custom tab/group titles, button captions, validator messages, explicit caption overrides). Only include keys with NO matching DS-bound view model attribute on the page; matching keys are auto-provided by the platform \u2014 see `page-schema-resources` guidance. Each page item also accepts `optional-properties` (JSON array of {key, value} merged into schema optionalProperties)."),
					Field("validate", BooleanType, "Run client-side validation before save."),
					Field(VerifyFieldName, BooleanType, "Read the page back after save.")
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, "Whether every page operation succeeded."),
				Field(PagesFieldName, ArrayType, "Per-page results with `schema-name`, `success`, `body-length`, `validation`, `error`, `resources-registered`, optional `page` metadata, and optional `verified-body-file` when `verify=true`.")
			),
			CommonErrorContract,
			[],
			[
				Default("validate", "true", "Client-side validation is enabled by default."),
				Default(VerifyFieldName, BooleanFalseLiteral, "Read-back verification is optional and disabled by default.")
			],
			[
				Example("Validate and save one page body copied from get-page raw.body", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[PagesFieldName] = new object[] {
						new Dictionary<string, object?> {
							[SchemaNameFieldName] = "UsrTaskApp_FormPage",
							["body"] = "/* raw.body returned by get-page */ define(...)",
							[ResourcesFieldName] = "{\"UsrDetailsTab_caption\":\"Details\"}"
						}
					},
					["validate"] = true
				})
			],
			Flow(
				[
					PageListTool.ToolName,
					PageGetTool.ToolName,
					PageSyncTool.ToolName,
					PageGetTool.ToolName
				],
				"Canonical write path for page synchronization, including single-page saves when the caller wants the clio-advertised workflow."),
			[
				Flow(
					[
						PageListTool.ToolName,
						PageGetTool.ToolName,
						PageUpdateTool.ToolName,
						PageUpdateTool.ToolName,
						PageGetTool.ToolName
					],
					"Fallback when single-page dry-run or legacy save is required.")
			],
			[]);
	}

	private static ToolContractDefinition BuildPageList() {
		return new ToolContractDefinition(
			PageListTool.ToolName,
			"Lists Freedom UI pages for the requested package or installed app with schema, package, and parent schema context so the caller can discover candidate page schemas before inspection or mutation.",
			new ToolInputSchemaContract(
				[],
				EnvironmentOrExplicitConnectionFields(
					Field(PackageNameFieldName, StringType, "Package name to inspect."),
					Field(SelectorCodeFieldName, StringType, "Installed application code. When provided, list-pages resolves the application's primary package before querying pages."),
					Field(SearchPatternFieldName, StringType, "Optional case-insensitive schema-name filter."),
					Field(LimitFieldName, NumberType, "Optional max result count. Omit or pass 0 to use the default of 50. A negative limit is rejected with success:false (it must not disable the cap).")),
				Validators: [
					new ToolContractValidator(
						"mutually-exclusive-fields",
						InvalidWorkflowShapeCode,
						Fields: [
							PackageNameFieldName,
							SelectorCodeFieldName
						],
						Context: "list-pages accepts package-name or code, not both."),
					new ToolContractValidator(
						LimitFieldName, "invalid-limit", LimitFieldName,
						Context: "limit must be zero or greater; 0 (or omitting it) uses the default of 50, and a negative value is rejected with success:false."),
				],
				AnyOf: EnvironmentOrExplicitConnectionRequirements()),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, ToolSucceededDescription),
				Field(CountFieldName, NumberType, "Number of pages returned (after the result cap is applied)."),
				Field("total", NumberType, "Total pages matching the query before the cap. Compare to count to detect an incomplete result."),
				Field("truncated", BooleanType, "True when total is greater than count, meaning more pages match than were returned. Raise limit or add a filter to retrieve the rest."),
				Field(PagesFieldName, ArrayType, "Discovered pages using `schema-name`, `uId`, `packageName`, and `parentSchemaName`."),
				Field(ErrorFieldName, StringType, FailureMessageDescription)
			),
			CommonErrorContract,
				[
					PackageNameParameterAlias(),
					Alias(ParameterScope, "code", AppCodeFieldName, RejectedStatus, $"Use 'code' instead of '{AppCodeFieldName}'."),
					Alias(ParameterScope, SearchPatternFieldName, "searchPattern", RejectedStatus, $"Use '{SearchPatternFieldName}' instead of 'searchPattern'."),
					EnvironmentNameParameterAlias()
				],
			[],
			[
				Example("List pages in the target package", new Dictionary<string, object?> {
					[PackageNameFieldName] = ExamplePackageName,
					[EnvironmentNameFieldName] = ExampleEnvironmentName
				}),
				Example("List pages for an installed app code", new Dictionary<string, object?> {
					[SelectorCodeFieldName] = ExamplePackageName,
					[EnvironmentNameFieldName] = ExampleEnvironmentName
				})
			],
			Flow(
				[
					PageListTool.ToolName,
					PageGetTool.ToolName,
					PageSyncTool.ToolName,
					PageGetTool.ToolName
				],
				"Use when the page schema is not yet known and the workflow should follow the canonical clio page path."),
			[
				Flow(
						[
							PageListTool.ToolName,
							PageGetTool.ToolName,
							PageUpdateTool.ToolName,
							PageGetTool.ToolName
						],
					"Fallback when single-page dry-run or legacy save is required after discovery.")
			],
			[]);
	}

	private static ToolContractDefinition BuildPageGet() {
		return new ToolContractDefinition(
			PageGetTool.ToolName,
			"Reads a Freedom UI page bundle plus the raw editable body so the caller can inspect before mutating and edit `raw.body` directly when saving. Before editing `raw.body`, call get-guidance with name `page-modification` and use its checklist to choose specialized guidance.",
			new ToolInputSchemaContract(
				[SchemaNameFieldName],
				EnvironmentOrExplicitConnectionFields(
					Field(SchemaNameFieldName, StringType, "Freedom UI page schema name.")),
				AnyOf: EnvironmentOrExplicitConnectionRequirements()),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, ToolSucceededDescription),
				Field("page", ObjectType, "Page metadata carrying schema and package identity such as schemaName, schemaUId, packageName, packageUId, and parentSchemaName."),
				Field("bundle", ObjectType, "Merged page bundle."),
				Field("raw", ObjectType, "Raw editable payload. The JavaScript source to edit and round-trip through update-page/sync-pages is `raw.body`."),
				Field(ErrorFieldName, StringType, FailureMessageDescription)
			),
			CommonErrorContract,
			[
				SchemaNameParameterAlias(),
				EnvironmentNameParameterAlias()
			],
			[],
			[
				Example("Read an existing FormPage body", new Dictionary<string, object?> {
					[SchemaNameFieldName] = "UsrTaskApp_FormPage",
					[EnvironmentNameFieldName] = ExampleEnvironmentName
				})
			],
			Flow(
				[
					PageListTool.ToolName,
					PageGetTool.ToolName,
					PageSyncTool.ToolName,
					PageGetTool.ToolName
				],
				"Use after list-pages to inspect `raw.body` before following the canonical page write path and to read back after saving."),
			[
				Flow(
					[
						PageGetTool.ToolName,
						ComponentInfoTool.ToolName,
						PageSyncTool.ToolName,
						PageGetTool.ToolName
					],
					"Call get-component-info before editing when bundle.viewConfig contains unfamiliar crt.* component types."),
				Flow(
					[
						PageGetTool.ToolName,
						PageUpdateTool.ToolName,
						PageGetTool.ToolName
					],
					"Fallback when the caller explicitly needs single-page dry-run or legacy save behavior.")
			],
			[]);
	}

	private static ToolContractDefinition BuildCreateLookup() {
		return new ToolContractDefinition(
			CreateLookupTool.CreateLookupToolName,
			"Creates a BaseLookup schema directly in the target package.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, PackageNameFieldName, SchemaNameFieldName, TitleLocalizationsFieldName],
				EnvironmentPackageSchemaFields(
					"Lookup schema name.",
					Field(TitleLocalizationsFieldName, ObjectType, "Localization map that must include en-US."),
					Field(ColumnsFieldName, ArrayType, "Optional custom columns.")),
				Validators: [
					RequiredLocalizationMapValidator(TitleLocalizationsFieldName)
				]),
			CommandExecutionOutput(),
			CommonErrorContract,
			EnvironmentPackageSchemaAliases(
				TitleParameterAlias(),
				CaptionParameterAlias()),
			[],
			[
				Example("Create a lookup schema", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[PackageNameFieldName] = ExamplePackageName,
					[SchemaNameFieldName] = ExampleTaskStatusSchemaName,
					[TitleLocalizationsFieldName] = LocalizationMap("Task Status")
				})
			],
			PreferSchemaSyncFlow(),
			[],
			PreferSchemaSyncDeprecations(CreateLookupTool.CreateLookupToolName));
	}

	private static ToolContractDefinition BuildCreateEntity() {
		return new ToolContractDefinition(
			CreateEntitySchemaTool.CreateEntitySchemaToolName,
			"Creates an entity schema directly in the target package.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, PackageNameFieldName, SchemaNameFieldName, TitleLocalizationsFieldName],
				EnvironmentPackageSchemaFields(
					EntitySchemaNameDescription,
					Field(TitleLocalizationsFieldName, ObjectType, "Localization map that must include en-US."),
					Field("columns", ArrayType, "Optional initial columns."),
					Field(ParentSchemaNameFieldName, StringType, "Optional parent schema name."),
					Field("extend-parent", BooleanType, "Optional replacement-schema flag."),
					Field(IsVirtualFieldName, BooleanType, "Creates a virtual entity schema without a physical database table when true.")),
				Validators: [
					RequiredLocalizationMapValidator(TitleLocalizationsFieldName)
				]),
			CommandExecutionOutput(),
			CommonErrorContract,
			EnvironmentPackageSchemaAliases(
				Alias(ParameterScope, ParentSchemaNameFieldName, "parentSchemaName", RejectedStatus, $"Use '{ParentSchemaNameFieldName}' instead of 'parentSchemaName'."),
				Alias(ParameterScope, "extend-parent", "extendParent", RejectedStatus, "Use 'extend-parent' instead of 'extendParent'."),
				TitleParameterAlias(),
				CaptionParameterAlias()),
			[
				Default(IsVirtualFieldName, "false", "Entity schemas are persistent unless explicitly marked virtual.")
			],
			[
				Example("Create an additional business entity", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[PackageNameFieldName] = ExamplePackageName,
					[SchemaNameFieldName] = "UsrTaskComment",
					[TitleLocalizationsFieldName] = LocalizationMap("Task Comment"),
					[ParentSchemaNameFieldName] = "BaseEntity"
				})
			],
			PreferSchemaSyncFlow(),
			[],
			PreferSchemaSyncDeprecations(CreateEntitySchemaTool.CreateEntitySchemaToolName));
	}

	private static ToolContractDefinition BuildUpdateEntity() {
		return new ToolContractDefinition(
			UpdateEntitySchemaTool.UpdateEntitySchemaToolName,
			"Applies explicit add, modify, or remove column operations to an entity schema when the target schema is already known.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, PackageNameFieldName, SchemaNameFieldName, OperationsFieldName],
				EnvironmentPackageSchemaFields(
					EntitySchemaNameDescription,
					Field(OperationsFieldName, ArrayType, "Explicit column mutation operations.")),
				Validators: [
					new ToolContractValidator("update-operations-localizations", InvalidLocalizationMapCode, OperationsFieldName)
				]),
			CommandExecutionOutput(),
			CommonErrorContract,
			EnvironmentPackageSchemaAliases(),
			[],
			[
				Example("Add a lookup column to an existing entity", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[PackageNameFieldName] = ExamplePackageName,
					[SchemaNameFieldName] = ExamplePackageName,
					[OperationsFieldName] = new object[] {
						new Dictionary<string, object?> {
							[ActionFieldName] = "add",
							[ColumnNameFieldName] = "UsrStatus",
							["type"] = "Lookup",
							[TitleLocalizationsFieldName] = LocalizationMap("Status"),
							[ReferenceSchemaNameFieldName] = ExampleTaskStatusSchemaName
						}
					}
				})
			],
			Flow(
				[
					GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName,
					UpdateEntitySchemaTool.UpdateEntitySchemaToolName,
					GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName
				],
				"Use for explicit multi-column mutations within one existing schema when the caller wants read-before-write and read-back verification."),
			[
				PreferSchemaSyncFlow(),
				Flow(
					[
						GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName,
						ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName,
						GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName
					],
					"Fallback when the requested change is only one isolated column mutation.")
			],
			[]);
	}

	private static ToolContractDefinition BuildCreateDataBindingDb() {
		return new ToolContractDefinition(
			CreateDataBindingDbTool.CreateDataBindingDbToolName,
			"Creates or updates a DB-first package data binding and optionally applies rows immediately as an explicit fallback or standalone path outside a batched sync-schemas flow. SaveSchema metadata is projected from the primary key plus columns referenced by currently bound or requested rows, so unrelated runtime-only columns are not blockers, while explicitly requested unsupported runtime columns still fail. For workflow selection, call get-guidance with name `data-bindings`.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, PackageNameFieldName, SchemaNameFieldName],
				EnvironmentPackageSchemaFields(
					"Entity schema name for the binding.",
					Field(BindingNameFieldName, StringType, "Optional binding name; defaults to the schema name."),
					Field("rows", StringType,
						"Optional JSON array of row objects. Each row must contain a values object keyed by column name. Binding metadata is projected from the primary key plus referenced row columns."))),
			CommandExecutionOutput(),
			CommonErrorContract,
			EnvironmentPackageSchemaAliases(),
			[],
			[
				Example("Create lookup seed rows without leaving the MCP data-binding surface", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[PackageNameFieldName] = ExamplePackageName,
					[SchemaNameFieldName] = ExampleTaskStatusSchemaName,
					["rows"] = "[{\"values\":{\"Name\":\"New\"}},{\"values\":{\"Name\":\"In Progress\"}}]"
				})
			],
			Flow([SchemaSyncTool.ToolName], "Prefer inline seed-rows inside sync-schemas when the flow can stay batched."),
			[],
			[
				new ToolDeprecation(
					"Prefer sync-schemas with inline seed-rows as the canonical batched path. Keep create-data-binding-db for explicit fallback or standalone binding work, and prefer it over direct SQL when MCP callers still need lookup seed rows. For broader behavior-level guidance, call get-guidance with name `data-bindings`.",
					[
						SchemaSyncTool.ToolName
					])
			]);
	}

	private static ToolContractDefinition BuildUpsertDataBindingRowDb() {
		return new ToolContractDefinition(
			UpsertDataBindingRowDbTool.UpsertDataBindingRowDbToolName,
			"Upserts a single row in an existing DB-first binding. " +
			"The binding must already exist; call create-data-binding-db first if it does not. " +
			"SaveSchema metadata is rebuilt from the primary key plus columns present in the bound rows and the requested upsert payload. " +
			"For workflow selection and verification discipline, call get-guidance with name `data-bindings`.",
			new ToolInputSchemaContract(
					[EnvironmentNameFieldName, PackageNameFieldName, BindingNameFieldName, ValuesFieldName],
					EnvironmentPackageFields(
						Field(BindingNameFieldName, StringType, BindingNameDescription),
						Field(ValuesFieldName, StringType, "JSON object keyed by column name. Referenced columns become part of the projected binding metadata."))),
			CommandExecutionOutput(),
			new ToolErrorContract([
				..CommonErrorContract.Codes,
				new ToolErrorCodeContract("binding-not-found",
					"The specified binding does not exist in the remote environment. " +
					"Create it first with create-data-binding-db.")
			]),
			EnvironmentPackageAliases(
				BindingNameParameterAlias()),
			[],
			[
				Example("Upsert one binding row", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[PackageNameFieldName] = ExamplePackageName,
					[BindingNameFieldName] = ExampleTaskStatusSchemaName,
						[ValuesFieldName] = "{\"Name\":\"New\"}"
				})
			],
			Flow(["create-data-binding-db", UpsertDataBindingRowDbTool.UpsertDataBindingRowDbToolName],
				"create-data-binding-db → upsert-data-binding-row-db: create the binding first, then upsert individual rows."),
			[],
			[]);
	}

	private static ToolContractDefinition BuildRemoveDataBindingRowDb() {
		return new ToolContractDefinition(
			RemoveDataBindingRowDbTool.RemoveDataBindingRowDbToolName,
			"Removes a single row from an existing DB-first binding by key value, and deletes the package schema data record when the removed row was the last bound row. When rows remain, SaveSchema metadata is rebuilt from the primary key plus the columns present in the remaining bound rows. For workflow selection and verification discipline, call get-guidance with name `data-bindings`.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, PackageNameFieldName, BindingNameFieldName, KeyValueFieldName],
				EnvironmentPackageFields(
						Field(BindingNameFieldName, StringType, BindingNameDescription),
						Field(KeyValueFieldName, StringType, "Primary-key value of the row to remove."))),
			CommandExecutionOutput(),
			CommonErrorContract,
			[
				..EnvironmentPackageAliases(
					BindingNameParameterAlias()),
				Alias(ParameterScope, KeyValueFieldName, "keyValue", RejectedStatus, $"Use '{KeyValueFieldName}' instead of 'keyValue'.")
			],
			[],
			[
				Example("Remove one binding row", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[PackageNameFieldName] = ExamplePackageName,
					[BindingNameFieldName] = ExampleTaskStatusSchemaName,
					[KeyValueFieldName] = ExampleLookupValueId
				})
			],
			Flow([RemoveDataBindingRowDbTool.RemoveDataBindingRowDbToolName], "Standalone DB-first binding maintenance."),
			[],
			[]);
	}

	private static ToolContractDefinition BuildCreateDataBinding() {
		return new ToolContractDefinition(
			CreateDataBindingTool.CreateDataBindingToolName,
			"Creates or regenerates a local package data binding from a built-in template or a runtime entity schema. For workflow selection, call get-guidance with name `data-bindings`.",
			new ToolInputSchemaContract(
				[PackageNameFieldName, SchemaNameFieldName, WorkspacePathFieldName],
				[
					Field(EnvironmentNameFieldName, StringType, "Registered clio environment name. Required when schema-name is not SysSettings because the MCP tool does not expose a uri fallback."),
						Field(PackageNameFieldName, StringType, PackageNameDescription),
						Field(SchemaNameFieldName, StringType, "Entity schema name for the binding. The built-in offline template currently includes SysSettings."),
						Field(WorkspacePathFieldName, StringType, WorkspacePathDescription),
						Field(BindingNameFieldName, StringType, "Optional binding name; defaults to the schema name."),
						Field("install-type", NumberType, "Optional descriptor install type; defaults to 0."),
						Field(ValuesFieldName, StringType, "Optional JSON object keyed by column name for the initial row."),
						Field("localizations", StringType, "Optional JSON object keyed by culture then column name.")
					],
					Validators: [
						new ToolContractValidator(
							"require-environment-name-for-runtime-schema",
							MissingRequiredParameterCode,
						Fields: [
							SchemaNameFieldName,
							EnvironmentNameFieldName
						],
						Context: "`environment-name` is required when `schema-name` is not `SysSettings` because this MCP tool only supports offline generation for built-in templates and does not expose `--uri`.",
						Required: true)
				]),
			CommandExecutionOutput(),
			CommonErrorContract,
			[
				EnvironmentNameParameterAlias(),
				PackageNameParameterAlias(),
				SchemaNameParameterAlias(),
				BindingNameParameterAlias(),
				WorkspacePathParameterAlias(),
				Alias(ParameterScope, "install-type", "installType", RejectedStatus, "Use 'install-type' instead of 'installType'.")
			],
			[
				Default("install-type", "0", "Use descriptor install type 0 unless the workflow requires a different value.")
			],
			[
				Example("Create a local SysSettings binding artifact", new Dictionary<string, object?> {
					[PackageNameFieldName] = ExamplePackageName,
					[SchemaNameFieldName] = "SysSettings",
						[WorkspacePathFieldName] = ExampleWorkspacePath,
						[ValuesFieldName] = "{\"Code\":\"UsrTaskSetting\",\"Name\":\"Task setting\"}"
					}),
					Example("Create a local binding for a non-templated schema", new Dictionary<string, object?> {
						[EnvironmentNameFieldName] = ExampleEnvironmentName,
						[PackageNameFieldName] = ExamplePackageName,
						[SchemaNameFieldName] = ExampleTaskStatusSchemaName,
						[WorkspacePathFieldName] = ExampleWorkspacePath
					})
				],
			Flow(
				[
					CreateDataBindingTool.CreateDataBindingToolName,
					AddDataBindingRowTool.AddDataBindingRowToolName
				],
				"Use when the workflow explicitly needs a local binding artifact under the workspace."),
			[
				Flow(
					[
						SchemaSyncTool.ToolName
					],
					"Fallback to sync-schemas when lookup seeding can stay inside the current schema batch."),
				Flow(
					[
						CreateDataBindingDbTool.CreateDataBindingDbToolName
					],
					"Fallback to create-data-binding-db when the desired outcome is a remote DB-first binding rather than a local artifact.")
			],
			[]);
	}

	private static ToolContractDefinition BuildAddDataBindingRow() {
		return new ToolContractDefinition(
			AddDataBindingRowTool.AddDataBindingRowToolName,
			"Adds or replaces one row in an existing local package data binding. For workflow selection and verification discipline, call get-guidance with name `data-bindings`.",
			new ToolInputSchemaContract(
					[PackageNameFieldName, BindingNameFieldName, WorkspacePathFieldName, ValuesFieldName],
					[
						Field(PackageNameFieldName, StringType, PackageNameDescription),
						Field(BindingNameFieldName, StringType, BindingNameDescription),
						Field(WorkspacePathFieldName, StringType, WorkspacePathDescription),
						Field(ValuesFieldName, StringType, "JSON object keyed by column name for the row to add or replace."),
						Field("localizations", StringType, "Optional JSON object keyed by culture then column name.")
					]),
			CommandExecutionOutput(),
			CommonErrorContract,
			[
				PackageNameParameterAlias(),
				BindingNameParameterAlias(),
				WorkspacePathParameterAlias()
			],
			[],
			[
				Example("Add one row to an existing local binding", new Dictionary<string, object?> {
					[PackageNameFieldName] = ExamplePackageName,
					[BindingNameFieldName] = ExampleTaskStatusSchemaName,
					[WorkspacePathFieldName] = ExampleWorkspacePath,
					[ValuesFieldName] = "{\"Name\":\"In Progress\"}"
				})
			],
			Flow(
				[
					CreateDataBindingTool.CreateDataBindingToolName,
					AddDataBindingRowTool.AddDataBindingRowToolName
				],
				"Local artifact flow: create the binding first, then add or replace rows."),
			[],
			[]);
	}

	private static ToolContractDefinition BuildRemoveDataBindingRow() {
		return new ToolContractDefinition(
			RemoveDataBindingRowTool.RemoveDataBindingRowToolName,
			"Removes one row from an existing local package data binding by key value. For workflow selection and verification discipline, call get-guidance with name `data-bindings`.",
			new ToolInputSchemaContract(
				[PackageNameFieldName, BindingNameFieldName, WorkspacePathFieldName, KeyValueFieldName],
				[
						Field(PackageNameFieldName, StringType, PackageNameDescription),
						Field(BindingNameFieldName, StringType, BindingNameDescription),
						Field(WorkspacePathFieldName, StringType, WorkspacePathDescription),
						Field(KeyValueFieldName, StringType, "Primary-key value of the row to remove.")
					]),
			CommandExecutionOutput(),
			CommonErrorContract,
			[
				PackageNameParameterAlias(),
				BindingNameParameterAlias(),
				WorkspacePathParameterAlias(),
				Alias(ParameterScope, KeyValueFieldName, "keyValue", RejectedStatus, $"Use '{KeyValueFieldName}' instead of 'keyValue'.")
			],
			[],
			[
				Example("Remove one row from a local binding", new Dictionary<string, object?> {
					[PackageNameFieldName] = ExamplePackageName,
					[BindingNameFieldName] = ExampleTaskStatusSchemaName,
					[WorkspacePathFieldName] = ExampleWorkspacePath,
					[KeyValueFieldName] = ExampleLookupValueId
				})
			],
			Flow([RemoveDataBindingRowTool.RemoveDataBindingRowToolName], "Standalone local binding maintenance."),
			[],
			[]);
	}

	private static ToolContractDefinition BuildGetEntitySchemaProperties() {
		return new ToolContractDefinition(
			GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName,
			"Returns a structured summary of entity schema metadata for read-before-write inspection and read-back verification.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, PackageNameFieldName, SchemaNameFieldName],
				EnvironmentPackageSchemaFields(EntitySchemaNameDescription)),
			StructuredResultOutput(
				Field("name", StringType, "Schema name."),
				Field("title", StringType, "Schema title."),
				Field("virtual", BooleanType, "Whether the entity schema is virtual and has no physical database table."),
				Field(ColumnsFieldName, ArrayType, "Column metadata.")),
			CommonErrorContract,
			EnvironmentPackageSchemaAliases(),
			[],
			[
				Example("Read deployed schema properties", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[PackageNameFieldName] = ExamplePackageName,
					[SchemaNameFieldName] = ExamplePackageName
				})
			],
			Flow(
				[
					GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName,
					ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName,
					GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName
				],
				"Use to inspect an existing schema before mutation and to verify the deployed shape after a minimal change."),
			[
				Flow(
					[
						GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName,
						SchemaSyncTool.ToolName,
						GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName
					],
					"Fallback when the schema change is part of a larger ordered sync-schemas workflow.")
			],
			[]);
	}

	private static ToolContractDefinition BuildGetEntitySchemaColumnProperties() {
		return new ToolContractDefinition(
			GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName,
			"Returns detailed metadata for one deployed entity schema column for read-before-write inspection and read-back verification. For a lookup column with a Const default, default-value-config is enriched with display-value (the referenced record's display value) or a record-resolution marker (no-access, not-found-or-no-access, display-column-unavailable) when it cannot be resolved.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, PackageNameFieldName, SchemaNameFieldName, ColumnNameFieldName],
				EnvironmentPackageSchemaFields(
					EntitySchemaNameDescription,
					Field(ColumnNameFieldName, StringType, "Column name."))),
			StructuredResultOutput(
				Field("name", StringType, "Column name."),
				Field("data-value-type", StringType, "Column type."),
				Field("source", StringType, "Column source.")),
			CommonErrorContract,
			EnvironmentPackageSchemaAliases(
				ColumnNameParameterAlias()),
			[],
			[
				Example("Read one deployed column", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[PackageNameFieldName] = ExamplePackageName,
					[SchemaNameFieldName] = ExamplePackageName,
					[ColumnNameFieldName] = "UsrStatus"
				})
			],
			Flow(
				[
					GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName,
					ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName,
					GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName
				],
				"Use when the change is scoped to one existing column and the caller wants read-before-write plus read-back verification."),
			[
				Flow(
					[
						GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName,
						SchemaSyncTool.ToolName,
						GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName
					],
					"Fallback when the requested work expands into a larger ordered sync-schemas plan.")
			],
			[]);
	}

	private static ToolContractDefinition BuildModifyEntitySchemaColumn() {
		return new ToolContractDefinition(
			ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName,
			"Adds, modifies, or removes one entity schema column directly for minimal existing-schema edits.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, PackageNameFieldName, SchemaNameFieldName, ActionFieldName, ColumnNameFieldName],
				EnvironmentPackageSchemaFields(
					EntitySchemaNameDescription,
					Field(ActionFieldName, StringType, "Column action: add, modify, or remove."),
					Field(ColumnNameFieldName, StringType, "Column name."),
					Field("type", StringType, "Optional column data type."),
					Field(TitleLocalizationsFieldName, ObjectType, "Optional localization map."),
					Field(DescriptionLocalizationsFieldName, ObjectType, "Optional localization map."),
					Field(ReferenceSchemaNameFieldName, StringType, "Optional lookup target."),
					Field("required", BooleanType, "Optional required flag."),
					Field("default-value-source", StringType, "Legacy optional default source shorthand. Supports only Const or None."),
					Field("default-value", StringType, "Legacy optional default value shorthand for Const."),
					Field(DefaultValueConfigFieldName, ObjectType, "Structured default value metadata with source None, Const, Settings, SystemValue, or Sequence. Settings value-source accepts code/name/id and resolves to code. SystemValue value-source accepts GUID/alias/caption and resolves to GUID. For a lookup column, a Const value is the referenced record GUID and is validated to exist in the referenced schema before save (an unknown GUID is rejected)."))),
			CommandExecutionOutput(),
			CommonErrorContract,
			EnvironmentPackageSchemaAliases(
				ColumnNameParameterAlias(),
				ReferenceSchemaNameParameterAlias(),
				DefaultValueParameterAlias(),
				DefaultValueConfigParameterAlias(),
				DefaultValueSourceParameterAlias(),
				TitleParameterAlias(),
				CaptionParameterAlias(),
				DescriptionParameterAlias()),
			[],
			[
				Example("Add one required text column", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[PackageNameFieldName] = ExamplePackageName,
					[SchemaNameFieldName] = ExamplePackageName,
					[ActionFieldName] = "add",
					[ColumnNameFieldName] = "UsrShortCode",
					["type"] = "Text",
					[TitleLocalizationsFieldName] = LocalizationMap("Short Code"),
					["required"] = true
				}),
				Example("Set a system-value default on one column", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[PackageNameFieldName] = ExamplePackageName,
					[SchemaNameFieldName] = ExamplePackageName,
					[ActionFieldName] = "modify",
					[ColumnNameFieldName] = "UsrStartDate",
					[DefaultValueConfigFieldName] = new Dictionary<string, object?> {
						[DefaultValueConfigSourceKey] = "SystemValue",
						["value-source"] = "Current Time and Date"
					}
				}),
				Example("Set a lookup-record Const default (GUID validated before save)", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[PackageNameFieldName] = ExamplePackageName,
					[SchemaNameFieldName] = ExamplePackageName,
					[ActionFieldName] = "modify",
					[ColumnNameFieldName] = "UsrColor",
					[DefaultValueConfigFieldName] = new Dictionary<string, object?> {
						[DefaultValueConfigSourceKey] = ConstDefaultValueSourceName,
						["value"] = "d1a6ea58-6a88-4cb7-bfea-7a41caa0ae50"
					}
				})
			],
			Flow(
				[
					GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName,
					ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName,
					GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName
				],
				"Use for a single-column mutation when the caller can inspect current metadata first and verify the column again after saving."),
			[
				Flow(
					[
						GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName,
						ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName,
						GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName
					],
					"Fallback when schema-level verification is more useful than column-level read-back."),
				PreferSchemaSyncFlow()
			],
			[]);
	}

	private static ToolContractDefinition BuildComponentInfo() {
		return new ToolContractDefinition(
			ComponentInfoTool.ToolName,
			"Returns a flat list of Freedom UI component summaries, the full contract for one component type, or the assembly recipe for a composite Designer element.",
			new ToolInputSchemaContract(
				[],
				[
					Field(ComponentTypeFieldName, StringType, "Freedom UI component type, e.g. 'crt.TabContainer'. Omit or use 'list' to return the catalog (list mode); a known type returns that one component's full contract (detail mode); an unknown type returns a bounded suggestion shortlist. Mutually exclusive with 'composite'."),
					Field("composite", StringType, "Composite Designer element caption, for example 'Expanded list' or 'Next steps'. Returns the composite's assembly docs — a composite is a pre-built combination of several components with NO componentType of its own. Discover available captions via list mode (composites section). Mutually exclusive with 'component-type'."),
					Field("search", StringType, "Optional keyword filter applied in list mode and to not-found suggestions, e.g. 'tab'."),
					Field("schema-type", StringType, "Component registry to query: 'web' (default) or 'mobile'. The mobile registry is separate (crt.Toggle, crt.BarcodeScanner, crt.Sort, ...) and excludes web-only types."),
					Field(EnvironmentNameFieldName, StringType, "PREFERRED. Registered environment name; scopes the catalog to its real platform version. Mutually exclusive with 'version'."),
					Field("version", StringType, "Explicit catalog version (3-part semver, e.g. '8.3.3'). Mutually exclusive with 'environment-name'."),
					Field("uri", StringType, "Emergency fallback only: direct application URI. Prefer 'environment-name'."),
					Field(LoginFieldName, StringType, "Emergency fallback only: login paired with 'uri'."),
					Field(PasswordFieldName, StringType, "Emergency fallback only: password paired with 'uri'.")
				],
				Validators: [
					new ToolContractValidator(
						"mutually-exclusive",
						InvalidWorkflowShapeCode,
						Fields: [ComponentTypeFieldName, "composite"],
						Context: "'component-type' and 'composite' are mutually exclusive — pass one or the other, not both.")
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, ToolSucceededDescription),
				Field("mode", StringType, "detail, list, or composite."),
				Field(CountFieldName, NumberType, "Number of matching components or composites."),
				Field("items", ArrayType, "Flat list-mode component summaries, each with componentType and an optional description."),
				Field("composites", ArrayType, "Composite Designer elements in list mode, each with caption and optional description. Fetch a composite's assembly recipe with composite=\"<caption>\"."),
				Field("caption", StringType, "Composite caption echoed back in composite detail mode."),
				Field("documentation", StringType, "Composite assembly recipe markdown in composite detail mode."),
				Field("componentType", StringType, "Component type echoed back in component detail mode."),
				Field("resolvedTargetVersion", StringType, "Catalog version the response was filtered against."),
				Field("resolvedFrom", StringType, "Resolver tier that produced the version: 'environment' (known, exact), 'environment-superset' (known version, approximate catalog — soft caveat), or 'latest-fallback' (version unknown — hard stop)."),
				Field("versionWarning", StringType, "Prose caveat present on 'environment-superset' (soft) and 'latest-fallback' (hard stop); omitted on 'environment'."),
				Field("requiresVersionConfirmation", BooleanType, "Machine-readable hard stop, true only on 'latest-fallback': the version is unknown — tell the user and request explicit confirmation before proceeding instead of assuming the 'latest' superset. Omitted otherwise."),
				Field("resolvedFromReason", StringType, "Why the version fell back, present only on 'latest-fallback': 'probe-error' (transient — a retry/reachable environment may help) or the stable 'no-active-environment' / 'core-version-missing' / 'core-version-unparseable'."),
				Field(ErrorFieldName, StringType, FailureMessageDescription)
			),
			CommonErrorContract,
			[
				Alias(ParameterScope, ComponentTypeFieldName, "componentType", RejectedStatus, "Use 'component-type' instead of 'componentType'."),
				Alias(ParameterScope, ComponentTypeFieldName, "component-name", RejectedStatus, "Use 'component-type' instead of 'component-name'."),
				Alias(ParameterScope, ComponentTypeFieldName, "componentName", RejectedStatus, "Use 'component-type' instead of 'componentName'."),
				Alias(ParameterScope, ComponentTypeFieldName, "component_name", RejectedStatus, "Use 'component-type' instead of 'component_name'."),
				Alias(ParameterScope, "schema-type", "schemaType", RejectedStatus, "Use 'schema-type' instead of 'schemaType'."),
				Alias(ParameterScope, EnvironmentNameFieldName, "environmentName", RejectedStatus, "Use 'environment-name' instead of 'environmentName'.")
			],
			[],
			[
				Example("Inspect one component contract", new Dictionary<string, object?> {
					[ComponentTypeFieldName] = "crt.TabContainer"
				}),
				Example("List the full component catalog", new Dictionary<string, object?>()),
				Example("Inspect a mobile component contract", new Dictionary<string, object?> {
					[ComponentTypeFieldName] = "crt.Toggle",
					["schema-type"] = "mobile"
				}),
				Example("Get the assembly recipe for a composite Designer element", new Dictionary<string, object?> {
					["composite"] = "Expanded list"
				})
			],
			Flow([ComponentInfoTool.ToolName],
				"Use after get-page when bundle.viewConfig contains unfamiliar crt.* component types. "
				+ "Use with composite=\"<caption>\" (not component-type) to get the authoritative assembly recipe for a composite Designer element "
				+ "(e.g. 'Expanded list', 'Attachments', 'Next steps') — composites have no componentType and must be fetched by caption."),
			[],
			[],
			[
				new ToolAntiPattern(
					"Hand-building a composite structure from memory, raw component docs, or guidance articles",
					"The 'documentation' field of a composite detail response contains the complete, authoritative assembly recipe. "
					+ "Do NOT synthesize the structure from memory or other sources — those are incomplete and will produce a broken result. "
					+ "Follow the documentation field verbatim.")
			]);
	}

	private static ToolContractDefinition BuildPageUpdate() {
		return new ToolContractDefinition(
			PageUpdateTool.ToolName,
			"Fallback single-page save path for a full Freedom UI page body copied from `get-page.raw.body` when the workflow explicitly requires dry-run or legacy save behavior.",
			new ToolInputSchemaContract(
				[SchemaNameFieldName],
				EnvironmentOrExplicitConnectionFields(
					Field(SchemaNameFieldName, StringType, "Freedom UI page schema name."),
					Field("body", StringType, "Full page body with all marker pairs. Reuse `get-page.raw.body` rather than `bundle` or `bundle.viewConfig`. Either `body` or `body-file` must be provided."),
					Field("body-file", StringType, "Absolute path to a file containing the page body. Used when `body` is empty. Enables passing large bodies without inline JSON escaping."),
					Field(DryRunFieldName, BooleanType, "Validate without saving."),
					Field(ResourcesFieldName, StringType, "Optional JSON object string of localizable strings the platform does NOT auto-provide (custom tab/group titles, button captions, validator messages, explicit overrides). Only include keys with NO matching DS-bound view model attribute on the page \u2014 see `page-schema-resources` guidance."),
					Field("optional-properties", StringType, "JSON array of {key, value} objects merged into schema optionalProperties (e.g. '[{\"key\":\"entitySchemaName\",\"value\":\"UsrMyEntity\"}]')."),
					Field(VerifyFieldName, BooleanType, "If true, read the page back after saving and return its metadata. Best-effort \u2014 verify failure does not fail the update."),
					Field("mode", StringType, "Write mode. 'replace' (default) saves the body verbatim. 'append' merges the incoming fragment with the schema's current body \u2014 viewConfigDiff entries dedupe by `name` (incoming wins), handlers dedupe by `request`."),
					Field("target-package-uid", StringType, "Explicit target package UId for the replacing schema. Overrides automatic design-package resolution."),
					Field("target-schema-uid", StringType, "Explicit schema UId to save into directly. Bypasses hierarchy resolution entirely.")),
				AnyOf: EnvironmentOrExplicitConnectionRequirements()),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, ToolSucceededDescription),
				Field("schemaName", StringType, "Page schema name."),
				Field("bodyLength", NumberType, "Saved body length."),
				Field("dryRun", BooleanType, "Whether the call ran in validation mode."),
				Field("resourcesRegistered", NumberType, "Number of registered resources."),
				Field(ErrorFieldName, StringType, FailureMessageDescription)
			),
			CommonErrorContract,
			[
				SchemaNameParameterAlias(),
				EnvironmentNameParameterAlias(),
				Alias(ParameterScope, DryRunFieldName, "dryRun", RejectedStatus, "Use 'dry-run' instead of 'dryRun'.")
			],
			[
				Default(DryRunFieldName, BooleanFalseLiteral, "Saves by default; pass true to validate without writing."),
				Default(VerifyFieldName, BooleanFalseLiteral, "Read-back verification is optional and disabled by default."),
				Default("mode", "replace", "Body is written verbatim by default; pass 'append' to merge with the existing body.")
			],
			[
				Example("Dry-run validate one page body copied from get-page raw.body", new Dictionary<string, object?> {
					[SchemaNameFieldName] = "UsrTaskApp_FormPage",
					["body"] = "/* raw.body returned by get-page */ define(...)",
					[ResourcesFieldName] = "{\"UsrDetailsTab_caption\":\"Details\"}",
					[DryRunFieldName] = true,
					[EnvironmentNameFieldName] = ExampleEnvironmentName
				})
			],
			Flow(
				[
					PageGetTool.ToolName,
					PageUpdateTool.ToolName,
					PageGetTool.ToolName
				],
				"Use only when the workflow explicitly requires single-page dry-run or legacy save behavior after reading the raw body with get-page."),
			[
				Flow(
					[
						PageListTool.ToolName,
						PageGetTool.ToolName,
						PageUpdateTool.ToolName,
						PageGetTool.ToolName
					],
					"Fallback when the schema name must be discovered first before a single-page edit."),
				Flow(
					[
						PageListTool.ToolName,
						PageGetTool.ToolName,
						PageSyncTool.ToolName,
						PageGetTool.ToolName
					],
					"Fallback when the workflow expands into a multi-page save or ordered sync-pages plan.")
			],
			[
				new ToolDeprecation(
					"Prefer sync-pages as the canonical page write path. Keep update-page only as a fallback for single-page dry-run or legacy save workflows.",
					[
						PageSyncTool.ToolName
					])
			]);
	}

	private static ToolContractDefinition BuildPageValidate() {
		return new ToolContractDefinition(
			PageValidateTool.ToolName,
			"Client-side Freedom UI page body validation without saving to Creatio. " +
			"For web pages (body starts with `define(`): checks marker integrity, JS syntax, JSON content, field bindings, column bindings, " +
			"handler structure, and VendorPrefix.Name format for converters, validators, and handler request values. " +
			"For mobile pages (plain JSON body starting with `{`): validates that disallowed constructs (validators, handlers, custom converters sections) are absent.",
			new ToolInputSchemaContract(
				["body"],
				[
					Field("body", StringType, "Full JavaScript page body with markers (web) or plain JSON body (mobile). Auto-detected by leading character."),
					Field(ResourcesFieldName, StringType, "Optional JSON object string of localizable strings the platform does NOT auto-provide (custom titles, button captions, validator messages, explicit overrides). Applicable to web pages only. Only include keys with NO matching DS-bound view model attribute on the page \u2014 see `page-schema-resources` guidance.")
				]),
			EnvelopeOutput(
				"valid",
				[
					"valid == false"
				],
				Field("valid", BooleanType, "Whether the page body passed all validations."),
				Field("validation", ObjectType, "Structured validation result with markers-ok, js-syntax-ok, content-ok, errors, and warnings.")
			),
			CommonErrorContract,
			[],
			[],
			[
				Example("Validate a web page body before saving", new Dictionary<string, object?> {
					["body"] = "define(\"MyApp/MyPage\", /** ... */)"
				}),
				Example("Validate a web page body with resources", new Dictionary<string, object?> {
					["body"] = "define(\"MyApp/MyPage\", /** ... */)",
					[ResourcesFieldName] = "{\"UsrDetailsTab_caption\":\"Details\"}"
				}),
				Example("Validate a mobile page body", new Dictionary<string, object?> {
					["body"] = "{\"type\": \"ep.MobileViewElement\", \"items\": []}"
				})
			],
			Flow(
				[
					PageGetTool.ToolName,
					PageValidateTool.ToolName
				],
				"Validate a page body fetched via get-page before saving with sync-pages or update-page."),
			[
				Flow(
					[
						PageGetTool.ToolName,
						PageValidateTool.ToolName,
						PageSyncTool.ToolName
					],
					"Full read-validate-save cycle using sync-pages as the canonical save path.")
			],
			[]);
	}

	private static ToolContractDefinition BuildApplicationDelete() {
		return new ToolContractDefinition(
			ApplicationDeleteTool.ToolName,
			"Deletes an installed application by name or code.",
			new ToolInputSchemaContract(
				[AppNameFieldName],
				EnvironmentOrExplicitConnectionFields(
					Field(AppNameFieldName, StringType, "Application name or code.")),
				AnyOf: EnvironmentOrExplicitConnectionRequirements()),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, ToolSucceededDescription),
				Field(ErrorFieldName, StringType, FailureMessageDescription)
			),
			CommonErrorContract,
			[],
			[],
			[
				Example("Delete an application by code", new Dictionary<string, object?> {
					[AppNameFieldName] = ExamplePackageName,
					[EnvironmentNameFieldName] = ExampleEnvironmentName
				})
			],
			Flow([ApplicationDeleteTool.ToolName], "Standalone destructive application lifecycle operation."),
			[],
			[]);
	}

	private static ToolContractField Field(string name, string type, string description) {
		return new ToolContractField(name, type, description);
	}

	private static IReadOnlyDictionary<string, string> LocalizationMap(string enUs) {
		return new Dictionary<string, string> {
			["en-US"] = enUs
		};
	}

	private static IReadOnlyList<ToolContractField> EnvironmentPackageFields(params ToolContractField[] extraFields) {
		return [
			Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription),
			Field(PackageNameFieldName, StringType, "Target package name."),
			..extraFields
		];
	}

	private static IReadOnlyList<ToolContractField> EnvironmentPackageSchemaFields(string schemaDescription, params ToolContractField[] extraFields) {
		return [
			..EnvironmentPackageFields(
				Field(SchemaNameFieldName, StringType, schemaDescription)),
			..extraFields
		];
	}

	private static IReadOnlyList<ToolContractField> WorkspacePackageFields(params ToolContractField[] extraFields) {
		return [
			Field(PackageNameFieldName, StringType, "Target package name."),
			Field(WorkspacePathFieldName, StringType, "Absolute local workspace path. Network-share paths are not supported."),
			..extraFields
		];
	}

	private static IReadOnlyList<ToolContractField> EnvironmentOrExplicitConnectionFields(params ToolContractField[] leadingFields) {
		return [
			..leadingFields,
			Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription),
			Field("uri", StringType, "Explicit Creatio URL."),
			Field(LoginFieldName, StringType, "Explicit login."),
			Field(PasswordFieldName, StringType, "Explicit password.")
		];
	}

	private static IReadOnlyList<IReadOnlyList<string>> EnvironmentOrExplicitConnectionRequirements() {
		return [
			[EnvironmentNameFieldName],
			["uri", LoginFieldName, PasswordFieldName]
		];
	}

	private static IReadOnlyList<ToolContractField> DataForgeConnectionFields(params ToolContractField[] leadingFields) {
		return [
			..leadingFields,
			Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription)
		];
	}

	private static IReadOnlyList<IReadOnlyList<string>> DataForgeConnectionRequirements() {
		return [
			[EnvironmentNameFieldName]
		];
	}

	private static ToolContractAlias EnvironmentNameParameterAlias() {
		return Alias(ParameterScope, EnvironmentNameFieldName, "environmentName", RejectedStatus,
			$"Use '{EnvironmentNameFieldName}' instead of 'environmentName'.");
	}

	private static ToolContractAlias PackageNameParameterAlias() {
		return Alias(ParameterScope, PackageNameFieldName, "packageName", RejectedStatus,
			$"Use '{PackageNameFieldName}' instead of 'packageName'.");
	}

	private static ToolContractAlias SchemaNameParameterAlias() {
		return Alias(ParameterScope, SchemaNameFieldName, "schemaName", RejectedStatus,
			$"Use '{SchemaNameFieldName}' instead of 'schemaName'.");
	}

	private static ToolContractAlias ColumnNameParameterAlias() {
		return Alias(ParameterScope, ColumnNameFieldName, "columnName", RejectedStatus,
			$"Use '{ColumnNameFieldName}' instead of 'columnName'.");
	}

	private static ToolContractAlias BindingNameParameterAlias() {
		return Alias(ParameterScope, BindingNameFieldName, "bindingName", RejectedStatus,
			$"Use '{BindingNameFieldName}' instead of 'bindingName'.");
	}

	private static ToolContractAlias WorkspacePathParameterAlias() {
		return Alias(ParameterScope, WorkspacePathFieldName, "workspacePath", RejectedStatus,
			$"Use '{WorkspacePathFieldName}' instead of 'workspacePath'.");
	}

	private static ToolContractAlias ReferenceSchemaNameParameterAlias() {
		return Alias(ParameterScope, ReferenceSchemaNameFieldName, "referenceSchemaName", RejectedStatus,
			$"Use '{ReferenceSchemaNameFieldName}' instead of 'referenceSchemaName'.");
	}

	private static ToolContractAlias DefaultValueParameterAlias() {
		return Alias(ParameterScope, "default-value", "defaultValue", RejectedStatus,
			"Use 'default-value' instead of 'defaultValue'.");
	}

	private static ToolContractAlias DefaultValueConfigParameterAlias() {
		return Alias(ParameterScope, DefaultValueConfigFieldName, "defaultValueConfig", RejectedStatus,
			"Use 'default-value-config' instead of 'defaultValueConfig'.");
	}

	private static ToolContractAlias DefaultValueSourceParameterAlias() {
		return Alias(ParameterScope, "default-value-source", "defaultValueSource", RejectedStatus,
			"Use 'default-value-source' instead of 'defaultValueSource'.");
	}

	private static ToolContractAlias TitleParameterAlias() {
		return Alias(ParameterScope, TitleLocalizationsFieldName, "title", RejectedStatus,
			$"Prefer '{TitleLocalizationsFieldName}'; legacy scalar 'title' is used only as an en-US fallback for an add.");
	}

	private static ToolContractAlias CaptionParameterAlias() {
		return Alias(ParameterScope, TitleLocalizationsFieldName, CaptionFieldName, RejectedStatus,
			$"Prefer '{TitleLocalizationsFieldName}'; legacy scalar 'caption' is used only as an en-US fallback for an add.");
	}

	private static ToolContractAlias DescriptionParameterAlias() {
		return Alias(ParameterScope, DescriptionLocalizationsFieldName, DescriptionFieldName, RejectedStatus,
			$"Use '{DescriptionLocalizationsFieldName}' instead of legacy scalar 'description'.");
	}

	private static IReadOnlyList<ToolContractAlias> EnvironmentPackageAliases(params ToolContractAlias[] extraAliases) {
		return [
			EnvironmentNameParameterAlias(),
			PackageNameParameterAlias(),
			..extraAliases
		];
	}

	private static IReadOnlyList<ToolContractAlias> EnvironmentPackageSchemaAliases(params ToolContractAlias[] extraAliases) {
		return [
			..EnvironmentPackageAliases(),
			SchemaNameParameterAlias(),
			..extraAliases
		];
	}

	private static IReadOnlyList<ToolContractAlias> DataForgeConnectionAliases(params ToolContractAlias[] extraAliases) {
		return [
			EnvironmentNameParameterAlias(),
			..extraAliases
		];
	}

	private static ToolContractValidator RequiredLocalizationMapValidator(string field) {
		return new ToolContractValidator("localizations-map", InvalidLocalizationMapCode, field,
			Context: "Parameter", Required: true);
	}

	private static ToolFlowHint PreferSchemaSyncFlow() {
		return Flow([SchemaSyncTool.ToolName], "Prefer sync-schemas for ordered multi-step entity work.");
	}

	private static IReadOnlyList<ToolDeprecation> PreferSchemaSyncDeprecations(string toolName) {
		return [
			new ToolDeprecation(
				$"Prefer sync-schemas as the canonical entity mutation path. Keep {toolName} for explicit fallback or isolated operations.",
				[
					SchemaSyncTool.ToolName
				])
		];
	}

	private static ToolOutputContract StructuredResultOutput(params ToolContractField[] fields) {
		return new ToolOutputContract(
			"structured-result",
			null,
			[
				"structured result cannot be parsed"
			],
			fields);
	}

	private static ToolContractAlias Alias(string scope, string canonicalName, string alias, string status, string message) {
		return new ToolContractAlias(scope, canonicalName, alias, status, message);
	}

	private static ToolContractDefaultValue Default(string name, string value, string reason) {
		return new ToolContractDefaultValue(name, value, reason);
	}

	private static ToolContractExample Example(string summary, IReadOnlyDictionary<string, object?> arguments) {
		return new ToolContractExample(summary, arguments);
	}

	private static ToolFlowHint Flow(IReadOnlyList<string> tools, string notes) {
		return new ToolFlowHint(tools, notes);
	}

	private static ToolOutputContract EnvelopeOutput(
		string successField,
		IReadOnlyList<string> failureSignals,
		params ToolContractField[] fields) {
		return new ToolOutputContract("structured-envelope", successField, failureSignals, fields);
	}

	private static ToolContractDefinition BuildFindEntitySchema() {
		return new ToolContractDefinition(
			FindEntitySchemaTool.FindEntitySchemaToolName,
			"Finds entity schemas in a Creatio environment by exact name, substring pattern, or UId without requiring the package name.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName],
				[
					Field(EnvironmentNameFieldName, StringType, "Creatio environment name."),
					Field(SchemaNameFieldName, StringType, "Exact entity schema name to find (use instead of search-pattern or uid)."),
					Field(SearchPatternFieldName, StringType, "Case-insensitive substring to search in entity schema names."),
					Field("uid", StringType, "Entity schema UId (Guid) for exact lookup.")
				],
				[
					[SchemaNameFieldName],
					[SearchPatternFieldName],
					["uid"]
				]),
			StructuredResultOutput(
				Field("schema-name", StringType, "Entity schema name."),
				Field("package-name", StringType, "Package that owns the schema."),
				Field("package-maintainer", StringType, "Package maintainer."),
				Field(ParentSchemaNameFieldName, StringType, "Parent schema name, if any.")),
			CommonErrorContract,
			[],
			[],
			[
				Example("Search for schemas containing a substring", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[SearchPatternFieldName] = ExampleTaskSchemaName
				}),
				Example("Look up a schema by exact name", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[SchemaNameFieldName] = ExampleTaskStatusSchemaName
				})
			],
			Flow(
				[
					FindEntitySchemaTool.FindEntitySchemaToolName,
					GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName
				],
				"Use to discover the package that owns a schema before calling get-entity-schema-properties or modify-entity-schema-column."),
			[],
			[]);
	}

	private static ToolContractDefinition BuildGetSchemaNamePrefix() {
		return new ToolContractDefinition(
			SchemaNamePrefixTool.GetSchemaNamePrefixToolName,
			"Returns the active SchemaNamePrefix system setting for the environment. " +
			"Returns empty string when no prefix is configured (use no prefix in that case). " +
			"Default Creatio environments return 'Usr'. " +
			"Note: create-app and get-app-info both read this setting automatically and return schema-name-prefix " +
			"in their responses — you only need this tool when you require the prefix before calling either of those.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName],
				[
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription)
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, ToolSucceededDescription),
				Field("schema-name-prefix", StringType, "Active SchemaNamePrefix system setting. Empty string means no prefix is configured."),
				Field(ErrorFieldName, StringType, FailureMessageDescription)
			),
			CommonErrorContract,
			[],
			[],
			[
				Example("Read the active schema name prefix for the configured environment", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName
				})
			],
			Flow(
				[
					SchemaNamePrefixTool.GetSchemaNamePrefixToolName,
					CreateEntitySchemaTool.CreateEntitySchemaToolName
				],
				"Use before create-entity-schema, create-lookup, create-page, or sync-schemas when neither create-app nor get-app-info has been called yet in the session."),
			[],
			[]);
	}

	private static ToolContractDefinition BuildCompileCreatio() {
		return new ToolContractDefinition(
			CompileCreatioTool.CompileCreatioToolName,
			"Recompiles a registered Creatio environment and forces a runtime reload. Long-running (often several minutes). Reserved for C# schema changes, FSM-mode transitions, and schema-missing runtime errors. Freedom UI page-body edits (validators, handlers, converters) do NOT require compilation — those changes are AMD modules served at runtime.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName],
				[
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription),
					Field(PackageNameFieldName, StringType, "Optional package name. When omitted, runs a full compilation (`clio cc -e ENV_NAME --all`). When provided, recompiles only that single package. Comma-separated lists are not supported.")
				]),
			CommandExecutionOutput(),
			CommonErrorContract,
			[],
			[],
			[
				Example("Run a full compilation after toggling FSM mode", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName
				}),
				Example("Recompile a single package after a C# schema change", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[PackageNameFieldName] = ExamplePackageName
				})
			],
			Flow(
				[
					FsmModeTool.SetFsmModeToolName,
					CompileCreatioTool.CompileCreatioToolName
				],
				"Call only after C# schema work, after `set-fsm-mode`, or in response to a runtime schema-missing error. Skip this tool entirely when the work touches only Freedom UI page bodies or DDL changes routed through `update-entity-schema`."),
			[],
			[],
			AntiPatterns: [
				new ToolAntiPattern(
					$"{PageUpdateTool.ToolName} → {CompileCreatioTool.CompileCreatioToolName}",
					"Freedom UI page bodies are AMD modules served at runtime. `update-page` and `sync-pages` make changes live; running `compile-creatio` afterward forces an unnecessary runtime reload and breaks the active session."),
				new ToolAntiPattern(
					$"{UpdateEntitySchemaTool.UpdateEntitySchemaToolName} → {CompileCreatioTool.CompileCreatioToolName}",
					"`update-entity-schema` applies DDL changes directly. No compilation is required as a follow-up."),
				new ToolAntiPattern(
					$"{ApplicationCreateTool.ApplicationCreateToolName} → {CompileCreatioTool.CompileCreatioToolName}",
					"`create-app` provisions a starter section, entity, and pages without any compilation step. Calling `compile-creatio` afterward serves no purpose and forces a runtime reload."),
				new ToolAntiPattern(
					$"{PageCreateTool.ToolName} → {CompileCreatioTool.CompileCreatioToolName}",
					"`create-page` writes a page schema into the runtime catalog directly. The new page becomes available without compilation."),
				new ToolAntiPattern(
					$"{CreateEntityBusinessRuleTool.BusinessRuleCreateToolName} → {CompileCreatioTool.CompileCreatioToolName}",
					"Business-rule creation writes add-on metadata directly. Successful rule creation does not need compilation as a routine post-step.")
			],
			Preconditions: [
				"`set-fsm-mode` was just toggled (full compilation only).",
				"C# schemas were added or modified in the targeted package.",
				"The runtime reported a missing-in-runtime or schema-not-found error that maps to a compilation gap.",
				"Caller must NOT call this tool after `create-app`, `update-page`, `sync-pages`, `update-entity-schema`, `create-page`, `create-entity-business-rules`, or `create-page-business-rules`."
			]);
	}

	private static ToolContractDefinition BuildInstallGate() {
		return new ToolContractDefinition(
			InstallGateTool.InstallGateToolName,
			"Installs (or updates) the bundled cliogate package into a registered Creatio environment. cliogate exposes the server-side API that workspace and package tooling depends on. Run this once per freshly deployed instance, or whenever a gate-dependent tool fails with \"you need to install the cliogate package version ... or higher\".",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName],
				[
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription)
				]),
			CommandExecutionOutput(),
			CommonErrorContract,
			[],
			[],
			[
				Example("Install cliogate into a freshly deployed environment", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName
				})
			],
			Flow(
				[
					InstallGateTool.InstallGateToolName,
					RestoreWorkspaceTool.RestoreWorkspaceToolName
				],
				"Install cliogate first, then retry the gate-dependent tool (for example restore-workspace) that reported the missing-cliogate error."),
			[],
			[],
			Preconditions: [
				"The target environment is registered (see list-environments / reg-web-app).",
				"A gate-dependent tool reported \"you need to install the cliogate package version ... or higher\", or this is a freshly deployed instance that has not yet had cliogate installed."
			]);
	}

	private const string WorkspaceDirectoryFieldName = "workspaceDirectory";
	private const string ProjectNameFieldName = "projectName";
	private const string VendorPrefixFieldName = "vendorPrefix";
	private const string EmptyFieldName = "empty";
	private const string CreatioVersionFieldName = "creatioVersion";

	private static ToolContractDefinition BuildNewUiProject() {
		return new ToolContractDefinition(
			CreateUiProjectTool.CreateUiProjectToolName,
			"Scaffolds a Freedom UI Angular remote-module project inside an existing clio workspace. Pure local file-system scaffolding under <workspaceDirectory>/projects/<projectName> and <workspaceDirectory>/packages/<packageName>; no Creatio environment is contacted. The MCP wrapper pins the process working directory to workspaceDirectory and runs the underlying CLI in silent mode, so the interactive 'download package?' prompt is auto-answered 'no'.",
			new ToolInputSchemaContract(
				[WorkspaceDirectoryFieldName, ProjectNameFieldName, "packageName", VendorPrefixFieldName],
				[
					Field(WorkspaceDirectoryFieldName, StringType,
						"Absolute path to an existing clio workspace directory. MUST contain '.clio/workspaceSettings.json'. Relative paths, network-share paths, and non-workspace directories are rejected. Call 'create-workspace' first when the target directory is not yet a workspace."),
					Field(ProjectNameFieldName, StringType,
						"Angular project name in snake_case. MUST match '^[0-9a-z_]+$' (lowercase letters, digits, underscores). Examples: 'rss_reader', 'task_board'. Translate any PascalCase/camelCase/kebab-case user input into snake_case before sending."),
					Field("packageName", StringType,
						"Clio package name that will host the project. MUST be a simple identifier matching '^[A-Za-z0-9_]+$' — path separators, '..', and absolute paths are rejected so scaffolding cannot escape the workspace. Conventionally PascalCase (e.g., 'UsrRssReader', 'RssReader'). Created if missing; reused if it already exists."),
					Field(VendorPrefixFieldName, StringType,
						"Vendor prefix; 1-50 lowercase letters only ('^[a-z]{1,50}$'). Examples: 'usr', 'crt', 'acme'. Uppercase and digits are rejected by the options validator."),
					Field(EmptyFieldName, BooleanType,
						"When true, scaffold the 'ui-project-Empty' minimal template instead of the default 'ui-project' template. Default false."),
					Field(CreatioVersionFieldName, StringType,
						"Optional Creatio version to pick a matching UI project template. Omit to use the template provider's current default.")
				],
				Validators: [
					new ToolContractValidator(
						"absolute-path",
						"invalid-workspace-directory",
						Field: WorkspaceDirectoryFieldName,
						Context: "workspaceDirectory must be a fully-qualified absolute path (Path.IsPathFullyQualified). Drive-relative ('C:ws') and root-relative ('\\ws') paths are rejected. It must also point at an existing directory containing '.clio/workspaceSettings.json'.",
						Required: true),
					new ToolContractValidator(
						"regex",
						"invalid-project-name",
						Field: ProjectNameFieldName,
						Context: "projectName must match ^[0-9a-z_]+$ (snake_case). Uppercase, hyphens, dots and spaces are rejected.",
						Required: true),
					new ToolContractValidator(
						"regex",
						"invalid-package-name",
						Field: "packageName",
						Context: "packageName must match ^[A-Za-z0-9_]+$ (simple identifier). Path separators, '..', and absolute paths are rejected so scaffolding stays inside the workspace.",
						Required: true),
					new ToolContractValidator(
						"regex",
						"invalid-vendor-prefix",
						Field: VendorPrefixFieldName,
						Context: "vendorPrefix must match ^[a-z]{1,50}$ (lowercase letters only, 1-50 chars).",
						Required: true)
				]),
			CommandExecutionOutput(),
			CommonErrorContract,
			[],
			[
				Default(EmptyFieldName, BooleanFalseLiteral,
					"Use the default Freedom UI remote-module template unless the caller asks for the minimal shell.")
			],
			[
				Example("Scaffold a default RSS reader remote module", new Dictionary<string, object?> {
					[WorkspaceDirectoryFieldName] = @"C:\Projects\Workspaces\newModule",
					[ProjectNameFieldName] = "rss_reader",
					["packageName"] = "RssReader",
					[VendorPrefixFieldName] = "usr"
				}),
				Example("Scaffold an empty-template project for a specific Creatio version", new Dictionary<string, object?> {
					[WorkspaceDirectoryFieldName] = @"C:\Projects\Workspaces\son",
					[ProjectNameFieldName] = "kpi_widget",
					["packageName"] = "UsrKpiWidgets",
					[VendorPrefixFieldName] = "usr",
					[EmptyFieldName] = true,
					[CreatioVersionFieldName] = "8.1.2"
				})
			],
			Flow(
				[
					CreateWorkspaceTool.CreateWorkspaceToolName,
					CreateUiProjectTool.CreateUiProjectToolName
				],
				"Ensure the target directory is a clio workspace first (skip when '.clio/workspaceSettings.json' already exists), then scaffold the Angular project. No compile-creatio or restart follow-up is required — Angular tooling is invoked by the user outside clio."),
			[
				Flow(
					[
						CreateUiProjectTool.CreateUiProjectToolName
					],
					"Use this single-step flow when the workspace already exists and only the UI project needs scaffolding.")
			],
			[],
			AntiPatterns: [
				new ToolAntiPattern(
					$"{CreateUiProjectTool.CreateUiProjectToolName} → {CompileCreatioTool.CompileCreatioToolName}",
					"new-ui-project is local file-system scaffolding only. No Creatio assemblies change and no environment is contacted, so a compile-creatio follow-up serves no purpose."),
				new ToolAntiPattern(
					$"{ApplicationCreateTool.ApplicationCreateToolName} → {CreateUiProjectTool.CreateUiProjectToolName}",
					"create-app installs an application into a Creatio environment; new-ui-project scaffolds an Angular remote module on the local file system. They address different artifacts and should not be chained as if one followed from the other."),
				new ToolAntiPattern(
					$"PascalCase or hyphenated {ProjectNameFieldName}",
					"The underlying creator enforces snake_case via '^[0-9a-z_]+$'. Translate user-supplied names like 'RssReader' or 'rss-reader' into 'rss_reader' before calling the tool; do not pass the raw display name through.")
			],
			Preconditions: [
				$"`{WorkspaceDirectoryFieldName}` is a fully-qualified absolute path to an existing directory containing `.clio/workspaceSettings.json` (call `create-workspace` first when it is not).",
				$"`{ProjectNameFieldName}` is snake_case matching `^[0-9a-z_]+$`.",
				"`packageName` is a simple identifier matching `^[A-Za-z0-9_]+$`.",
				$"`{VendorPrefixFieldName}` is lowercase-only matching `^[a-z]{{1,50}}$`."
			]);
	}

	private const string SiteNameFieldName = "siteName";
	private const string ZipFileFieldName = "zipFile";
	private const string SitePortFieldName = "sitePort";
	private const string DbServerNameFieldName = "dbServerName";
	private const string RedisServerNameFieldName = "redisServerName";
	private const string IdentitySitePortFieldName = "identitySitePort";
	private const string IdentitySiteNameFieldName = "identitySiteName";
	private const string IdentityPathFieldName = "identityPath";
	private const string IdentityArchivePathInBundleFieldName = "identityArchivePathInBundle";
	private const string ConfigurationModeFieldName = "configurationMode";
	private const string ClientNameFieldName = "clientName";
	private const string ClientApplicationUrlFieldName = "clientApplicationUrl";
	private const string ClientDescriptionFieldName = "clientDescription";
	private const string NoAppFieldName = "noApp";
	private const string CreateTechUserFieldName = "createTechUser";
	private const string UserFieldName = "user";
	private const string SkipBackupFieldName = "skip-backup";
	private const string ExampleWorkspaceAbsolutePath = @"C:\Projects\Workspaces\UsrTaskApp";

	private static ToolContractDefinition BuildAssertInfrastructure() {
		return new ToolContractDefinition(
			AssertInfrastructureTool.AssertInfrastructureToolName,
			"Runs the full infrastructure assertion sweep (Kubernetes, local infrastructure, and filesystem) in one call and returns a machine-readable aggregate result with per-section assertion results plus normalized database candidates. Call this first in the deploy lifecycle to inspect failing or degraded areas before selecting a deployment target.",
			new ToolInputSchemaContract([], []),
			StructuredResultOutput(
				Field(StatusFieldName, StringType, "Overall infrastructure assertion status: pass, partial, or fail."),
				Field("exit-code", NumberType, "Overall infrastructure assertion exit code."),
				Field("summary", StringType, "Human-readable summary of the assertion sweep."),
				Field("sections", ObjectType, "Per-scope assertion results (k8, local, filesystem)."),
				Field("database-candidates", ArrayType, "Normalized database candidates discovered across passing sections.")),
			CommonErrorContract,
			[],
			[],
			[
				Example("Run the full infrastructure assertion sweep", new Dictionary<string, object?>())
			],
			Flow(
				[
					AssertInfrastructureTool.AssertInfrastructureToolName,
					ShowPassingInfrastructureTool.ShowPassingInfrastructureToolName,
					FindEmptyIisPortTool.FindEmptyIisPortToolName,
					InstallerCommandTool.DeployCreatioToolName
				],
				"Canonical deploy preflight: assert full infrastructure, narrow to passing choices, pick a safe local IIS port, then deploy. See the deploy-lifecycle guidance topic via get-guidance."),
			[],
			[]);
	}

	private static ToolContractDefinition BuildShowPassingInfrastructure() {
		return new ToolContractDefinition(
			ShowPassingInfrastructureTool.ShowPassingInfrastructureToolName,
			"Returns only the passing infrastructure choices that are safe to use for deployment selection, plus the recommended deploy-creatio argument bundle for the current infrastructure state. Run assert-infrastructure first to inspect failing or degraded areas.",
			new ToolInputSchemaContract([], []),
			StructuredResultOutput(
				Field(StatusFieldName, StringType, "Passing-infrastructure availability status: available or unavailable."),
				Field("summary", StringType, "Human-readable summary of the passing infrastructure discovery."),
				Field("kubernetes", ObjectType, "Passing Kubernetes deployment choices."),
				Field("local", ObjectType, "Passing local deployment choices."),
				Field("filesystem", ObjectType, "Passing filesystem readiness relevant for deployment."),
				Field("recommendedDeployment", ObjectType, "Recommended passing choice to merge into a deploy-creatio call."),
				Field("recommendedByEngine", ObjectType, "Recommended passing choices grouped by database engine.")),
			CommonErrorContract,
			[],
			[],
			[
				Example("Show passing infrastructure and deployment recommendations", new Dictionary<string, object?>())
			],
			Flow(
				[
					AssertInfrastructureTool.AssertInfrastructureToolName,
					ShowPassingInfrastructureTool.ShowPassingInfrastructureToolName,
					InstallerCommandTool.DeployCreatioToolName
				],
				"Use the recommended bundle from this tool as the deploy-creatio argument source after assert-infrastructure confirms readiness."),
			[],
			[]);
	}

	private static ToolContractDefinition BuildFindEmptyIisPort() {
		return new ToolContractDefinition(
			FindEmptyIisPortTool.FindEmptyIisPortToolName,
			"Finds the first free IIS deployment port between 40000 and 42000. Use this before deploy-creatio when you need a safe local IIS sitePort.",
			new ToolInputSchemaContract([], []),
			StructuredResultOutput(
				Field("status", StringType, "Availability status for the requested range."),
				Field("summary", StringType, "Human-readable scan summary."),
				Field("rangeStart", NumberType, "Inclusive start of the scanned range."),
				Field("rangeEnd", NumberType, "Inclusive end of the scanned range."),
				Field("firstAvailablePort", NumberType, "First discovered free IIS port, if any. Use as the deploy-creatio sitePort."),
				Field("iisBoundPortCount", NumberType, "Number of ports already claimed by IIS site bindings."),
				Field("activeTcpPortCount", NumberType, "Number of ports already claimed by active TCP listeners or connections.")),
			CommonErrorContract,
			[],
			[],
			[
				Example("Find a safe local IIS port for deployment", new Dictionary<string, object?>())
			],
			Flow(
				[
					FindEmptyIisPortTool.FindEmptyIisPortToolName,
					InstallerCommandTool.DeployCreatioToolName
				],
				"Pass firstAvailablePort as the deploy-creatio sitePort for a local IIS deployment."),
			[],
			[]);
	}

	private static ToolContractDefinition BuildDeployCreatio() {
		return new ToolContractDefinition(
			InstallerCommandTool.DeployCreatioToolName,
			"Deploys Creatio from a zip archive using the real deploy-creatio command path. This is the most consequential, hardest-to-reverse lifecycle tool: it drops and recreates the target site. Run the deploy preflight first (assert-infrastructure -> show-passing-infrastructure -> find-empty-iis-port) and prefer the recommended bundle from show-passing-infrastructure. Deployment preserves the build database's existing forced-password-change state and does not clear it automatically.",
			new ToolInputSchemaContract(
				[SiteNameFieldName, ZipFileFieldName, SitePortFieldName],
				[
					Field(SiteNameFieldName, StringType, "Creatio instance name."),
					Field(ZipFileFieldName, StringType, "Absolute path to the Creatio build archive (.zip). Pick a build from the configured creatio-products folder when the path is unknown."),
					Field(SitePortFieldName, NumberType, "Port where Creatio will be deployed. Use find-empty-iis-port to choose a safe local IIS port."),
					Field(DbServerNameFieldName, StringType, "Optional local database server configuration name; omit to keep the default Kubernetes deployment path."),
					Field(RedisServerNameFieldName, StringType, "Optional local Redis server configuration name.")
				]),
			CommandExecutionOutput(),
			CommonErrorContract,
			[],
			[],
			[
				Example("Deploy a local IIS instance after the deploy preflight", new Dictionary<string, object?> {
					[SiteNameFieldName] = "creatio-app",
					[ZipFileFieldName] = @"F:\CreatioBuilds\8.1.5.2176_StudioNet8_Softkey_PostgreSQL_ENU.zip",
					[SitePortFieldName] = 40001,
					[DbServerNameFieldName] = "postgres-local",
					[RedisServerNameFieldName] = "redis-local"
				})
			],
			Flow(
				[
					AssertInfrastructureTool.AssertInfrastructureToolName,
					ShowPassingInfrastructureTool.ShowPassingInfrastructureToolName,
					FindEmptyIisPortTool.FindEmptyIisPortToolName,
					InstallerCommandTool.DeployCreatioToolName
				],
				"Always run the full deploy preflight before deploy-creatio. After deployment, register the instance with reg-web-app and install cliogate with install-gate before using workspace tools."),
			[],
			[],
			Preconditions: [
				"assert-infrastructure was run and the targeted database/Redis sections pass (or were chosen from show-passing-infrastructure).",
				"For a local IIS deployment, sitePort is a free port (use find-empty-iis-port).",
				"zipFile points at an existing Creatio build archive (pick one from the configured creatio-products folder)."
			]);
	}

	private static ToolContractDefinition BuildDeployIdentity() {
		return new ToolContractDefinition(
			DeployIdentityTool.DeployIdentityToolName,
			"Deploys IdentityService to IIS for a registered local Creatio environment, connects Creatio through the platform sys-settings/REST path, creates a fresh clio OAuth client bound to an existing user by default, and stores the returned client credentials in local clio appsettings. Never echo the generated client secret in logs or public messages.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName],
				[
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription),
					Field(ZipFileFieldName, StringType, "Optional path to a standalone IdentityService.zip or a Creatio distribution bundle containing IdentityService.zip. When omitted, deploy-identity finds IdentityService.zip under the registered EnvironmentPath."),
					Field(IdentitySitePortFieldName, NumberType, "Optional HTTP port where IdentityService will listen. When omitted, deploy-identity selects the first free IIS port in range 40001-40100."),
					Field(IdentityArchivePathInBundleFieldName, StringType, "Nested IdentityService archive path when zipFile is a Creatio bundle, and the relative path preferred under EnvironmentPath when zipFile is omitted. Default: IdentityService.zip."),
					Field(IdentitySiteNameFieldName, StringType, "Optional IIS site and app pool name. Defaults to <environment>-identity."),
					Field(IdentityPathFieldName, StringType, "Optional target directory for IdentityService files."),
					Field(ConfigurationModeFieldName, StringType, "Creatio connection mode: db-first, rest, or db. db-first currently falls back to REST/sys-settings until direct DB seeding is proven."),
					Field(ClientNameFieldName, StringType, "OAuth client display name created for clio."),
					Field(ClientApplicationUrlFieldName, StringType, "OAuth client application URL."),
					Field(ClientDescriptionFieldName, StringType, "OAuth client description."),
					Field(NoAppFieldName, BooleanType, "Deploy and connect IdentityService without creating a clio OAuth app or verifying client_credentials."),
					Field(CreateTechUserFieldName, BooleanType, "Create a new technical user for the OAuth app instead of binding it to an existing user."),
					Field(UserFieldName, StringType, "Existing Creatio system user used by the OAuth client. Defaults to Supervisor.")
				]),
			CommandExecutionOutput(),
			CommonErrorContract,
			[],
			[],
			[
				Example("Deploy IdentityService with environment defaults", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[ConfigurationModeFieldName] = "db-first"
				})
			],
			Flow(
				[
					DeployIdentityTool.DeployIdentityToolName
				],
				"Deploy IdentityService for an already registered local Creatio environment. The command can discover IdentityService.zip under EnvironmentPath and auto-pick a free IIS port; by default it creates a fresh OAuth app bound to Supervisor, stores generated clio OAuth credentials in local clio settings, and masks the secret in command output."),
			[],
			[],
			Preconditions: [
				"The environment is registered and has EnvironmentPath pointing to a local Creatio installation.",
				"Supervisor/default credentials or existing environment credentials can authenticate to Creatio.",
				"Use explicit zipFile or identitySitePort only when overriding the EnvironmentPath archive discovery or default port range."
			]);
	}

	private static ToolContractDefinition BuildRestoreWorkspace() {
		return new ToolContractDefinition(
			RestoreWorkspaceTool.RestoreWorkspaceToolName,
			"Restores the local workspace at workspace-path from the specified Creatio environment. Requires the cliogate package on the target environment; when it is missing, install it with install-gate and retry.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, WorkspacePathFieldName],
				[
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription),
					Field(WorkspacePathFieldName, StringType, WorkspacePathDescription)
				]),
			CommandExecutionOutput(),
			CommonErrorContract,
			[],
			[],
			[
				Example("Restore a workspace from a registered environment", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[WorkspacePathFieldName] = ExampleWorkspaceAbsolutePath
				})
			],
			Flow(
				[
					RestoreWorkspaceTool.RestoreWorkspaceToolName,
					PushWorkspaceTool.PushWorkspaceToolName
				],
				"Restore packages from the environment into the local workspace, then push local changes back with push-workspace."),
			[],
			[],
			Preconditions: [
				"The environment is registered (see list-environments / reg-web-app).",
				"cliogate is installed on the target environment; if restore fails with a missing-cliogate error, run install-gate and retry.",
				"workspace-path is a local absolute path to an existing directory (network-share paths are not supported)."
			]);
	}

	private static ToolContractDefinition BuildPushWorkspace() {
		return new ToolContractDefinition(
			PushWorkspaceTool.PushWorkspaceToolName,
			"Pushes the local workspace at workspace-path to the specified Creatio environment using the application installer.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, WorkspacePathFieldName],
				[
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription),
					Field(WorkspacePathFieldName, StringType, WorkspacePathDescription),
					Field(SkipBackupFieldName, BooleanType, "When true, skips package backup before workspace install.")
				]),
			CommandExecutionOutput(),
			CommonErrorContract,
			[],
			[],
			[
				Example("Push a local workspace to a registered environment", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[WorkspacePathFieldName] = ExampleWorkspaceAbsolutePath
				})
			],
			Flow(
				[
					PushWorkspaceTool.PushWorkspaceToolName,
					CompileCreatioTool.CompileCreatioToolName
				],
				"Push the workspace, then run compile-creatio only when the pushed packages contain C# schema changes that require compilation."),
			[],
			[],
			Preconditions: [
				"The environment is registered (see list-environments / reg-web-app).",
				"workspace-path is a local absolute path to an existing workspace directory (network-share paths are not supported)."
			]);
	}

	private static ToolContractDefinition BuildListCreatioBuilds() {
		return new ToolContractDefinition(
			ListCreatioBuildsTool.ListCreatioBuildsToolName,
			"Lists the Creatio build archives (.zip) available under the configured creatio-products folder so a deploy-creatio zipFile can be chosen deterministically instead of globbing the filesystem. The response surfaces the resolved products folder and whether it exists, so a stale or missing configuration is reported explicitly.",
			new ToolInputSchemaContract([], []),
			StructuredResultOutput(
				Field(StatusFieldName, StringType, "Discovery status: ok, no-builds-found, products-folder-missing, products-folder-not-configured, or products-folder-unreadable."),
				Field("products-folder", StringType, "Resolved creatio-products folder configured in clio appsettings.json."),
				Field("products-folder-exists", BooleanType, "Whether the configured creatio-products folder exists on disk."),
				Field("message", StringType, "Human-readable summary or remediation hint."),
				Field("builds", ArrayType, "Discovered build archives newest-first, each with file-name, full-path, size-bytes, and modified-on-utc. Pass full-path as the deploy-creatio zipFile."),
				Field("truncated", BooleanType, "True when more builds exist than were returned.")),
			CommonErrorContract,
			[],
			[],
			[
				Example("List available Creatio builds before deploying", new Dictionary<string, object?>())
			],
			Flow(
				[
					ListCreatioBuildsTool.ListCreatioBuildsToolName,
					InstallerCommandTool.DeployCreatioToolName
				],
				"Discover a build, then pass its full-path as the deploy-creatio zipFile. Run the infrastructure preflight (assert-infrastructure) alongside build discovery."),
			[],
			[]);
	}

	private static ToolOutputContract CommandExecutionOutput() {
		return new ToolOutputContract(
			"command-execution-result",
			null,
			[
				"exit-code != 0",
				"execution-log-messages[*].message-type == Error"
			],
			[
				Field("exit-code", NumberType, "Process exit code."),
				Field("execution-log-messages", ArrayType, "Structured log messages."),
				Field("log-file-path", StringType, "Optional operation log path.")
			]);
	}

	private static ToolOutputContract BusinessRuleBatchOutput() {
		return new ToolOutputContract(
			"business-rule-batch-result",
			null,
			[
				"failed > 0",
				"error != null"
			],
			[
				Field("created", NumberType, "Number of rules created."),
				Field("failed", NumberType, "Number of rules that failed."),
				Field("results", ArrayType, "Per-rule outcomes in input order; each item has name, success, ruleName, and error."),
				Field("error", StringType, "Request-level error that prevented the whole batch from running. Note: when the requested environment cannot be resolved (unknown/unreachable), the tool instead returns the standard command-execution envelope (exit-code 1 with execution-log-messages referencing the environment) rather than this batch shape.")
			]);
	}

	private static ToolOutputContract ODataCreateBatchOutput() {
		return new ToolOutputContract(
			"odata-create-batch-result",
			null,
			[
				"failed > 0",
				"error != null"
			],
			[
				Field("created", NumberType, "Number of rows created."),
				Field("failed", NumberType, "Number of rows that failed."),
				Field("results", ArrayType, "Per-row outcomes for every attempted row; each item has index, success, id, and error."),
				Field("error", StringType, "Request-level error that prevented any row from being attempted.")
			]);
	}

	private const string SysSettingCodeFieldName = "code";
	private const string SysSettingValueFieldName = ValueFieldName;
	private const string SysSettingValueTypeFieldName = "value-type-name";
	private const string ExampleSysSettingCode = "MaxFileSize";
	private const string ExampleSysSettingName = "Maximum file size";
	private const string ExampleSysSettingValueType = "Integer";

	private static ToolContractDefinition BuildGetSysSetting() {
		return new ToolContractDefinition(
			SysSettingGetTool.GetSysSettingToolName,
			"Reads the All-Users default value of a Creatio system setting by code. Returns an empty value when the setting is not configured. Pair with list-sys-settings to discover codes.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, SysSettingCodeFieldName],
				[
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription),
					Field(SysSettingCodeFieldName, StringType, "Sys-setting code (e.g., 'SchemaNamePrefix').")
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, ToolSucceededDescription),
				Field(SysSettingCodeFieldName, StringType, "Sys-setting code echoed from the request."),
				Field(SysSettingValueFieldName, StringType, "Raw string value of the sys-setting. Empty string when not configured."),
				Field(ErrorFieldName, StringType, FailureMessageDescription)
			),
			CommonErrorContract,
			[],
			[],
			[
				Example("Read a single sys-setting by code", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[SysSettingCodeFieldName] = "SchemaNamePrefix"
				})
			],
			Flow(
				[
					SysSettingsListTool.ListSysSettingsToolName,
					SysSettingGetTool.GetSysSettingToolName
				],
				"Discover available codes with list-sys-settings, then read a specific value with get-sys-setting."),
			[],
			[]);
	}

	private static ToolContractDefinition BuildListSysSettings() {
		return new ToolContractDefinition(
			SysSettingsListTool.ListSysSettingsToolName,
			"Lists Creatio system settings with their All-Users default values, value-type-name, and metadata. Binary-type settings are excluded — Binary read/write is not exposed through this MCP tool set and needs the dedicated upload/download flow.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName],
				[
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription)
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, ToolSucceededDescription),
				Field("settings", ArrayType, "Sys-settings with code, name, value-type-name, value, is-cacheable, is-personal."),
				Field(ErrorFieldName, StringType, FailureMessageDescription)
			),
			CommonErrorContract,
			[],
			[],
			[
				Example("List sys-settings for the configured environment", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName
				})
			],
			Flow(
				[
					SysSettingsListTool.ListSysSettingsToolName,
					SysSettingUpdateTool.UpdateSysSettingToolName
				],
				"Discover an existing sys-setting before updating its value."),
			[],
			[]);
	}

	private static ToolContractDefinition BuildCreateSysSetting() {
		return new ToolContractDefinition(
			SysSettingCreateTool.CreateSysSettingToolName,
			"Creates a new Creatio system setting and optionally assigns an initial All-Users default value. " +
			"Allowed value-type-name values match Creatio internal names: Text, ShortText, MediumText, LongText, SecureText, MaxSizeText, " +
			"Boolean, DateTime, Date, Time, Integer, Money, Float, Lookup. " +
			"Aliases: Currency = Money, Decimal = Float. Binary sys-settings are not exposed through this tool set. " +
			"For Lookup type, reference-schema-name is required.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, SysSettingCodeFieldName, "name", SysSettingValueTypeFieldName],
				[
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription),
					Field(SysSettingCodeFieldName, StringType, "Sys-setting code (unique)."),
					Field("name", StringType, "Display name of the sys-setting."),
					Field(SysSettingValueTypeFieldName, StringType, "Value type. Creatio internal name: Text, ShortText, MediumText, LongText, SecureText, MaxSizeText, Boolean, DateTime, Date, Time, Integer, Money, Float, Lookup. Aliases: Currency = Money, Decimal = Float. Binary is not exposed by this tool set."),
					Field(SysSettingValueFieldName, StringType, "Optional initial All-Users default value applied via update-sys-setting after creation."),
					Field("description", StringType, "Optional description text."),
					Field("is-cacheable", BooleanType, "Whether the setting is cacheable. Defaults to true."),
					Field("is-personal", BooleanType, "Whether the setting stores per-user values. Defaults to false."),
					Field(ReferenceSchemaNameFieldName, StringType, "Entity schema name for the lookup target. Required when value-type-name is 'Lookup' (e.g., 'Contact', 'UsrPhoneFormat').")
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, ToolSucceededDescription),
				Field(SysSettingCodeFieldName, StringType, "Sys-setting code echoed from the request."),
				Field(SysSettingValueTypeFieldName, StringType, "Value-type-name applied to the created sys-setting."),
				Field(SysSettingValueFieldName, StringType, "Applied initial value (null when no value was provided or assignment failed)."),
				Field(ErrorFieldName, StringType, FailureMessageDescription),
				Field("warning", StringType, "Optional partial-success warning. Populated when the row was created but the initial value could not be applied; null on a fully successful or fully failed create.")
			),
			CommonErrorContract,
			[],
			[],
			[
				Example("Create a new integer sys-setting with an initial value", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[SysSettingCodeFieldName] = ExampleSysSettingCode,
					["name"] = ExampleSysSettingName,
					[SysSettingValueTypeFieldName] = ExampleSysSettingValueType,
					[SysSettingValueFieldName] = "10485760"
				})
			],
			Flow(
				[
					SysSettingCreateTool.CreateSysSettingToolName,
					SysSettingUpdateTool.UpdateSysSettingToolName
				],
				"Create the sys-setting once, then update the value as it changes."),
			[],
			[]);
	}

	private static ToolContractDefinition BuildUpdateSysSetting() {
		return new ToolContractDefinition(
			SysSettingUpdateTool.UpdateSysSettingToolName,
			"Updates the All-Users default value of an existing Creatio system setting. The setting must already exist — use create-sys-setting first to register a new code.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, SysSettingCodeFieldName, SysSettingValueFieldName],
				[
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription),
					Field(SysSettingCodeFieldName, StringType, "Existing sys-setting code."),
					Field(SysSettingValueFieldName, StringType, "New value. Booleans accept true/false, decimals/integers expect invariant culture, dates/times expect ISO 8601, Lookup expects a Guid or a display name."),
					Field(SysSettingValueTypeFieldName, StringType, "Optional fallback value-type-name when the setting cannot be located on the target environment.")
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, ToolSucceededDescription),
				Field(SysSettingCodeFieldName, StringType, "Sys-setting code echoed from the request."),
				Field(SysSettingValueFieldName, StringType, "Value read back from the environment after the update."),
				Field(ErrorFieldName, StringType, FailureMessageDescription)
			),
			CommonErrorContract,
			[],
			[],
			[
				Example("Update an existing sys-setting value", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[SysSettingCodeFieldName] = ExampleSysSettingCode,
					[SysSettingValueFieldName] = "20971520"
				})
			],
			Flow(
				[
					SysSettingsListTool.ListSysSettingsToolName,
					SysSettingUpdateTool.UpdateSysSettingToolName
				],
				"Discover the existing sys-setting via list-sys-settings, then apply the new value."),
			[],
			[]);
	}
}
