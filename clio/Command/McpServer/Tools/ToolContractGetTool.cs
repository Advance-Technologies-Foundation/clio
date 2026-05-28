using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Command.BusinessRules;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class ToolContractGetTool {
	internal const string ToolName = "get-tool-contract";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Returns the authoritative clio MCP executable contract for discovery, inspection, and mutation tools, including parameter schema, aliases, defaults, examples, and preferred or fallback workflow hints.")]
	public ToolContractGetResponse GetToolContracts(
		[Description("Parameters: tool-names (optional array of tool names). Omit to return the canonical clio MCP contract set.")]
		[Required]
		ToolContractGetArgs args) {
		try {
			string? aliasError = CollectLegacyAliasError(args);
			if (aliasError is not null) {
				return new ToolContractGetResponse(
					false,
					Error: new ToolContractError("invalid-parameter-alias", aliasError));
			}
			return ToolContractCatalog.GetContracts(args.ToolNames);
		} catch (Exception ex) {
			return new ToolContractGetResponse(
				false,
				Error: new ToolContractError(
					"internal-error",
					$"get-tool-contract failed: {ex.Message}. Expected args shape: {{\"tool-names\": [\"list-pages\", ...] }} or omit tool-names to list all."));
		}
	}

	private static readonly Dictionary<string, string> LegacyAliases = new(StringComparer.Ordinal) {
		["toolNames"] = ToolNamesParam,
		["tool_names"] = ToolNamesParam,
		["toolName"] = ToolNamesParam,
		["tool-name"] = ToolNamesParam,
		["tool_name"] = ToolNamesParam,
		["name"] = ToolNamesParam,
		["names"] = ToolNamesParam
	};

	private const string ToolNamesParam = "tool-names";

	private static string? CollectLegacyAliasError(ToolContractGetArgs args) {
		if (args.ExtensionData is null || args.ExtensionData.Count == 0) {
			return null;
		}
		List<string> mapped = [];
		List<string> unknown = [];
		foreach (string key in args.ExtensionData.Keys) {
			if (LegacyAliases.TryGetValue(key, out string? canonical)) {
				mapped.Add($"'{key}' -> '{canonical}'");
			} else {
				unknown.Add($"'{key}'");
			}
		}
		List<string> parts = [];
		if (mapped.Count > 0) {
			parts.Add("Rename: " + string.Join(", ", mapped) + ". tool-names must be an array of strings.");
		}
		if (unknown.Count > 0) {
			parts.Add("Unknown args: " + string.Join(", ", unknown)
				+ ". Valid: tool-names (array of strings). Omit args to list all tools.");
		}
		return parts.Count > 0 ? string.Join(" ", parts) : null;
	}
}

public sealed record ToolContractGetArgs(
	[property: JsonPropertyName("tool-names")]
	[property: Description("Optional array of tool names. Omit to return the canonical clio MCP contract set.")]
	IReadOnlyList<string>? ToolNames = null
) {
	[System.Text.Json.Serialization.JsonExtensionData]
	public Dictionary<string, System.Text.Json.JsonElement>? ExtensionData { get; init; }
}

public sealed record ToolContractGetResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("tools")] IReadOnlyList<ToolContractDefinition>? Tools = null,
	[property: JsonPropertyName("error")] ToolContractError? Error = null
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
	private const string IconBackgroundFieldName = "icon-background";
	private const string InvalidLocalizationMapCode = "invalid-localization-map";
	private const string KeyValueFieldName = "key-value";
	private const string LimitFieldName = "limit";
	private const string LoginFieldName = "login";
	private const string NumberType = "number";
	private const string ObjectType = "object";
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
	private const string MissingRequiredParameterCode = "missing-required-parameter";
	private const string PackageUIdFieldName = "package-u-id";
	private const string PackageNameDescription = "Target package name.";
	private const string PrimaryPackageIdentifierDescription = "Primary package identifier.";
	private const string PrimaryPackageNameDescription = "Primary package name.";
	private const string RuleFieldName = "rule";
	private const string SectionCodeFieldName = "section-code";
	private const string DeleteEntitySchemaFieldName = "delete-entity-schema";
	private const string SearchPatternFieldName = "search-pattern";
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
			[SettingsHealthTool.ToolName] = BuildSettingsHealth(),
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
			[SysSettingUpdateTool.UpdateSysSettingToolName] = BuildUpdateSysSetting()
		};

	private static readonly string[] CanonicalToolNames = [
		GuidanceGetTool.ToolName,
		SettingsHealthTool.ToolName,
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

	internal static ToolContractGetResponse GetContracts(IReadOnlyList<string>? toolNames) {
		if (toolNames is null || toolNames.Count == 0) {
			return new ToolContractGetResponse(
				true,
				CanonicalToolNames.Select(name => Contracts[name]).ToArray());
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
			if (!Contracts.TryGetValue(normalizedName, out ToolContractDefinition? contract)) {
				return new ToolContractGetResponse(
					false,
					Error: new ToolContractError(
						"tool-not-found",
						$"Tool '{normalizedName}' is not registered by clio MCP.",
						BuildSuggestions(normalizedName)));
			}
			results.Add(contract);
		}
		return new ToolContractGetResponse(true, results);
	}

	private static IReadOnlyList<string> BuildSuggestions(string requestedName) {
		return Contracts.Keys
			.OrderBy(name => ComputeDistance(requestedName, name))
			.ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
			.Take(3)
			.ToArray();
	}

	private static int ComputeDistance(string source, string target) {
		if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase)) {
			return 0;
		}
		string left = source.ToLowerInvariant();
		string right = target.ToLowerInvariant();
		int[,] matrix = new int[left.Length + 1, right.Length + 1];
		for (int i = 0; i <= left.Length; i++) {
			matrix[i, 0] = i;
		}
		for (int j = 0; j <= right.Length; j++) {
			matrix[0, j] = j;
		}
		for (int i = 1; i <= left.Length; i++) {
			for (int j = 1; j <= right.Length; j++) {
				int cost = left[i - 1] == right[j - 1] ? 0 : 1;
				matrix[i, j] = Math.Min(
					Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
					matrix[i - 1, j - 1] + cost);
			}
		}
		return matrix[left.Length, right.Length];
	}

	private static ToolContractDefinition BuildToolContractGet() {
		return new ToolContractDefinition(
			ToolContractGetTool.ToolName,
			"Returns the authoritative executable contract for canonical clio MCP discovery, inspection, and mutation tools.",
			new ToolInputSchemaContract(
				[],
				[
					Field("tool-names", ArrayType, "Optional array of tool names. Omit to return the canonical clio MCP contract set.")
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, "Whether the contract lookup succeeded."),
				Field("tools", ArrayType, "Tool contract definitions."),
				Field(ErrorFieldName, ObjectType, "Structured error payload when lookup fails.")
			),
			CommonErrorContract,
			[],
			[],
			[
				Example("Return the canonical clio MCP contract set", new Dictionary<string, object?>()),
				Example("Return the contract for list-apps, update-page, and modify-entity-schema-column", new Dictionary<string, object?> {
					["tool-names"] = new[] { "list-apps", "update-page", "modify-entity-schema-column" }
				})
			],
			Flow(["get-tool-contract"], "Use before execution when the caller needs authoritative clio MCP metadata or must choose the next discovery, inspection, or mutation step."),
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

	private static ToolContractDefinition BuildGuidanceGet() {
		return new ToolContractDefinition(
			GuidanceGetTool.ToolName,
			"Returns canonical clio MCP guidance text by stable guide name so clients can consume workflows and page-authoring rules without fetching docs:// resources directly.",
			new ToolInputSchemaContract(
				["name"],
				[
					Field("name", StringType, "Stable guidance name. Known values include app-modeling, data-bindings, existing-app-maintenance, dataforge-orchestration, page-modification, page-schema-handlers, page-schema-creatio-devkit-common, and page-schema-validators.")
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
					Field("optional-template-data-json", StringType, "Optional JSON object for advanced template configuration.")
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
				Field("entities", ArrayType, "Application entities."),
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
				Default(TemplateCodeFieldName, "AppFreedomUI", "Default template for standard Freedom UI app shells.")
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
					Field("entity-schema-name", StringType, "Optional existing entity schema name. When provided, the section reuses that entity."),
					Field("with-mobile-pages", BooleanType, "Create mobile pages in addition to web pages.")
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
				Field(ErrorFieldName, StringType, FailureMessageDescription)
			),
			CommonErrorContract,
			[
				Alias(ParameterScope, ApplicationCodeFieldName, SelectorCodeFieldName, RejectedStatus, $"Use '{ApplicationCodeFieldName}' instead of '{SelectorCodeFieldName}'."),
				Alias(ParameterScope, ApplicationCodeFieldName, AppCodeFieldName, RejectedStatus, $"Use '{ApplicationCodeFieldName}' instead of '{AppCodeFieldName}'."),
				Alias(ParameterScope, ApplicationCodeFieldName, ApplicationIdFieldName, RejectedStatus, $"Use '{ApplicationCodeFieldName}' instead of '{ApplicationIdFieldName}'."),
				Alias(ParameterScope, "entity-schema-name", "use-existing-entity-schema", RejectedStatus, "Use 'entity-schema-name' alone to reuse an existing entity; the boolean flag is not supported.")
			],
			[
				Default("with-mobile-pages", "true", "Create both web and mobile pages unless the caller explicitly disables mobile pages.")
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
					["with-mobile-pages"] = true
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
						["target-table"] = "Account",
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
					Field("top", NumberType, "Maximum number of records to return, 1-100. Default: 25.")
				],
				Validators: [
					new ToolContractValidator("limit", "invalid-top", "top",
						Context: "top must be between 1 and 100; omitted or out-of-range values default to 25.")
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, "Whether the OData read succeeded."),
				Field(ErrorFieldName, StringType, FailureMessageDescription),
				Field("count", NumberType, "Number of records returned."),
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
			"Creates a Creatio record through OData v4 (POST). Returns the created record including its generated Id.",
			new ToolInputSchemaContract(
				[EntityFieldName, "data", EnvironmentNameFieldName],
				[
					Field(EntityFieldName, StringType, "Creatio OData entity set name such as Contact, Account, or a custom schema."),
					Field("data", ObjectType, "Object of field/value pairs for the new record. Lookup fields are set with their GUID, for example { \"Name\": \"Acme\", \"TypeId\": \"00000000-0000-0000-0000-000000000001\" }."),
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription)
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[SuccessFalseSignal],
				Field(SuccessFieldName, BooleanType, "Whether the OData create succeeded."),
				Field(ErrorFieldName, StringType, FailureMessageDescription),
				Field("id", StringType, "Generated GUID of the created record."),
				Field("record", ObjectType, "The created record returned by Creatio.")
			),
			CommonErrorContract,
			[],
			[],
			[
				Example("Create a contact", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[EntityFieldName] = ExampleContactSchemaName,
					["data"] = new Dictionary<string, object?> { ["Name"] = "Jane Doe", ["JobTitle"] = "CEO" }
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
				Field("entities", ArrayType, "Application entities."),
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
				[EnvironmentNameFieldName, PackageNameFieldName, EntitySchemaNameFieldName, RuleFieldName],
				[
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription),
					Field(PackageNameFieldName, StringType, "Target package name."),
					Field(EntitySchemaNameFieldName, StringType, "Target entity schema name."),
					Field(RuleFieldName, ObjectType, "Structured entity business-rule definition with caption, one top-level condition group, and one or more actions. Unary filled-in comparisons omit rightExpression. Relational comparisons only support numeric and date/time left attributes (Date, DateTime, Time). Set values actions support Const assignments for text, number, boolean, Date, DateTime, Time, and Lookup targets, Formula assignments with simple numeric direct-field expressions such as Field1 + Field2, and AttributeValue assignments from same-typed direct or forward reference paths such as Owner.Age. Apply-filter actions target one lookup field and may use an empty condition group because the filter logic is expressed inside the action itself.")
				],
				Validators: [
					.. BusinessRuleConditionValidators(),
					new ToolContractValidator("enum", "unsupported-action", "rule.actions[*].type",
						Context: $"Supported values: {BusinessRuleConstants.SupportedActionTypesDescription}."),
					new ToolContractValidator("set-values-shape", "invalid-set-values-item", "rule.actions[*].items[*]",
						Context: "When rule.actions[*].type is set-values, each item must provide expression { type: AttributeValue, path } and value { type: Const, value }, { type: Formula, expression }, or { type: AttributeValue, path }. Formula expression must be a string using a simple numeric direct-field arithmetic expression, for example Field1 + Field2. Formula target and source attributes must be numeric; date/time arithmetic is not supported. AttributeValue source paths may be direct columns or forward reference paths like LookupColumn.SourceColumn; the final source attribute and target attribute must have the same data value type. Formula functions, comparison operators, and string literals are not supported in formula scope."),
					new ToolContractValidator("set-values-constant", "unsupported-set-values-constant", "rule.actions[*].items[*].value.value",
						Context: "Set values supports JSON string constants for text targets, JSON number constants for numeric targets, JSON booleans for Boolean targets, yyyy-MM-dd strings for Date targets, ISO 8601 strings with timezone suffix for DateTime targets, ISO 8601 time strings with timezone suffix for Time targets, and GUID string constants for Lookup targets."),
					new ToolContractValidator("set-values-formula", "invalid-set-values-formula", "rule.actions[*].items[*].value.expression",
						Context: "Formula expressions are translated after payload parsing into expression-schema PowerFx metadata, checked locally against a numeric arithmetic whitelist, then validated remotely through ServiceModel/ExpressionService.svc/Validate before saving. Referenced direct numeric source fields are added as business-rule triggers. AttributeValue sources are serialized as business-rule attribute expressions; direct sources trigger on that source column, and forward sources trigger on the root lookup column."),
					new ToolContractValidator("apply-filter-shape", "invalid-apply-filter-action", "rule.actions[*]",
						Context: "When rule.actions[*].type is apply-filter, provide target, targetFilterPath, source, optional sourceFilterPath, clearValue, and populateValue. Target and source must be direct lookup attributes on the root entity. targetFilterPath and sourceFilterPath resolve inside the referenced lookup schemas and must themselves resolve to Lookup attributes, not Guid columns. apply-filter rules support exactly one action and may use an empty condition group."),
					new ToolContractValidator("apply-filter-lookup", "unsupported-apply-filter-lookup", "rule.actions[*].target",
						Context: "apply-filter only supports lookup targets and lookup sources. The final targetFilterPath and source/sourceFilterPath endpoints must both resolve to Lookup attributes that reference the same schema; Guid endpoints are not supported. If sourceFilterPath is provided, populateValue must be false."),
					new ToolContractValidator("apply-static-filter-shape", "invalid-apply-static-filter-action", "rule.actions[*]",
						Context: "When rule.actions[*].type is apply-static-filter, provide targetAttribute (a direct Lookup column on the root entity) and filter (a friendly filter group). rootSchemaName is inferred from the target lookup's reference schema and must never be sent by the caller. apply-static-filter rules support exactly one action and may use an empty condition group."),
					new ToolContractValidator("apply-static-filter-filter", "invalid-apply-static-filter-filter", "rule.actions[*].filter",
						Context: "filter requires logicalOperation (AND or OR) and may include filters[], groups[] for nested logical compositions, and backwardReferenceFilters[] (EXISTS or NOT_EXISTS only in this release). Leaf comparisons: EQUAL, NOT_EQUAL, IS_NULL, IS_NOT_NULL, GREATER, GREATER_OR_EQUAL, LESS, LESS_OR_EQUAL, CONTAIN, NOT_CONTAIN, START_WITH, NOT_START_WITH, END_WITH, NOT_END_WITH. columnPath supports forward paths through Lookup chains. Lookup values accept GUID strings or display names (resolved against the lookup's primary display column). JSON array of strings on a Lookup column with EQUAL/NOT_EQUAL produces a multi-value IN. See guidance resource business-rules for the full contract."),
					new ToolContractValidator("lookup-record", "missing-lookup-record", "rule.actions[*].items[*].value.value",
						Context: $"Lookup set-values constants must be GUID strings for existing records in the target attribute reference schema. Use {ODataReadTool.ToolName} structured filters to resolve or verify the lookup record Id before calling create-entity-business-rule; when filtering records by a lookup value, use traversal paths such as Account/Id.")
				]),
			CommandExecutionOutput(),
			CommonErrorContract,
			[
			],
			[],
			[
				BusinessRuleExample("Create a required-field rule when owner equals a lookup constant",
					"UsrTask", "Require status for a specific owner", "Owner", "equal",
					MakeRequiredActionTypeName, ["Status"], ExampleLookupValueId),
				BusinessRuleExample("Create a readonly rule when a text field is filled in",
					"UsrTask", "Lock planned date when name is filled", "Name", "is-filled-in",
					MakeReadOnlyActionTypeName, ["PlannedDate"]),
				BusinessRuleExample("Create a readonly rule when completed is true",
					"UsrTask", "Lock name and description when completed", "Completed", "equal",
					MakeReadOnlyActionTypeName, ["Name", "Description"], true),
				BusinessRuleExample("Create a required-field rule when annual revenue reaches a numeric threshold",
					"Account", "Require owner for high-revenue accounts", "AnnualRevenue", "greater-than-or-equal",
					MakeRequiredActionTypeName, ["Owner"], 1000000),
				BusinessRuleExample("Create a required-field rule when created date is before a cutoff",
					"UsrTask", "Require owner before the 2025 cutoff", "CreatedOn", "less-than-or-equal",
					MakeRequiredActionTypeName, ["Owner"], "2025-01-01T00:00:00Z"),
				BusinessRuleExample("Create a readonly rule when reminder time is after a timezone-aware cutoff",
					"UsrTask", "Lock reminder note after local noon", "ReminderTime", "greater-than",
					MakeReadOnlyActionTypeName, ["ReminderNote"], "12:00:00+02:00"),
				BusinessRuleExample("Create a Set values rule with text number boolean Date DateTime and Time constants",
					"UsrTask", "Populate defaults when name is filled", "Name", "is-filled-in",
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
					"UsrTask", "Calculate total effort when name is filled", "Name", "is-filled-in",
					"set-values", [
						BusinessRuleFormulaSetValueItem("UsrTotalEffort", "UsrEstimatedEffort + UsrExtraEffort")
					]),
				BusinessRuleExample("Create a Set values rule from a forward reference attribute",
					"UsrTask", "Copy creator age when name changes", "Name", "is-filled-in",
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
					false)
			],
			Flow(
				[
					ApplicationGetListTool.ApplicationGetListToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName,
					ToolContractGetTool.ToolName,
					GuidanceGetTool.ToolName,
					CreateEntityBusinessRuleTool.BusinessRuleCreateToolName
				],
				"When the application exists and the entity is a part of it. Read the business-rules guidance and the create-entity-business-rule contract before calling the mutation tool. Successful rule creation writes add-on metadata directly, so do not add compile-creatio as a routine post-step."),
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
					"When the application exists but the entity is not a part of it. Find entity using find-entity or dataforge-find-tables. Use odata-read structured filters before rule creation when lookup constants must be resolved to real record Ids; filter records by lookup values with traversal paths such as Account/Id."),
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
				"Call get-guidance with name business-rules before calling create-entity-business-rule.",
				"Call get-tool-contract for create-entity-business-rule before building the final payload.",
				"When any lookup condition or lookup set-values constant is needed, call odata-read first and use an existing record Id."
			]);
	}

	private static ToolContractDefinition BuildPageBusinessRuleCreate() {
		return new ToolContractDefinition(
			CreatePageBusinessRuleTool.BusinessRuleCreateToolName,
			"Creates a page-level Freedom UI business rule that changes visibility, editability, or required state of named page elements using datasource-bound page attributes and constants. Read get-guidance business-rules and this get-tool-contract entry before calling.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, PackageNameFieldName, PageSchemaNameFieldName, RuleFieldName],
				[
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription),
					Field(PackageNameFieldName, StringType, "Target package name where the page BusinessRule add-on will be saved."),
					Field(PageSchemaNameFieldName, StringType, "Target Freedom UI page schema name."),
					Field(RuleFieldName, ObjectType, "Structured page business-rule definition with caption, one top-level condition group, and one or more page actions. AttributeValue paths must be declared page attribute names from get-page bundle.viewModelConfig.attributes, not datasource paths like PDS.Priority. Action items must be page element names from recursive get-page bundle.viewConfig. Lookup constants are supported when supplied as stable GUID strings.")
				],
				Validators: [
					.. BusinessRuleConditionValidators(),
					new ToolContractValidator("page-attribute", "unsupported-condition-attribute", "rule.condition.conditions[*].leftExpression.path",
						Context: "Use declared datasource-bound page attribute names from bundle.viewModelConfig.attributes, for example PDS_Priority. Do not use datasource paths like PDS.Priority."),
					new ToolContractValidator("page-attribute", "unsupported-right-attribute", "rule.condition.conditions[*].rightExpression.path",
						Context: "Right-side AttributeValue is supported only when it is also a declared datasource-bound page attribute and resolves to the same data value type as the left attribute."),
					new ToolContractValidator("enum", "unsupported-action", "rule.actions[*].type",
						Context: $"Supported values: {BusinessRuleConstants.SupportedPageActionTypesDescription}."),
					new ToolContractValidator("page-element", "unknown-page-element", "rule.actions[*].items",
						Context: "Use any named element from recursive get-page bundle.viewConfig.")
				]),
			CommandExecutionOutput(),
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
					"equal",
					MakeRequiredActionTypeName,
					["CloseDateInput"],
					"Closed"),
				PageBusinessRuleExample(
					"Make comment optional when page flag is false",
					ExampleOrderPageSchemaName,
					"Make comment optional when flag is false",
					"PDS_UsrFlag",
					"equal",
					"make-optional",
					["CommentInput"],
					false),
				PageBusinessRuleExample(
					"Hide Escalate when priority matches a lookup constant",
					"Case_FormPage",
					"Hide Escalate when priority matches",
					"PDS_Priority",
					"equal",
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
				"Use list-pages or application discovery to choose the page, call get-page to inspect bundle.viewConfig and bundle.viewModelConfig.attributes, then read the business-rules guidance and create-page-business-rule contract before creating the page rule. Successful rule creation writes add-on metadata directly, so do not add compile-creatio as a routine post-step."),
			[
				Flow(
					[
						ApplicationGetListTool.ApplicationGetListToolName,
						ApplicationGetInfoTool.ApplicationGetInfoToolName,
						PageGetTool.ToolName,
						ODataReadTool.ToolName,
						CreatePageBusinessRuleTool.BusinessRuleCreateToolName
					],
					"When the target page belongs to a known application, inspect the application first and then fetch the page bundle before creating the rule. Use odata-read structured filters before rule creation when lookup constants must be resolved to real record Ids; filter records by lookup values with traversal paths such as Account/Id.")
			],
			[],
			[
				new ToolAntiPattern(
					"Using datasource paths like PDS.Priority in rule.condition.conditions[*].leftExpression.path.",
					"Page business rules use declared view-model attribute names from bundle.viewModelConfig.attributes so the generated metadata and triggers match the page runtime.")
			],
			[
				"Call get-guidance with name business-rules before calling create-page-business-rule.",
				"Call get-tool-contract for create-page-business-rule before building the final payload.",
				"When any lookup condition constant is needed, call odata-read first and use an existing record Id. When filtering records by a lookup value, use structured filters with a traversal path such as Account/Id."
			]);
	}

	private static ToolContractValidator[] BusinessRuleConditionValidators() =>
		[
			new ToolContractValidator("enum", "unsupported-operator", "rule.condition.logicalOperation",
				Context: "Supported values: AND, OR."),
			new ToolContractValidator("enum", "unsupported-comparison", "rule.condition.conditions[*].comparisonType",
				Context: $"Supported values: {BusinessRuleConstants.SupportedComparisonTypesDescription}."),
			new ToolContractValidator("conditional-field", "invalid-right-expression-shape", "rule.condition.conditions[*].rightExpression",
				Context: "Required for equal, not-equal, greater-than, greater-than-or-equal, less-than, and less-than-or-equal. Omit or null for is-filled-in and is-not-filled-in."),
			new ToolContractValidator("comparison-family", "unsupported-relational-operands", "rule.condition.conditions[*]",
				Context: "greater-than, greater-than-or-equal, less-than, and less-than-or-equal only support numeric and date/time left attributes (Date, DateTime, Time). Attribute-to-attribute relational comparisons must use matching data value types."),
			new ToolContractValidator("comparison-family", "unsupported-equality-operands", "rule.condition.conditions[*]",
				Context: "equal and not-equal are not supported when the left attribute data value type is RichText or Image. Use is-filled-in or is-not-filled-in for those attributes."),
			new ToolContractValidator("date-time-constant", "invalid-date-time-constant", "rule.condition.conditions[*].rightExpression.value",
				Context: "Date constants must be JSON strings in yyyy-MM-dd format. DateTime constants must be JSON strings in ISO 8601 date-time format with a timezone suffix ('Z' or '+/-HH:mm'). Time constants must be JSON strings in ISO 8601 time format with a timezone suffix ('Z' or '+/-HH:mm')."),
			new ToolContractValidator("lookup-record", "missing-lookup-record", "rule.condition.conditions[*].rightExpression.value",
				Context: $"Lookup constants must be GUID strings for existing records in the attribute reference schema. Use {ODataReadTool.ToolName} structured filters to resolve or verify the lookup record Id before calling the business-rule creation tool; when filtering records by a lookup value, use traversal paths such as Account/Id.")
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
			[RuleFieldName] = new Dictionary<string, object?> {
				["caption"] = caption,
				["condition"] = new Dictionary<string, object?> {
					["logicalOperation"] = "AND",
					["conditions"] = System.Array.Empty<object>()
				},
				["actions"] = new object[] { action }
			}
		});
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
			[RuleFieldName] = new Dictionary<string, object?> {
				["caption"] = caption,
				["condition"] = new Dictionary<string, object?> {
					["logicalOperation"] = "AND",
					["conditions"] = new object[] {
						condition
					}
				},
				["actions"] = new object[] {
					new Dictionary<string, object?> {
						["type"] = actionType,
						["items"] = actionItems
					}
				}
			}
		});
	}

	private static ToolContractExample PageBusinessRuleAttributeComparisonExample() {
		return Example("Hide a warning when two datasource-bound page attributes match", new Dictionary<string, object?> {
			[EnvironmentNameFieldName] = ExampleEnvironmentName,
			[PackageNameFieldName] = ExamplePackageName,
			[PageSchemaNameFieldName] = ExampleOrderPageSchemaName,
			[RuleFieldName] = new Dictionary<string, object?> {
				["caption"] = "Hide warning when planned and actual dates match",
				["condition"] = new Dictionary<string, object?> {
					["logicalOperation"] = "AND",
					["conditions"] = new object[] {
						new Dictionary<string, object?> {
							["leftExpression"] = new Dictionary<string, object?> {
								["type"] = "AttributeValue",
								["path"] = "PDS_UsrPlannedDate"
							},
							["comparisonType"] = "equal",
							["rightExpression"] = new Dictionary<string, object?> {
								["type"] = "AttributeValue",
								["path"] = "PDS_UsrActualDate"
							}
						}
					}
				},
				["actions"] = new object[] {
					new Dictionary<string, object?> {
						["type"] = "hide-element",
						["items"] = new object[] { "DateMismatchWarningLabel" }
					}
				}
			}
		});
	}

	private static ToolContractDefinition BuildSchemaSync() {
		return new ToolContractDefinition(
			SchemaSyncTool.ToolName,
			"Batches create-lookup, create-entity, update-entity, and inline seed operations in one call. Requests use operations[*].type; do not send operations[*].operation.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, PackageNameFieldName, OperationsFieldName],
				EnvironmentPackageFields(
					Field(OperationsFieldName, ArrayType, "Ordered schema operations.")),
				Validators: [
					new ToolContractValidator(
						"sync-schemas-operations-localizations",
						InvalidLocalizationMapCode,
						Field: OperationsFieldName)
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
			[],
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
					Field("limit", NumberType, "Optional max result count.")),
				Validators: [
					new ToolContractValidator(
						"mutually-exclusive-fields",
						InvalidWorkflowShapeCode,
						Fields: [
							PackageNameFieldName,
							SelectorCodeFieldName
						],
						Context: "list-pages accepts package-name or code, not both."),
				],
				AnyOf: EnvironmentOrExplicitConnectionRequirements()),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, ToolSucceededDescription),
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
					Field("extend-parent", BooleanType, "Optional replacement-schema flag.")),
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
			[],
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
			"Returns detailed metadata for one deployed entity schema column for read-before-write inspection and read-back verification.",
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
					Field("default-value-config", ObjectType, "Structured default value metadata with source None, Const, Settings, SystemValue, or Sequence. Settings value-source accepts code/name/id and resolves to code. SystemValue value-source accepts GUID/alias/caption and resolves to GUID."))),
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
					["default-value-config"] = new Dictionary<string, object?> {
						["source"] = "SystemValue",
						["value-source"] = "Current Time and Date"
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
			"Returns grouped Freedom UI component summaries or a full component contract for one component type.",
			new ToolInputSchemaContract(
				[],
				[
					Field("component-type", StringType, "Optional component type. Omit or use list to return the grouped catalog."),
					Field("search", StringType, "Optional keyword filter for list mode.")
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, ToolSucceededDescription),
				Field("mode", StringType, "detail or list."),
				Field("count", NumberType, "Number of matching components."),
				Field("groups", ArrayType, "Grouped list-mode results."),
				Field("componentType", StringType, "Component type for detail mode."),
				Field(ErrorFieldName, StringType, FailureMessageDescription)
			),
			CommonErrorContract,
			[
				Alias(ParameterScope, "component-type", "componentType", RejectedStatus, "Use 'component-type' instead of 'componentType'.")
			],
			[],
			[
				Example("Inspect one component contract", new Dictionary<string, object?> {
					["component-type"] = "crt.TabContainer"
				})
			],
			Flow([ComponentInfoTool.ToolName], "Use after get-page when bundle.viewConfig contains unfamiliar crt.* component types."),
			[],
			[]);
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
		return Alias(ParameterScope, "default-value-config", "defaultValueConfig", RejectedStatus,
			"Use 'default-value-config' instead of 'defaultValueConfig'.");
	}

	private static ToolContractAlias DefaultValueSourceParameterAlias() {
		return Alias(ParameterScope, "default-value-source", "defaultValueSource", RejectedStatus,
			"Use 'default-value-source' instead of 'defaultValueSource'.");
	}

	private static ToolContractAlias TitleParameterAlias() {
		return Alias(ParameterScope, TitleLocalizationsFieldName, "title", RejectedStatus,
			$"Use '{TitleLocalizationsFieldName}' instead of legacy scalar 'title'.");
	}

	private static ToolContractAlias CaptionParameterAlias() {
		return Alias(ParameterScope, TitleLocalizationsFieldName, CaptionFieldName, RejectedStatus,
			$"Use '{TitleLocalizationsFieldName}' instead of legacy scalar 'caption'.");
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
					[SearchPatternFieldName] = "UsrTask"
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
				"Caller must NOT call this tool after `create-app`, `update-page`, `sync-pages`, `update-entity-schema`, `create-page`, `create-entity-business-rule`, or `create-page-business-rule`."
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
