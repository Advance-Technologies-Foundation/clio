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
	[Description("Returns the authoritative clio MCP executable contract for app-generation tools, including parameter schema, aliases, defaults, examples, and preferred or fallback workflow hints.")]
	public ToolContractGetResponse GetToolContracts(
		[Description("Parameters: tool-names (optional array of tool names). Omit to return all app-generation tool contracts.")]
		[Required]
		ToolContractGetArgs args) {
		return ToolContractCatalog.GetContracts(args.ToolNames);
	}
}

public sealed record ToolContractGetArgs(
	[property: JsonPropertyName("tool-names")]
	[property: Description("Optional array of tool names. Omit to return all app-generation tool contracts.")]
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
	private static readonly ToolErrorContract CommonErrorContract = new([
		new ToolErrorCodeContract("tool-not-found", "Requested tool name is not registered by clio MCP."),
		new ToolErrorCodeContract("missing-required-parameter", "A required parameter is missing."),
		new ToolErrorCodeContract("invalid-parameter-alias", "A legacy or unsupported parameter alias was used."),
		new ToolErrorCodeContract("invalid-parameter-type", "A parameter value type does not match the tool contract."),
		new ToolErrorCodeContract("invalid-localization-map", "A localization map is malformed or missing en-US."),
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

	private static readonly string[] AppGenerationToolNames = [
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
				AppGenerationToolNames.Select(name => Contracts[name]).ToArray());
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
			"Returns the authoritative executable contract for clio MCP app-generation tools.",
			new ToolInputSchemaContract(
				[],
				[
					Field("tool-names", "array", "Optional array of tool names. Omit to return all app-generation tool contracts.")
				]),
			EnvelopeOutput(
				"success",
				[
					"success == false"
				],
				Field("success", "boolean", "Whether the contract lookup succeeded."),
				Field("tools", "array", "Tool contract definitions."),
				Field("error", "object", "Structured error payload when lookup fails.")
			),
			CommonErrorContract,
			[],
			[],
			[
				Example("Return all app-generation tool contracts", new Dictionary<string, object?>()),
				Example("Return the contract for application-create and schema-sync", new Dictionary<string, object?> {
					["tool-names"] = new[] { "application-create", "schema-sync" }
				})
			],
			Flow(["tool-contract-get"], "Use before execution when the caller needs authoritative MCP metadata."),
			[],
			[]);
	}

	private static ToolContractDefinition BuildApplicationCreate() {
		return new ToolContractDefinition(
			ApplicationCreateTool.ApplicationCreateToolName,
			"Creates a Creatio application and returns the created application context envelope.",
			new ToolInputSchemaContract(
				["environment-name", "name", "code", "template-code", "icon-background"],
				[
					Field("environment-name", "string", "Registered clio environment name."),
					Field("name", "string", "Application display name."),
					Field("code", "string", "Application code starting with Usr."),
					Field("template-code", "string", "Technical template code such as AppFreedomUI."),
					Field("icon-background", "string", "Hex color string in #RRGGBB format."),
					Field("description", "string", "Optional application description."),
					Field("icon-id", "string", "Optional icon GUID or 'auto'."),
					Field("client-type-id", "string", "Optional client type identifier."),
					Field("optional-template-data-json", "string", "Optional JSON object for advanced template configuration.")
				],
				Validators: [
					new ToolContractValidator(
						"forbid-fields",
						"invalid-workflow-shape",
						Fields: [
							"title-localizations",
							"description-localizations",
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
				"success",
				[
					"success == false"
				],
				Field("success", "boolean", "Whether the tool succeeded."),
				Field("package-u-id", "string", "Primary package identifier."),
				Field("package-name", "string", "Primary package name."),
				Field("canonical-main-entity-name", "string", "Canonical main entity name."),
				Field("entities", "array", "Application entities."),
				Field("error", "string", "Human-readable failure message.")
			),
			CommonErrorContract,
			[
				Alias("parameter", "code", "app-code", "rejected", "Use 'code' instead of 'app-code'."),
				Alias("parameter", "name", "app-name", "rejected", "Use 'name' instead of 'app-name'."),
				Alias("parameter", "template-code", "templateCode", "rejected", "Use 'template-code' instead of 'templateCode'."),
				Alias("parameter", "icon-background", "iconBackground", "rejected", "Use 'icon-background' instead of 'iconBackground'.")
			],
			[
				Default("template-code", "AppFreedomUI", "Default template for standard Freedom UI app shells.")
			],
			[
				Example("Create a new Freedom UI application", new Dictionary<string, object?> {
					["environment-name"] = "local",
					["name"] = "Task App",
					["code"] = "UsrTaskApp",
					["template-code"] = "AppFreedomUI",
					["icon-background"] = "#1F5F8B"
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
			"Returns the current package and entity metadata for an installed application.",
			new ToolInputSchemaContract(
				["environment-name"],
				[
					Field("environment-name", "string", "Registered clio environment name."),
					Field("app-id", "string", "Application GUID."),
					Field("app-code", "string", "Application code.")
				],
				AnyOf: [
					new[] { "app-id" },
					new[] { "app-code" }
				]),
			EnvelopeOutput(
				"success",
				[
					"success == false"
				],
				Field("success", "boolean", "Whether the tool succeeded."),
				Field("package-u-id", "string", "Primary package identifier."),
				Field("package-name", "string", "Primary package name."),
				Field("canonical-main-entity-name", "string", "Canonical main entity name."),
				Field("entities", "array", "Application entities."),
				Field("error", "string", "Human-readable failure message.")
			),
			CommonErrorContract,
			[],
			[],
			[
				Example("Refresh app context by code", new Dictionary<string, object?> {
					["environment-name"] = "local",
					["app-code"] = "UsrTaskApp"
				})
			],
			Flow([ApplicationGetInfoTool.ApplicationGetInfoToolName], "Refresh once after schema-sync completes."),
			[],
			[]);
	}

	private static ToolContractDefinition BuildApplicationGetList() {
		return new ToolContractDefinition(
			ApplicationGetListTool.ApplicationGetListToolName,
			"Lists installed applications from the target Creatio environment.",
			new ToolInputSchemaContract(
				["environment-name"],
				[
					Field("environment-name", "string", "Registered clio environment name.")
				]),
			EnvelopeOutput(
				"success",
				[
					"success == false"
				],
				Field("success", "boolean", "Whether the tool succeeded."),
				Field("applications", "array", "Installed applications."),
				Field("error", "string", "Human-readable failure message.")
			),
			CommonErrorContract,
			[],
			[],
			[
				Example("List installed applications", new Dictionary<string, object?> {
					["environment-name"] = "local"
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
				["environment-name", "package-name", "operations"],
				[
					Field("environment-name", "string", "Registered clio environment name."),
					Field("package-name", "string", "Target package name."),
					Field("operations", "array", "Ordered schema operations.")
				],
				Validators: [
					new ToolContractValidator(
						"schema-sync-operations-localizations",
						"invalid-localization-map",
						Field: "operations")
				]),
			EnvelopeOutput(
				"success",
				[
					"success == false"
				],
				Field("success", "boolean", "Whether every schema-sync operation succeeded."),
				Field("results", "array", "Per-operation execution results.")
			),
			CommonErrorContract,
			[
				Alias("parameter", "environment-name", "environmentName", "rejected", "Use 'environment-name' instead of 'environmentName'."),
				Alias("parameter", "package-name", "packageName", "rejected", "Use 'package-name' instead of 'packageName'.")
			],
			[],
			[
				Example("Create a lookup and extend the main entity", new Dictionary<string, object?> {
					["environment-name"] = "local",
					["package-name"] = "UsrTaskApp",
					["operations"] = new object[] {
						new Dictionary<string, object?> {
							["type"] = "create-lookup",
							["schema-name"] = "UsrTaskStatus",
							["title-localizations"] = new Dictionary<string, string> {
								["en-US"] = "Task Status"
							}
						},
						new Dictionary<string, object?> {
							["type"] = "update-entity",
							["schema-name"] = "UsrTaskApp",
							["update-operations"] = new object[] {
								new Dictionary<string, object?> {
									["action"] = "add",
									["column-name"] = "UsrStatus",
									["type"] = "Lookup",
									["title-localizations"] = new Dictionary<string, string> {
										["en-US"] = "Status"
									},
									["reference-schema-name"] = "UsrTaskStatus"
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
				["environment-name", "pages"],
				[
					Field("environment-name", "string", "Registered clio environment name."),
					Field("pages", "array", "Page update requests."),
					Field("validate", "boolean", "Run client-side validation before save."),
					Field("verify", "boolean", "Read the page back after save.")
				]),
			EnvelopeOutput(
				"success",
				[
					"success == false"
				],
				Field("success", "boolean", "Whether every page operation succeeded."),
				Field("pages", "array", "Per-page results.")
			),
			CommonErrorContract,
			[],
			[
				Default("validate", "true", "Client-side validation is enabled by default."),
				Default("verify", "false", "Read-back verification is optional and disabled by default.")
			],
			[
				Example("Validate and save a single page", new Dictionary<string, object?> {
					["environment-name"] = "local",
					["pages"] = new object[] {
						new Dictionary<string, object?> {
							["schema-name"] = "UsrTaskApp_FormPage",
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
			"Lists Freedom UI pages for the requested package.",
			new ToolInputSchemaContract(
				[],
				[
					Field("package-name", "string", "Package name to inspect."),
					Field("search-pattern", "string", "Optional case-insensitive schema-name filter."),
					Field("limit", "number", "Optional max result count."),
					Field("environment-name", "string", "Registered clio environment name."),
					Field("uri", "string", "Explicit Creatio URL."),
					Field("login", "string", "Explicit login."),
					Field("password", "string", "Explicit password.")
				],
				AnyOf: [
					new[] { "environment-name" },
					new[] { "uri", "login", "password" }
				]),
			EnvelopeOutput(
				"success",
				[
					"success == false"
				],
				Field("success", "boolean", "Whether the tool succeeded."),
				Field("pages", "array", "Discovered pages."),
				Field("error", "string", "Human-readable failure message.")
			),
			CommonErrorContract,
			[
				Alias("parameter", "package-name", "packageName", "rejected", "Use 'package-name' instead of 'packageName'."),
				Alias("parameter", "search-pattern", "searchPattern", "rejected", "Use 'search-pattern' instead of 'searchPattern'."),
				Alias("parameter", "environment-name", "environmentName", "rejected", "Use 'environment-name' instead of 'environmentName'.")
			],
			[],
			[
				Example("List pages in the target package", new Dictionary<string, object?> {
					["package-name"] = "UsrTaskApp",
					["environment-name"] = "local"
				})
			],
			Flow([PageListTool.ToolName], "Use before page-get when the exact page schema names are not yet known."),
			[],
			[]);
	}

	private static ToolContractDefinition BuildPageGet() {
		return new ToolContractDefinition(
			PageGetTool.ToolName,
			"Reads a Freedom UI page bundle plus the raw editable body.",
			new ToolInputSchemaContract(
				["schema-name"],
				[
					Field("schema-name", "string", "Freedom UI page schema name."),
					Field("environment-name", "string", "Registered clio environment name."),
					Field("uri", "string", "Explicit Creatio URL."),
					Field("login", "string", "Explicit login for uri-based execution."),
					Field("password", "string", "Explicit password for uri-based execution.")
				],
				AnyOf: [
					new[] { "environment-name" },
					new[] { "uri", "login", "password" }
				]),
			EnvelopeOutput(
				"success",
				[
					"success == false"
				],
				Field("success", "boolean", "Whether the tool succeeded."),
				Field("page", "object", "Page metadata."),
				Field("bundle", "object", "Merged page bundle."),
				Field("raw", "object", "Raw body and related data."),
				Field("error", "string", "Human-readable failure message.")
			),
			CommonErrorContract,
			[
				Alias("parameter", "schema-name", "schemaName", "rejected", "Use 'schema-name' instead of 'schemaName'."),
				Alias("parameter", "environment-name", "environmentName", "rejected", "Use 'environment-name' instead of 'environmentName'.")
			],
			[],
			[
				Example("Read an existing FormPage body", new Dictionary<string, object?> {
					["schema-name"] = "UsrTaskApp_FormPage",
					["environment-name"] = "local"
				})
			],
			Flow([PageGetTool.ToolName], "Read before any page-editing workflow."),
			[],
			[]);
	}

	private static ToolContractDefinition BuildCreateLookup() {
		return new ToolContractDefinition(
			CreateLookupTool.CreateLookupToolName,
			"Creates a BaseLookup schema directly in the target package.",
			new ToolInputSchemaContract(
				["environment-name", "package-name", "schema-name", "title-localizations"],
				[
					Field("environment-name", "string", "Registered clio environment name."),
					Field("package-name", "string", "Target package name."),
					Field("schema-name", "string", "Lookup schema name."),
					Field("title-localizations", "object", "Localization map that must include en-US."),
					Field("columns", "array", "Optional custom columns.")
				],
				Validators: [
					new ToolContractValidator("localizations-map", "invalid-localization-map", "title-localizations", Context: "Parameter", Required: true)
				]),
			CommandExecutionOutput(),
			CommonErrorContract,
			[
				Alias("parameter", "environment-name", "environmentName", "rejected", "Use 'environment-name' instead of 'environmentName'."),
				Alias("parameter", "package-name", "packageName", "rejected", "Use 'package-name' instead of 'packageName'."),
				Alias("parameter", "schema-name", "schemaName", "rejected", "Use 'schema-name' instead of 'schemaName'."),
				Alias("parameter", "title-localizations", "title", "rejected", "Use 'title-localizations' instead of legacy scalar 'title'."),
				Alias("parameter", "title-localizations", "caption", "rejected", "Use 'title-localizations' instead of legacy scalar 'caption'.")
			],
			[],
			[
				Example("Create a lookup schema", new Dictionary<string, object?> {
					["environment-name"] = "local",
					["package-name"] = "UsrTaskApp",
					["schema-name"] = "UsrTaskStatus",
					["title-localizations"] = new Dictionary<string, string> {
						["en-US"] = "Task Status"
					}
				})
			],
			Flow(
				[
					SchemaSyncTool.ToolName
				],
				"Prefer schema-sync for ordered multi-step entity work."),
			[],
			[
				new ToolDeprecation(
					"Prefer schema-sync as the canonical entity mutation path. Keep create-lookup for explicit fallback or isolated operations.",
					[
						SchemaSyncTool.ToolName
					])
			]);
	}

	private static ToolContractDefinition BuildCreateEntity() {
		return new ToolContractDefinition(
			CreateEntitySchemaTool.CreateEntitySchemaToolName,
			"Creates an entity schema directly in the target package.",
			new ToolInputSchemaContract(
				["environment-name", "package-name", "schema-name", "title-localizations"],
				[
					Field("environment-name", "string", "Registered clio environment name."),
					Field("package-name", "string", "Target package name."),
					Field("schema-name", "string", "Entity schema name."),
					Field("title-localizations", "object", "Localization map that must include en-US."),
					Field("columns", "array", "Optional initial columns."),
					Field("parent-schema-name", "string", "Optional parent schema name."),
					Field("extend-parent", "boolean", "Optional replacement-schema flag.")
				],
				Validators: [
					new ToolContractValidator("localizations-map", "invalid-localization-map", "title-localizations", Context: "Parameter", Required: true)
				]),
			CommandExecutionOutput(),
			CommonErrorContract,
			[
				Alias("parameter", "environment-name", "environmentName", "rejected", "Use 'environment-name' instead of 'environmentName'."),
				Alias("parameter", "package-name", "packageName", "rejected", "Use 'package-name' instead of 'packageName'."),
				Alias("parameter", "schema-name", "schemaName", "rejected", "Use 'schema-name' instead of 'schemaName'."),
				Alias("parameter", "parent-schema-name", "parentSchemaName", "rejected", "Use 'parent-schema-name' instead of 'parentSchemaName'."),
				Alias("parameter", "extend-parent", "extendParent", "rejected", "Use 'extend-parent' instead of 'extendParent'."),
				Alias("parameter", "title-localizations", "title", "rejected", "Use 'title-localizations' instead of legacy scalar 'title'."),
				Alias("parameter", "title-localizations", "caption", "rejected", "Use 'title-localizations' instead of legacy scalar 'caption'.")
			],
			[],
			[
				Example("Create an additional business entity", new Dictionary<string, object?> {
					["environment-name"] = "local",
					["package-name"] = "UsrTaskApp",
					["schema-name"] = "UsrTaskComment",
					["title-localizations"] = new Dictionary<string, string> {
						["en-US"] = "Task Comment"
					},
					["parent-schema-name"] = "BaseEntity"
				})
			],
			Flow(
				[
					SchemaSyncTool.ToolName
				],
				"Prefer schema-sync for ordered multi-step entity work."),
			[],
			[
				new ToolDeprecation(
					"Prefer schema-sync as the canonical entity mutation path. Keep create-entity-schema for explicit fallback or isolated operations.",
					[
						SchemaSyncTool.ToolName
					])
			]);
	}

	private static ToolContractDefinition BuildUpdateEntity() {
		return new ToolContractDefinition(
			UpdateEntitySchemaTool.UpdateEntitySchemaToolName,
			"Applies explicit add, modify, or remove column operations to an entity schema.",
			new ToolInputSchemaContract(
				["environment-name", "package-name", "schema-name", "operations"],
				[
					Field("environment-name", "string", "Registered clio environment name."),
					Field("package-name", "string", "Target package name."),
					Field("schema-name", "string", "Entity schema name."),
					Field("operations", "array", "Explicit column mutation operations.")
				],
				Validators: [
					new ToolContractValidator("update-operations-localizations", "invalid-localization-map", "operations")
				]),
			CommandExecutionOutput(),
			CommonErrorContract,
			[
				Alias("parameter", "environment-name", "environmentName", "rejected", "Use 'environment-name' instead of 'environmentName'."),
				Alias("parameter", "package-name", "packageName", "rejected", "Use 'package-name' instead of 'packageName'."),
				Alias("parameter", "schema-name", "schemaName", "rejected", "Use 'schema-name' instead of 'schemaName'.")
			],
			[],
			[
				Example("Add a lookup column to an existing entity", new Dictionary<string, object?> {
					["environment-name"] = "local",
					["package-name"] = "UsrTaskApp",
					["schema-name"] = "UsrTaskApp",
					["operations"] = new object[] {
						new Dictionary<string, object?> {
							["action"] = "add",
							["column-name"] = "UsrStatus",
							["type"] = "Lookup",
							["title-localizations"] = new Dictionary<string, string> {
								["en-US"] = "Status"
							},
							["reference-schema-name"] = "UsrTaskStatus"
						}
					}
				})
			],
			Flow(
				[
					SchemaSyncTool.ToolName
				],
				"Prefer schema-sync for ordered multi-step entity work."),
			[],
			[
				new ToolDeprecation(
					"Prefer schema-sync as the canonical entity mutation path. Keep update-entity-schema for explicit fallback or isolated operations.",
					[
						SchemaSyncTool.ToolName
					])
			]);
	}

	private static ToolContractDefinition BuildCreateDataBindingDb() {
		return new ToolContractDefinition(
			CreateDataBindingDbTool.CreateDataBindingDbToolName,
			"Creates or updates a DB-first package data binding and optionally applies rows immediately.",
			new ToolInputSchemaContract(
				["environment-name", "package-name", "schema-name"],
				[
					Field("environment-name", "string", "Registered clio environment name."),
					Field("package-name", "string", "Target package name."),
					Field("schema-name", "string", "Entity schema name for the binding."),
					Field("binding-name", "string", "Optional binding name; defaults to the schema name."),
					Field("rows", "string", "Optional JSON array of rows.")
				]),
			CommandExecutionOutput(),
			CommonErrorContract,
			[
				Alias("parameter", "environment-name", "environmentName", "rejected", "Use 'environment-name' instead of 'environmentName'."),
				Alias("parameter", "package-name", "packageName", "rejected", "Use 'package-name' instead of 'packageName'."),
				Alias("parameter", "schema-name", "schemaName", "rejected", "Use 'schema-name' instead of 'schemaName'.")
			],
			[],
			[
				Example("Create a default lookup binding with rows", new Dictionary<string, object?> {
					["environment-name"] = "local",
					["package-name"] = "UsrTaskApp",
					["schema-name"] = "UsrTaskStatus",
					["rows"] = "[{\"values\":{\"Name\":\"New\"}}]"
				})
			],
			Flow(
				[
					SchemaSyncTool.ToolName
				],
				"Prefer inline seed-rows inside schema-sync when the flow can stay batched."),
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
				["environment-name", "package-name", "binding-name", "values"],
				[
					Field("environment-name", "string", "Registered clio environment name."),
					Field("package-name", "string", "Target package name."),
					Field("binding-name", "string", "Binding name."),
					Field("values", "string", "JSON object keyed by column name.")
				]),
			CommandExecutionOutput(),
			new ToolErrorContract([
				..CommonErrorContract.Codes,
				new ToolErrorCodeContract("binding-not-found",
					"The specified binding does not exist in the remote environment. " +
					"Create it first with create-data-binding-db.")
			]),
			[
				Alias("parameter", "environment-name", "environmentName", "rejected", "Use 'environment-name' instead of 'environmentName'."),
				Alias("parameter", "package-name", "packageName", "rejected", "Use 'package-name' instead of 'packageName'."),
				Alias("parameter", "binding-name", "bindingName", "rejected", "Use 'binding-name' instead of 'bindingName'.")
			],
			[],
			[
				Example("Upsert one binding row", new Dictionary<string, object?> {
					["environment-name"] = "local",
					["package-name"] = "UsrTaskApp",
					["binding-name"] = "UsrTaskStatus",
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
				["environment-name", "package-name", "binding-name", "key-value"],
				[
					Field("environment-name", "string", "Registered clio environment name."),
					Field("package-name", "string", "Target package name."),
					Field("binding-name", "string", "Binding name."),
					Field("key-value", "string", "Primary-key value of the row to remove.")
				]),
			CommandExecutionOutput(),
			CommonErrorContract,
			[
				Alias("parameter", "environment-name", "environmentName", "rejected", "Use 'environment-name' instead of 'environmentName'."),
				Alias("parameter", "package-name", "packageName", "rejected", "Use 'package-name' instead of 'packageName'."),
				Alias("parameter", "binding-name", "bindingName", "rejected", "Use 'binding-name' instead of 'bindingName'."),
				Alias("parameter", "key-value", "keyValue", "rejected", "Use 'key-value' instead of 'keyValue'.")
			],
			[],
			[
				Example("Remove one binding row", new Dictionary<string, object?> {
					["environment-name"] = "local",
					["package-name"] = "UsrTaskApp",
					["binding-name"] = "UsrTaskStatus",
					["key-value"] = "00000000-0000-0000-0000-000000000001"
				})
			],
			Flow([RemoveDataBindingRowDbTool.RemoveDataBindingRowDbToolName], "Standalone DB-first binding maintenance."),
			[],
			[]);
	}

	private static ToolContractDefinition BuildGetEntitySchemaProperties() {
		return new ToolContractDefinition(
			GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName,
			"Returns a structured summary of entity schema metadata.",
			new ToolInputSchemaContract(
				["environment-name", "package-name", "schema-name"],
				[
					Field("environment-name", "string", "Registered clio environment name."),
					Field("package-name", "string", "Target package name."),
					Field("schema-name", "string", "Entity schema name.")
				]),
			new ToolOutputContract(
				"structured-result",
				null,
				[
					"structured result cannot be parsed"
				],
				[
					Field("name", "string", "Schema name."),
					Field("title", "string", "Schema title."),
					Field("columns", "array", "Column metadata.")
				]),
			CommonErrorContract,
			[
				Alias("parameter", "environment-name", "environmentName", "rejected", "Use 'environment-name' instead of 'environmentName'."),
				Alias("parameter", "package-name", "packageName", "rejected", "Use 'package-name' instead of 'packageName'."),
				Alias("parameter", "schema-name", "schemaName", "rejected", "Use 'schema-name' instead of 'schemaName'.")
			],
			[],
			[
				Example("Read deployed schema properties", new Dictionary<string, object?> {
					["environment-name"] = "local",
					["package-name"] = "UsrTaskApp",
					["schema-name"] = "UsrTaskApp"
				})
			],
			Flow([GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName], "Use for machine-readable schema verification after mutations."),
			[],
			[]);
	}

	private static ToolContractDefinition BuildGetEntitySchemaColumnProperties() {
		return new ToolContractDefinition(
			GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName,
			"Returns detailed metadata for one deployed entity schema column.",
			new ToolInputSchemaContract(
				["environment-name", "package-name", "schema-name", "column-name"],
				[
					Field("environment-name", "string", "Registered clio environment name."),
					Field("package-name", "string", "Target package name."),
					Field("schema-name", "string", "Entity schema name."),
					Field("column-name", "string", "Column name.")
				]),
			new ToolOutputContract(
				"structured-result",
				null,
				[
					"structured result cannot be parsed"
				],
				[
					Field("name", "string", "Column name."),
					Field("data-value-type", "string", "Column type."),
					Field("source", "string", "Column source.")
				]),
			CommonErrorContract,
			[
				Alias("parameter", "environment-name", "environmentName", "rejected", "Use 'environment-name' instead of 'environmentName'."),
				Alias("parameter", "package-name", "packageName", "rejected", "Use 'package-name' instead of 'packageName'."),
				Alias("parameter", "schema-name", "schemaName", "rejected", "Use 'schema-name' instead of 'schemaName'."),
				Alias("parameter", "column-name", "columnName", "rejected", "Use 'column-name' instead of 'columnName'.")
			],
			[],
			[
				Example("Read one deployed column", new Dictionary<string, object?> {
					["environment-name"] = "local",
					["package-name"] = "UsrTaskApp",
					["schema-name"] = "UsrTaskApp",
					["column-name"] = "UsrStatus"
				})
			],
			Flow([GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName], "Use for machine-readable verification of one deployed column."),
			[],
			[]);
	}

	private static ToolContractDefinition BuildModifyEntitySchemaColumn() {
		return new ToolContractDefinition(
			ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName,
			"Adds, modifies, or removes a single entity schema column directly.",
			new ToolInputSchemaContract(
				["environment-name", "package-name", "schema-name", "action", "column-name"],
				[
					Field("environment-name", "string", "Registered clio environment name."),
					Field("package-name", "string", "Target package name."),
					Field("schema-name", "string", "Entity schema name."),
					Field("action", "string", "Column action: add, modify, or remove."),
					Field("column-name", "string", "Column name."),
					Field("type", "string", "Optional column data type."),
					Field("title-localizations", "object", "Optional localization map."),
					Field("description-localizations", "object", "Optional localization map."),
					Field("reference-schema-name", "string", "Optional lookup target."),
					Field("is-required", "boolean", "Optional required flag."),
					Field("default-value-source", "string", "Optional default source."),
					Field("default-value", "string", "Optional default value.")
				]),
			CommandExecutionOutput(),
			CommonErrorContract,
			[
				Alias("parameter", "environment-name", "environmentName", "rejected", "Use 'environment-name' instead of 'environmentName'."),
				Alias("parameter", "package-name", "packageName", "rejected", "Use 'package-name' instead of 'packageName'."),
				Alias("parameter", "schema-name", "schemaName", "rejected", "Use 'schema-name' instead of 'schemaName'."),
				Alias("parameter", "column-name", "columnName", "rejected", "Use 'column-name' instead of 'columnName'."),
				Alias("parameter", "reference-schema-name", "referenceSchemaName", "rejected", "Use 'reference-schema-name' instead of 'referenceSchemaName'."),
				Alias("parameter", "default-value", "defaultValue", "rejected", "Use 'default-value' instead of 'defaultValue'."),
				Alias("parameter", "default-value-source", "defaultValueSource", "rejected", "Use 'default-value-source' instead of 'defaultValueSource'."),
				Alias("parameter", "title-localizations", "title", "rejected", "Use 'title-localizations' instead of legacy scalar 'title'."),
				Alias("parameter", "title-localizations", "caption", "rejected", "Use 'title-localizations' instead of legacy scalar 'caption'."),
				Alias("parameter", "description-localizations", "description", "rejected", "Use 'description-localizations' instead of legacy scalar 'description'.")
			],
			[],
			[
				Example("Add one required text column", new Dictionary<string, object?> {
					["environment-name"] = "local",
					["package-name"] = "UsrTaskApp",
					["schema-name"] = "UsrTaskApp",
					["action"] = "add",
					["column-name"] = "UsrShortCode",
					["type"] = "Text",
					["title-localizations"] = new Dictionary<string, string> {
						["en-US"] = "Short Code"
					},
					["is-required"] = true
				})
			],
			Flow(
				[
					SchemaSyncTool.ToolName
				],
				"Prefer schema-sync for ordered multi-step entity work."),
			[],
			[
				new ToolDeprecation(
					"Prefer schema-sync as the canonical entity mutation path. Keep modify-entity-schema-column for explicit fallback or isolated operations.",
					[
						SchemaSyncTool.ToolName
					])
			]);
	}

	private static ToolContractDefinition BuildComponentInfo() {
		return new ToolContractDefinition(
			ComponentInfoTool.ToolName,
			"Returns grouped Freedom UI component summaries or a full component contract for one component type.",
			new ToolInputSchemaContract(
				[],
				[
					Field("component-type", "string", "Optional component type. Omit or use list to return the grouped catalog."),
					Field("search", "string", "Optional keyword filter for list mode.")
				]),
			EnvelopeOutput(
				"success",
				[
					"success == false"
				],
				Field("success", "boolean", "Whether the tool succeeded."),
				Field("mode", "string", "detail or list."),
				Field("count", "number", "Number of matching components."),
				Field("groups", "array", "Grouped list-mode results."),
				Field("componentType", "string", "Component type for detail mode."),
				Field("error", "string", "Human-readable failure message.")
			),
			CommonErrorContract,
			[
				Alias("parameter", "component-type", "componentType", "rejected", "Use 'component-type' instead of 'componentType'.")
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
			"Saves a full Freedom UI page body for one page.",
			new ToolInputSchemaContract(
				["schema-name", "body"],
				[
					Field("schema-name", "string", "Freedom UI page schema name."),
					Field("body", "string", "Full page body with all marker pairs."),
					Field("dry-run", "boolean", "Validate without saving."),
					Field("resources", "string", "Optional JSON object of resource strings."),
					Field("environment-name", "string", "Registered clio environment name."),
					Field("uri", "string", "Explicit Creatio URL."),
					Field("login", "string", "Explicit login."),
					Field("password", "string", "Explicit password.")
				],
				AnyOf: [
					new[] { "environment-name" },
					new[] { "uri", "login", "password" }
				]),
			EnvelopeOutput(
				"success",
				[
					"success == false"
				],
				Field("success", "boolean", "Whether the tool succeeded."),
				Field("schemaName", "string", "Page schema name."),
				Field("bodyLength", "number", "Saved body length."),
				Field("dryRun", "boolean", "Whether the call ran in validation mode."),
				Field("resourcesRegistered", "number", "Number of registered resources."),
				Field("error", "string", "Human-readable failure message.")
			),
			CommonErrorContract,
			[
				Alias("parameter", "schema-name", "schemaName", "rejected", "Use 'schema-name' instead of 'schemaName'."),
				Alias("parameter", "environment-name", "environmentName", "rejected", "Use 'environment-name' instead of 'environmentName'."),
				Alias("parameter", "dry-run", "dryRun", "rejected", "Use 'dry-run' instead of 'dryRun'.")
			],
			[],
			[
				Example("Dry-run validate one page body", new Dictionary<string, object?> {
					["schema-name"] = "UsrTaskApp_FormPage",
					["body"] = "define(...)",
					["dry-run"] = true,
					["environment-name"] = "local"
				})
			],
			Flow(
				[
					PageSyncTool.ToolName
				],
				"Prefer page-sync as the canonical page write path."),
			[
				Flow(
					[
						PageGetTool.ToolName,
						PageUpdateTool.ToolName,
						PageUpdateTool.ToolName,
						PageGetTool.ToolName
					],
					"Fallback when the flow must dry-run and save one page explicitly.")
			],
			[
				new ToolDeprecation(
					"Prefer page-sync as the canonical page write path. Keep page-update for explicit fallback or targeted single-page saves.",
					[
						PageSyncTool.ToolName
					])
			]);
	}

	private static ToolContractDefinition BuildApplicationDelete() {
		return new ToolContractDefinition(
			ApplicationDeleteTool.ToolName,
			"Deletes an installed application by name or code.",
			new ToolInputSchemaContract(
				["app-name"],
				[
					Field("app-name", "string", "Application name or code."),
					Field("environment-name", "string", "Registered clio environment name."),
					Field("uri", "string", "Explicit Creatio URL."),
					Field("login", "string", "Explicit login."),
					Field("password", "string", "Explicit password.")
				],
				AnyOf: [
					new[] { "environment-name" },
					new[] { "uri", "login", "password" }
				]),
			EnvelopeOutput(
				"success",
				[
					"success == false"
				],
				Field("success", "boolean", "Whether the tool succeeded."),
				Field("error", "string", "Human-readable failure message.")
			),
			CommonErrorContract,
			[],
			[],
			[
				Example("Delete an application by code", new Dictionary<string, object?> {
					["app-name"] = "UsrTaskApp",
					["environment-name"] = "local"
				})
			],
			Flow([ApplicationDeleteTool.ToolName], "Standalone destructive application lifecycle operation."),
			[],
			[]);
	}

	private static ToolContractField Field(string name, string type, string description) {
		return new ToolContractField(name, type, description);
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
				Field("exit-code", "number", "Process exit code."),
				Field("execution-log-messages", "array", "Structured log messages."),
				Field("log-file-path", "string", "Optional operation log path.")
			]);
	}
}
