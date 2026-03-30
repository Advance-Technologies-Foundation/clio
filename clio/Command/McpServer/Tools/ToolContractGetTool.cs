using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class ToolContractGetTool {
	internal const string ToolName = "tool-contract-get";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Returns the authoritative clio MCP executable contract for discovery, inspection, and mutation tools, including parameter schema, aliases, defaults, examples, and preferred or fallback workflow hints.")]
	public ToolContractGetResponse GetToolContracts(
		[Description("Parameters: tool-names (optional array of tool names). Omit to return the canonical clio MCP contract set.")]
		[Required]
		ToolContractGetArgs args) {
		return ToolContractCatalog.GetContracts(args.ToolNames);
	}
}

public sealed record ToolContractGetArgs(
	[property: JsonPropertyName("tool-names")]
	[property: Description("Optional array of tool names. Omit to return the canonical clio MCP contract set.")]
	IReadOnlyList<string>? ToolNames = null
);

public sealed record ToolContractGetResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("tools")] IReadOnlyList<ToolContractDefinition>? Tools = null,
	[property: JsonPropertyName("error")] ToolContractError? Error = null
);

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
	[property: JsonPropertyName("deprecations")] IReadOnlyList<ToolDeprecation> Deprecations
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
	private const string ArrayType = "array";
	private const string BindingNameFieldName = "binding-name";
	private const string BooleanType = "boolean";
	private const string ColumnNameFieldName = "column-name";
	private const string DescriptionLocalizationsFieldName = "description-localizations";
	private const string EntitySchemaNameDescription = "Entity schema name.";
	private const string EnvironmentNameFieldName = "environment-name";
	private const string ErrorFieldName = "error";
	private const string ExampleEnvironmentName = "local";
	private const string ExamplePackageName = "UsrTaskApp";
	private const string ExampleTaskStatusSchemaName = "UsrTaskStatus";
	private const string FailureMessageDescription = "Human-readable failure message.";
	private const string IconBackgroundFieldName = "icon-background";
	private const string InvalidLocalizationMapCode = "invalid-localization-map";
	private const string KeyValueFieldName = "key-value";
	private const string NumberType = "number";
	private const string ObjectType = "object";
	private const string OperationsFieldName = "operations";
	private const string PackageNameFieldName = "package-name";
	private const string PagesFieldName = "pages";
	private const string ParameterScope = "parameter";
	private const string ReferenceSchemaNameFieldName = "reference-schema-name";
	private const string RegisteredEnvironmentNameDescription = "Registered clio environment name.";
	private const string RejectedStatus = "rejected";
	private const string SchemaNameFieldName = "schema-name";
	private const string StringType = "string";
	private const string SuccessFalseSignal = "success == false";
	private const string SuccessFieldName = "success";
	private const string TemplateCodeFieldName = "template-code";
	private const string TitleLocalizationsFieldName = "title-localizations";
	private const string ToolSucceededDescription = "Whether the tool succeeded.";

	private static readonly ToolErrorContract CommonErrorContract = new([
		new ToolErrorCodeContract("tool-not-found", "Requested tool name is not registered by clio MCP."),
		new ToolErrorCodeContract("missing-required-parameter", "A required parameter is missing."),
		new ToolErrorCodeContract("invalid-parameter-alias", "A legacy or unsupported parameter alias was used."),
		new ToolErrorCodeContract("invalid-parameter-type", "A parameter value type does not match the tool contract."),
		new ToolErrorCodeContract(InvalidLocalizationMapCode, "A localization map is malformed or missing en-US."),
		new ToolErrorCodeContract("invalid-workflow-shape", "The request shape is structurally invalid for the target tool.")
	]);

	private static readonly IReadOnlyDictionary<string, ToolContractDefinition> Contracts =
		new Dictionary<string, ToolContractDefinition>(StringComparer.OrdinalIgnoreCase) {
			[ToolContractGetTool.ToolName] = BuildToolContractGet(),
			[ApplicationCreateTool.ApplicationCreateToolName] = BuildApplicationCreate(),
			[ApplicationGetInfoTool.ApplicationGetInfoToolName] = BuildApplicationGetInfo(),
			[ApplicationGetListTool.ApplicationGetListToolName] = BuildApplicationGetList(),
			[SchemaSyncTool.ToolName] = BuildSchemaSync(),
			[PageSyncTool.ToolName] = BuildPageSync(),
			[PageListTool.ToolName] = BuildPageList(),
			[PageGetTool.ToolName] = BuildPageGet(),
			[CreateLookupTool.CreateLookupToolName] = BuildCreateLookup(),
			[CreateEntitySchemaTool.CreateEntitySchemaToolName] = BuildCreateEntity(),
			[UpdateEntitySchemaTool.UpdateEntitySchemaToolName] = BuildUpdateEntity(),
			[CreateDataBindingDbTool.CreateDataBindingDbToolName] = BuildCreateDataBindingDb(),
			[UpsertDataBindingRowDbTool.UpsertDataBindingRowDbToolName] = BuildUpsertDataBindingRowDb(),
			[RemoveDataBindingRowDbTool.RemoveDataBindingRowDbToolName] = BuildRemoveDataBindingRowDb(),
			[GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName] = BuildGetEntitySchemaProperties(),
			[GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName] = BuildGetEntitySchemaColumnProperties(),
			[ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName] = BuildModifyEntitySchemaColumn(),
			[ComponentInfoTool.ToolName] = BuildComponentInfo(),
			[PageUpdateTool.ToolName] = BuildPageUpdate(),
			[ApplicationDeleteTool.ToolName] = BuildApplicationDelete()
		};

	private static readonly string[] CanonicalToolNames = [
		ApplicationCreateTool.ApplicationCreateToolName,
		ApplicationGetInfoTool.ApplicationGetInfoToolName,
		ApplicationGetListTool.ApplicationGetListToolName,
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
		GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName,
		GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName,
		ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName,
		ComponentInfoTool.ToolName,
		PageUpdateTool.ToolName,
		ApplicationDeleteTool.ToolName
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
						"missing-required-parameter",
						"tool-names must contain non-empty tool names.",
						FieldErrors: [
							new ToolContractFieldError($"tool-names[{index}]", "missing-required-parameter",
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
				Example("Return the contract for application-get-list, page-update, and modify-entity-schema-column", new Dictionary<string, object?> {
					["tool-names"] = new[] { "application-get-list", "page-update", "modify-entity-schema-column" }
				})
			],
			Flow(["tool-contract-get"], "Use before execution when the caller needs authoritative clio MCP metadata or must choose the next discovery, inspection, or mutation step."),
			[],
			[]);
	}

	private static ToolContractDefinition BuildApplicationCreate() {
		return new ToolContractDefinition(
			ApplicationCreateTool.ApplicationCreateToolName,
			"Creates a Creatio application and returns installed application identity plus the created application context envelope.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, "name", "code", TemplateCodeFieldName, IconBackgroundFieldName],
				[
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription),
					Field("name", StringType, "Application display name."),
					Field("code", StringType, "Application code starting with Usr."),
					Field(TemplateCodeFieldName, StringType, "Technical template code such as AppFreedomUI."),
					Field(IconBackgroundFieldName, StringType, "Hex color string in #RRGGBB format."),
					Field("description", StringType, "Optional application description."),
					Field("icon-id", StringType, "Optional icon GUID or 'auto'."),
					Field("client-type-id", StringType, "Optional client type identifier."),
					Field("optional-template-data-json", StringType, "Optional JSON object for advanced template configuration.")
				],
				Validators: [
					new ToolContractValidator(
						"forbid-fields",
						"invalid-workflow-shape",
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
						Context: "application-create stays scalar-only; localized captions belong to follow-up schema tools.")
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, ToolSucceededDescription),
				Field("package-u-id", StringType, "Primary package identifier."),
				Field(PackageNameFieldName, StringType, "Primary package name."),
				Field("canonical-main-entity-name", StringType, "Canonical main entity name."),
				Field("application-id", StringType, "Installed application identifier."),
				Field("application-name", StringType, "Installed application display name."),
				Field("application-code", StringType, "Installed application code."),
				Field("application-version", StringType, "Installed application version."),
				Field("entities", ArrayType, "Application entities."),
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
				Example("Create a new Freedom UI application", new Dictionary<string, object?> {
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
				"Use application-create for the app shell, then schema-sync for entity mutations, then refresh once with application-get-info."),
			[
				Flow(
					[
						ApplicationGetListTool.ApplicationGetListToolName,
						ApplicationGetInfoTool.ApplicationGetInfoToolName
					],
					"Fallback when the app already exists and the flow must switch to existing-app discovery.")
			],
			[]);
	}

	private static ToolContractDefinition BuildApplicationGetInfo() {
		return new ToolContractDefinition(
			ApplicationGetInfoTool.ApplicationGetInfoToolName,
			"Returns installed application identity plus current package and entity metadata so callers can inspect the right app before mutating it.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName],
				[
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription),
					Field("app-id", StringType, "Application GUID."),
					Field(AppCodeFieldName, StringType, "Application code.")
				],
				AnyOf: [
					new[] { "app-id" },
					[AppCodeFieldName]
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, ToolSucceededDescription),
				Field("package-u-id", StringType, "Primary package identifier."),
				Field(PackageNameFieldName, StringType, "Primary package name."),
				Field("canonical-main-entity-name", StringType, "Canonical main entity name."),
				Field("application-id", StringType, "Installed application identifier."),
				Field("application-name", StringType, "Installed application display name."),
				Field("application-code", StringType, "Installed application code."),
				Field("application-version", StringType, "Installed application version."),
				Field("entities", ArrayType, "Application entities."),
				Field(ErrorFieldName, StringType, FailureMessageDescription)
			),
			CommonErrorContract,
			[],
			[],
			[
				Example("Refresh app context by code", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[AppCodeFieldName] = ExamplePackageName
				})
			],
			Flow(
				[
					ApplicationGetListTool.ApplicationGetListToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName
				],
				"Use after application-get-list when the target app is not fully known, or refresh again after mutations when app context must be re-read."),
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
				Example("List installed applications", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName
				})
			],
			Flow(
				[
					ApplicationGetListTool.ApplicationGetListToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName
				],
				"Use when the workflow must branch into existing-app discovery."),
			[],
			[]);
	}

	private static ToolContractDefinition BuildSchemaSync() {
		return new ToolContractDefinition(
			SchemaSyncTool.ToolName,
			"Batches create-lookup, create-entity, update-entity, and inline seed operations in one call.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, PackageNameFieldName, OperationsFieldName],
				EnvironmentPackageFields(
					Field(OperationsFieldName, ArrayType, "Ordered schema operations.")),
				Validators: [
					new ToolContractValidator(
						"schema-sync-operations-localizations",
						InvalidLocalizationMapCode,
						Field: OperationsFieldName)
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, "Whether every schema-sync operation succeeded."),
				Field("results", ArrayType, "Per-operation execution results.")
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
			"Batches page body validation, save, and optional read-back verification for multiple pages.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, PagesFieldName],
				[
					Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription),
					Field(PagesFieldName, ArrayType, "Page update requests."),
					Field("validate", BooleanType, "Run client-side validation before save."),
					Field("verify", BooleanType, "Read the page back after save.")
				]),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, "Whether every page operation succeeded."),
				Field(PagesFieldName, ArrayType, "Per-page results.")
			),
			CommonErrorContract,
			[],
			[
				Default("validate", "true", "Client-side validation is enabled by default."),
				Default("verify", "false", "Read-back verification is optional and disabled by default.")
			],
			[
				Example("Validate and save a single page", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[PagesFieldName] = new object[] {
						new Dictionary<string, object?> {
							[SchemaNameFieldName] = "UsrTaskApp_FormPage",
							["body"] = "define(...)"
						}
					},
					["validate"] = true,
					["verify"] = true
				})
			],
			Flow(
				[
					PageListTool.ToolName,
					PageGetTool.ToolName,
					PageSyncTool.ToolName,
					PageGetTool.ToolName
				],
				"Canonical write path for page synchronization."),
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
			"Lists Freedom UI pages for the requested package with package and parent schema context so the caller can discover candidate page schemas before inspection or mutation.",
			new ToolInputSchemaContract(
				[],
				EnvironmentOrExplicitConnectionFields(
					Field(PackageNameFieldName, StringType, "Package name to inspect."),
					Field("search-pattern", StringType, "Optional case-insensitive schema-name filter."),
					Field("limit", NumberType, "Optional max result count.")),
				AnyOf: EnvironmentOrExplicitConnectionRequirements()),
			EnvelopeOutput(
				SuccessFieldName,
				[
					SuccessFalseSignal
				],
				Field(SuccessFieldName, BooleanType, ToolSucceededDescription),
				Field(PagesFieldName, ArrayType, "Discovered pages with schema, package, and parent schema context."),
				Field(ErrorFieldName, StringType, FailureMessageDescription)
			),
			CommonErrorContract,
			[
				PackageNameParameterAlias(),
				Alias(ParameterScope, "search-pattern", "searchPattern", RejectedStatus, "Use 'search-pattern' instead of 'searchPattern'."),
				EnvironmentNameParameterAlias()
			],
			[],
			[
				Example("List pages in the target package", new Dictionary<string, object?> {
					[PackageNameFieldName] = ExamplePackageName,
					[EnvironmentNameFieldName] = ExampleEnvironmentName
				})
			],
			Flow(
				[
					PageListTool.ToolName,
					PageGetTool.ToolName,
					PageUpdateTool.ToolName
				],
				"Use when the page schema is not yet known and the workflow is a minimal single-page edit."),
			[
				Flow(
					[
						PageListTool.ToolName,
						PageGetTool.ToolName,
						PageSyncTool.ToolName,
						PageGetTool.ToolName
					],
					"Fallback when the workflow needs multi-page save orchestration or explicit page read-back verification.")
			],
			[]);
	}

	private static ToolContractDefinition BuildPageGet() {
		return new ToolContractDefinition(
			PageGetTool.ToolName,
			"Reads a Freedom UI page bundle plus the raw editable body so the caller can inspect before mutating.",
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
				Field("page", ObjectType, "Page metadata."),
				Field("bundle", ObjectType, "Merged page bundle."),
				Field("raw", ObjectType, "Raw body and related data."),
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
					PageUpdateTool.ToolName,
					PageGetTool.ToolName
				],
				"Use after page-list to inspect the raw body before a minimal page edit and to read back after saving when needed."),
			[
				Flow(
					[
						PageGetTool.ToolName,
						ComponentInfoTool.ToolName,
						PageUpdateTool.ToolName
					],
					"Call component-info before editing when bundle.viewConfig contains unfamiliar crt.* component types.")
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
					Field("columns", ArrayType, "Optional custom columns.")),
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
					Field("parent-schema-name", StringType, "Optional parent schema name."),
					Field("extend-parent", BooleanType, "Optional replacement-schema flag.")),
				Validators: [
					RequiredLocalizationMapValidator(TitleLocalizationsFieldName)
				]),
			CommandExecutionOutput(),
			CommonErrorContract,
			EnvironmentPackageSchemaAliases(
				Alias(ParameterScope, "parent-schema-name", "parentSchemaName", RejectedStatus, "Use 'parent-schema-name' instead of 'parentSchemaName'."),
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
					["parent-schema-name"] = "BaseEntity"
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
			"Creates or updates a DB-first package data binding and optionally applies rows immediately.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, PackageNameFieldName, SchemaNameFieldName],
				EnvironmentPackageSchemaFields(
					"Entity schema name for the binding.",
					Field(BindingNameFieldName, StringType, "Optional binding name; defaults to the schema name."),
					Field("rows", StringType, "Optional JSON array of rows."))),
			CommandExecutionOutput(),
			CommonErrorContract,
			EnvironmentPackageSchemaAliases(),
			[],
			[
				Example("Create a default lookup binding with rows", new Dictionary<string, object?> {
					[EnvironmentNameFieldName] = ExampleEnvironmentName,
					[PackageNameFieldName] = ExamplePackageName,
					[SchemaNameFieldName] = ExampleTaskStatusSchemaName,
					["rows"] = "[{\"values\":{\"Name\":\"New\"}}]"
				})
			],
			Flow([SchemaSyncTool.ToolName], "Prefer inline seed-rows inside schema-sync when the flow can stay batched."),
			[],
			[
				new ToolDeprecation(
					"Prefer schema-sync with inline seed-rows as the canonical path. Keep create-data-binding-db for explicit fallback or standalone binding work.",
					[
						SchemaSyncTool.ToolName
					])
			]);
	}

	private static ToolContractDefinition BuildUpsertDataBindingRowDb() {
		return new ToolContractDefinition(
			UpsertDataBindingRowDbTool.UpsertDataBindingRowDbToolName,
			"Upserts a single row in an existing DB-first binding. " +
			"The binding must already exist; call create-data-binding-db first if it does not.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, PackageNameFieldName, BindingNameFieldName, "values"],
				EnvironmentPackageFields(
					Field(BindingNameFieldName, StringType, "Binding name."),
					Field("values", StringType, "JSON object keyed by column name."))),
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
					["values"] = "{\"Name\":\"New\"}"
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
			"Removes a single row from an existing DB-first binding by key value.",
			new ToolInputSchemaContract(
				[EnvironmentNameFieldName, PackageNameFieldName, BindingNameFieldName, KeyValueFieldName],
				EnvironmentPackageFields(
					Field(BindingNameFieldName, StringType, "Binding name."),
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
					[KeyValueFieldName] = "00000000-0000-0000-0000-000000000001"
				})
			],
			Flow([RemoveDataBindingRowDbTool.RemoveDataBindingRowDbToolName], "Standalone DB-first binding maintenance."),
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
				Field("columns", ArrayType, "Column metadata.")),
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
					"Fallback when the schema change is part of a larger ordered schema-sync workflow.")
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
					"Fallback when the requested work expands into a larger ordered schema-sync plan.")
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
					Field("default-value-config", ObjectType, "Structured default value metadata with source None, Const, Settings, SystemValue, or Sequence."))),
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
						["value-source"] = "CurrentDateTime"
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
			Flow([ComponentInfoTool.ToolName], "Use after page-get when bundle.viewConfig contains unfamiliar crt.* component types."),
			[],
			[]);
	}

	private static ToolContractDefinition BuildPageUpdate() {
		return new ToolContractDefinition(
			PageUpdateTool.ToolName,
			"Saves a full Freedom UI page body for one page as the minimal page-mutation path.",
			new ToolInputSchemaContract(
				[SchemaNameFieldName, "body"],
				EnvironmentOrExplicitConnectionFields(
					Field(SchemaNameFieldName, StringType, "Freedom UI page schema name."),
					Field("body", StringType, "Full page body with all marker pairs."),
					Field("dry-run", BooleanType, "Validate without saving."),
					Field("resources", StringType, "Optional JSON object of resource strings.")),
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
				Alias(ParameterScope, "dry-run", "dryRun", RejectedStatus, "Use 'dry-run' instead of 'dryRun'.")
			],
			[],
			[
				Example("Dry-run validate one page body", new Dictionary<string, object?> {
					[SchemaNameFieldName] = "UsrTaskApp_FormPage",
					["body"] = "define(...)",
					["dry-run"] = true,
					[EnvironmentNameFieldName] = ExampleEnvironmentName
				})
			],
			Flow(
				[
					PageGetTool.ToolName,
					PageUpdateTool.ToolName,
					PageGetTool.ToolName
				],
				"Use for a minimal single-page edit after reading the raw body with page-get and read back with page-get when verification is needed."),
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
					"Fallback when the workflow expands into a multi-page save or ordered page-sync plan.")
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

	private static IReadOnlyList<ToolContractField> EnvironmentOrExplicitConnectionFields(params ToolContractField[] leadingFields) {
		return [
			..leadingFields,
			Field(EnvironmentNameFieldName, StringType, RegisteredEnvironmentNameDescription),
			Field("uri", StringType, "Explicit Creatio URL."),
			Field("login", StringType, "Explicit login."),
			Field("password", StringType, "Explicit password.")
		];
	}

	private static IReadOnlyList<IReadOnlyList<string>> EnvironmentOrExplicitConnectionRequirements() {
		return [
			[EnvironmentNameFieldName],
			["uri", "login", "password"]
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
		return Alias(ParameterScope, TitleLocalizationsFieldName, "caption", RejectedStatus,
			$"Use '{TitleLocalizationsFieldName}' instead of legacy scalar 'caption'.");
	}

	private static ToolContractAlias DescriptionParameterAlias() {
		return Alias(ParameterScope, DescriptionLocalizationsFieldName, "description", RejectedStatus,
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

	private static ToolContractValidator RequiredLocalizationMapValidator(string field) {
		return new ToolContractValidator("localizations-map", InvalidLocalizationMapCode, field,
			Context: "Parameter", Required: true);
	}

	private static ToolFlowHint PreferSchemaSyncFlow() {
		return Flow([SchemaSyncTool.ToolName], "Prefer schema-sync for ordered multi-step entity work.");
	}

	private static IReadOnlyList<ToolDeprecation> PreferSchemaSyncDeprecations(string toolName) {
		return [
			new ToolDeprecation(
				$"Prefer schema-sync as the canonical entity mutation path. Keep {toolName} for explicit fallback or isolated operations.",
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
}
