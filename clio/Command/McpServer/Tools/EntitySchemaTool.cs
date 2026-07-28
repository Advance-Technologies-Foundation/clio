using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for remote entity schema creation.
/// </summary>
public sealed class CreateEntitySchemaTool(
	CreateEntitySchemaCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver,
	ISchemaEnrichmentService? enrichmentService = null)
	: BaseTool<CreateEntitySchemaOptions>(command, logger, commandResolver) {

	internal const string CreateEntitySchemaToolName = "create-entity-schema";

	/// <summary>
	/// Creates a remote entity schema in a package on the requested Creatio environment.
	/// </summary>
	[McpServerTool(Name = CreateEntitySchemaToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("""
				 Creates a remote entity schema in an existing Creatio package through EntitySchemaDesignerService.

				 Use this when the schema should be created directly on the target environment instead of generating
				 local source files. The package must already exist on the target environment.
				 Set `is-virtual` to true only when the schema must not have a physical database table; it defaults to false.
				 Before setting `is-virtual` to true, call get-guidance with name virtual-entities and follow its
				 schema-before-executor, bounded-provider, authorization, and version-gated write rules.

				 The tool applies the DB structure and publishes the schema automatically, so the new entity is
				 immediately usable as a Lookup reference in sys-settings and lookup pickers — no compile needed.
				 Publishing also requests an OData entities rebuild, so the entity becomes reachable over OData
				 (/0/odata/<Entity>) without a compile. That rebuild is asynchronous (~1-2 min): a 404 from an
				 odata-* tool right after creation is the expected async gap — wait briefly and retry, do not compile.

				 Entity business rules (conditional editability/required/values) are separate artifacts — call get-guidance with name business-rules to learn more. For the schema-design workflow call get-guidance with name app-modeling.
				 """)]
	public async Task<CommandExecutionResult> CreateEntitySchema(
		[Description("Parameters: environment-name, package-name, schema-name, title-localizations (all required); columns, parent-schema-name (optional, defaults to BaseEntity unless extend-parent is true), extend-parent (optional, requires parent-schema-name when true)")] [Required] CreateEntitySchemaArgs args
	) {
		ApplicationDataForgeResult? dataForge = enrichmentService is not null
			? enrichmentService.Enrich(
				args.EnvironmentName,
				BuildCandidateTerms(args.SchemaName, args.TitleLocalizations))
			: null;
		try {
			CreateEntitySchemaOptions options = CreateOptions(
				args, args.ParentSchemaName, args.ExtendParent, args.IsVirtual);
			CommandExecutionResult result = InternalExecute<CreateEntitySchemaCommand>(options);
			return result with { DataForge = dataForge };
		} catch (Exception exception) {
			return new CommandExecutionResult(1, [new ErrorMessage(SensitiveErrorTextRedactor.Redact(exception.Message))], null, dataForge);
		}
	}

	private static IReadOnlyList<string> BuildCandidateTerms(
		string? schemaName,
		IReadOnlyDictionary<string, string>? titleLocalizations) {
		return new[] { schemaName }
			.Concat(titleLocalizations?.Values ?? [])
			.Where(term => !string.IsNullOrWhiteSpace(term))
			.Select(term => term!.Trim())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	internal static CreateEntitySchemaOptions CreateOptions(
		EntitySchemaCreateArgsBase args,
		string? parentSchemaName,
		bool extendParent,
		bool isVirtual = false) {
		string context = $"Schema '{args.SchemaName}'";
		IReadOnlyDictionary<string, string> titleLocalizations = EntitySchemaLocalizationContract.RequireTitleLocalizations(
			args.TitleLocalizations,
			args.LegacyTitle,
			context);
		TitleLocalizationNormalizationResult titleNormalization =
			EntitySchemaDesignerSupport.NormalizeTitleLocalizations(
				titleLocalizations,
				null,
				"title-localizations");
		return new CreateEntitySchemaOptions {
			Package = args.PackageName,
			SchemaName = args.SchemaName,
			Title = titleNormalization.EffectiveTitle
				?? EntitySchemaLocalizationContract.GetDefaultTitle(titleLocalizations, context),
			TitleLocalizations = titleNormalization.Localizations ?? titleLocalizations,
			ParentSchemaName = (!extendParent && string.IsNullOrWhiteSpace(parentSchemaName)) ? "BaseEntity" : parentSchemaName,
			ExtendParent = extendParent,
			IsVirtual = isVirtual,
			Columns = SerializeColumns(args.Columns, context),
			Environment = args.EnvironmentName,
			CaptionCulture = args.CaptionCulture
		};
	}

	internal static IEnumerable<string>? SerializeColumns(
		IEnumerable<CreateEntitySchemaColumnArgs>? columns,
		string schemaContext) {
		return columns?
			.Select((column, index) => SerializeColumn(column, $"{schemaContext} column #{index + 1}"))
			.ToList();
	}

	private static string SerializeColumn(CreateEntitySchemaColumnArgs column, string context) {
		IReadOnlyDictionary<string, string> titleLocalizations = EntitySchemaLocalizationContract.RequireTitleLocalizations(
			column.TitleLocalizations,
			column.LegacyTitle,
			column.LegacyCaption,
			column.ResolveName(),
			context);
		string? resolvedReferenceSchemaName = column.ResolveReferenceSchemaName();
		return JsonSerializer.Serialize(new Dictionary<string, object?> {
			["name"] = column.Name?.Trim(),
			["type"] = column.ResolveType()?.Trim(),
			["title-localizations"] = titleLocalizations,
			["reference-schema-name"] = string.IsNullOrWhiteSpace(resolvedReferenceSchemaName)
				? null
				: resolvedReferenceSchemaName.Trim(),
			["required"] = column.ResolveRequired(),
		    ["default-value-source"] = column.DefaultValueSource,
		    ["default-value"] = column.DefaultValue,
			["default-value-config"] = column.DefaultValueConfig,
			["masked"] = column.Masked
	});
}
}

/// <summary>
/// MCP tool surface for remote lookup schema creation.
/// </summary>
public sealed class CreateLookupTool : BaseTool<CreateEntitySchemaOptions> {
	private readonly ILogger _logger;
	private readonly IToolCommandResolver _commandResolver;
	private readonly ISchemaEnrichmentService? _enrichmentService;

	internal const string CreateLookupToolName = "create-lookup";
	private const string BaseLookupParentSchemaName = "BaseLookup";

	public CreateLookupTool(
		CreateEntitySchemaCommand command,
		ILogger logger,
		IToolCommandResolver commandResolver,
		ISchemaEnrichmentService? enrichmentService = null)
		: base(command, logger, commandResolver) {
		_logger = logger;
		_commandResolver = commandResolver;
		_enrichmentService = enrichmentService;
	}

	/// <summary>
	/// Creates a remote lookup schema in a package on the requested Creatio environment.
	/// </summary>
	[McpServerTool(Name = CreateLookupToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("""
				 Creates a remote lookup schema in an existing Creatio package through EntitySchemaDesignerService.

				 The schema always inherits from BaseLookup. Use this when the caller explicitly requested a lookup
				 entity instead of a generic entity schema. BaseLookup already provides Name and Description, so do
				 not send them as custom columns. Entity business rules are separate — call get-guidance with name business-rules.

				 The tool applies the DB structure and publishes the schema automatically, so the new lookup is
				 immediately usable as a Lookup reference in sys-settings and lookup pickers — no compile needed.
				 Publishing also requests an OData entities rebuild, so the lookup becomes reachable over OData
				 (/0/odata/<Entity>) without a compile. That rebuild is asynchronous (~1-2 min): a 404 from an
				 odata-* tool right after creation is the expected async gap — wait briefly and retry, do not compile.
				 """)]
	public async Task<CommandExecutionResult> CreateLookup(
		[Description("Parameters: environment-name, package-name, schema-name, title-localizations (all required); columns (optional)")] [Required] CreateLookupArgs args
	) {
		IReadOnlyList<string> lookupHints = new[] { args.SchemaName }
			.Concat((IEnumerable<string>?)args.TitleLocalizations?.Values ?? [])
			.Where(term => !string.IsNullOrWhiteSpace(term))
			.Select(term => term!.Trim())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		ApplicationDataForgeResult? dataForge = _enrichmentService is not null
			? _enrichmentService.Enrich(args.EnvironmentName, lookupHints, lookupHints)
			: null;
		try {
			ModelingGuardrails.EnsureLookupColumnsDoNotShadowInheritedBaseLookupColumns(args.Columns);
			CreateEntitySchemaOptions options = CreateEntitySchemaTool.CreateOptions(
				args,
				BaseLookupParentSchemaName,
				extendParent: false);
			string lookupTitle = EntitySchemaLocalizationContract.GetDefaultTitle(
				options.TitleLocalizations!,
				$"Lookup '{args.SchemaName}'");
			int exitCode = -1;
			return ExecuteUnderTenantLock(options, () => {
				// FR-11 (review): this tool self-captures and builds its own result inside the lock, so it
				// bypasses RunCommandUnderHeldLock's redaction — redact the snapshot here on a passthrough
				// request (no-op off passthrough). Same key the tenant lock resolves under.
				string tenantKey = ResolveTenantLockKey(options);
				bool previousPreserveMessages = _logger.PreserveMessages;
				_logger.PreserveMessages = true;
				try {
					CreateEntitySchemaCommand resolvedCommand = ResolveCommand<CreateEntitySchemaCommand>(options);
					exitCode = resolvedCommand.Execute(options);
					if (exitCode == 0) {
						ILookupRegistrationService registrationService =
							_commandResolver.Resolve<ILookupRegistrationService>(options);
						registrationService.EnsureLookupRegistration(args.PackageName, args.SchemaName, lookupTitle);
					}

					CommandExecutionResult returnResult = new(
						exitCode,
						[.. McpPassthroughRedaction.SanitizeAndRedact([.. _logger.FlushAndSnapshotMessages(clearMessages: true)], tenantKey)],
						null,
						dataForge);
					return returnResult;
				}
				catch (Exception exception) {
					List<LogMessage> logMessages = [.. McpPassthroughRedaction.SanitizeAndRedact([.. _logger.FlushAndSnapshotMessages(clearMessages: true)], tenantKey), new ErrorMessage(SensitiveErrorTextRedactor.Redact(exception.Message))];
					CommandExecutionResult returnResult = new(
						exitCode > 0 ? exitCode : 1,
						logMessages,
						null,
						dataForge);
					return returnResult;
				}
				finally {
					_logger.PreserveMessages = previousPreserveMessages;
				}
			});
		} catch (Exception exception) {
			return new CommandExecutionResult(1, [new ErrorMessage(SensitiveErrorTextRedactor.Redact(exception.Message))], null, dataForge);
		}
	}
}

/// <summary>
/// MCP tool surface for batch remote entity schema column mutations.
/// </summary>
public sealed class UpdateEntitySchemaTool(
	UpdateEntitySchemaCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver,
	ISchemaEnrichmentService? enrichmentService = null)
	: BaseTool<UpdateEntitySchemaOptions>(command, logger, commandResolver) {

	internal const string UpdateEntitySchemaToolName = "update-entity-schema";

	/// <summary>
	/// Applies a batch of add/modify/remove column operations to a remote entity schema.
	/// </summary>
	[McpServerTool(Name = UpdateEntitySchemaToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Applies a batch of add, modify, and remove column operations to a remote Creatio entity schema. " +
		"The batch is published and the OData entities are rebuilt automatically, so changed columns become reachable over OData (/0/odata/<Entity>) without a compile. That rebuild is asynchronous (~1-2 min): a 404 (or \"The request is invalid\") from an odata-* tool right after a change is the expected async gap — wait briefly and retry, do not compile. " +
		"An INHERITED column can have only its caption/description overridden (title-localizations / description-localizations); its name, type, and flags stay read-only. " +
		"Entity business rules (conditional editability/required/values) are separate artifacts — call get-guidance with name business-rules to learn more. For the schema-design workflow call get-guidance with name app-modeling.")]
	public async Task<CommandExecutionResult> UpdateEntitySchema(
		[Description("Parameters: environment-name, package-name, schema-name, operations (all required)")] [Required] UpdateEntitySchemaArgs args) {
		ApplicationDataForgeResult? dataForge = null;
		try {
			if (enrichmentService is not null) {
				dataForge = enrichmentService.Enrich(
					args.EnvironmentName,
					BuildCandidateTerms(args),
					BuildLookupHints(args));
			}
		} catch {
			// DataForge enrichment is best-effort; failure must not block the mutation.
		}
		try {
			UpdateEntitySchemaOptions options = new() {
				Environment = args.EnvironmentName,
				Package = args.PackageName,
				SchemaName = args.SchemaName,
				Operations = SerializeOperations(args.Operations, args.SchemaName)
			};
			CommandExecutionResult result = InternalExecute<UpdateEntitySchemaCommand>(options);
			return result with {
				DataForge = dataForge,
				Note = result.ExitCode == 0 ? CommandExecutionResult.CompileNotRequiredNote : result.Note
			};
		} catch (Exception exception) {
			return new CommandExecutionResult(1, [new ErrorMessage(SensitiveErrorTextRedactor.Redact(exception.Message))], null, dataForge);
		}
	}

	private static IReadOnlyList<string> BuildCandidateTerms(UpdateEntitySchemaArgs args) {
		return new[] { args.SchemaName }
			.Concat(args.Operations
				.Where(op => string.Equals(op.Action, "add", StringComparison.OrdinalIgnoreCase)
					&& !string.IsNullOrWhiteSpace(op.ResolveColumnName()))
				.Select(op => op.ResolveColumnName()!.Trim()))
			.Where(term => !string.IsNullOrWhiteSpace(term))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static IReadOnlyList<string> BuildLookupHints(UpdateEntitySchemaArgs args) {
		return args.Operations
			.Where(op => string.Equals(op.Action, "add", StringComparison.OrdinalIgnoreCase)
				&& !string.IsNullOrWhiteSpace(op.ResolveReferenceSchemaName()))
			.Select(op => op.ResolveReferenceSchemaName()!.Trim())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	internal static List<string> SerializeOperations(
		IEnumerable<UpdateEntitySchemaOperationArgs> operations,
		string schemaName) {
		return operations
			.Select((operation, index) =>
				JsonSerializer.Serialize(BuildOperationPayload(operation,
					$"Schema '{schemaName}' operation #{index + 1}")))
			.ToList();
	}

	internal static Dictionary<string, object?> BuildOperationPayload(
		UpdateEntitySchemaOperationArgs operation,
		string context) {
		IReadOnlyDictionary<string, string>? titleLocalizations =
			EntitySchemaLocalizationContract.NormalizeMutationTitleLocalizations(
				operation.Action,
				operation.TitleLocalizations,
				operation.LegacyTitle,
				operation.LegacyCaption,
				operation.ResolveColumnName(),
				context);
		IReadOnlyDictionary<string, string>? descriptionLocalizations =
			EntitySchemaLocalizationContract.NormalizeMutationDescriptionLocalizations(
				operation.Action,
				operation.DescriptionLocalizations,
				operation.LegacyDescription,
				context);
		return new Dictionary<string, object?> {
			["action"] = operation.Action,
			["column-name"] = operation.ResolveColumnName(),
			["new-name"] = operation.NewName,
			["type"] = operation.ResolveType(),
			["title-localizations"] = titleLocalizations,
			["description-localizations"] = descriptionLocalizations,
			["reference-schema-name"] = operation.ResolveReferenceSchemaName(),
			["required"] = operation.ResolveRequired(),
			["indexed"] = operation.Indexed,
			["cloneable"] = operation.Cloneable,
			["track-changes"] = operation.TrackChanges,
			["default-value"] = operation.DefaultValue,
			["default-value-source"] = operation.DefaultValueSource,
			["default-value-config"] = operation.DefaultValueConfig,
			["multiline-text"] = operation.MultilineText,
			["localizable-text"] = operation.LocalizableText,
			["accent-insensitive"] = operation.AccentInsensitive,
			["masked"] = operation.Masked,
			["format-validated"] = operation.FormatValidated,
			["use-seconds"] = operation.UseSeconds,
			["simple-lookup"] = operation.SimpleLookup,
			["cascade"] = operation.Cascade,
			["do-not-control-integrity"] = operation.DoNotControlIntegrity,
			["usage-type"] = operation.UsageType
		};
	}
}

/// <summary>
/// MCP tool surface for reading structured remote entity schema properties.
/// </summary>
public sealed class GetEntitySchemaPropertiesTool(
	GetEntitySchemaPropertiesCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<GetEntitySchemaPropertiesOptions>(command, logger, commandResolver) {

	internal const string GetEntitySchemaPropertiesToolName = "get-entity-schema-properties";

	/// <summary>
	/// Returns structured properties for a remote entity schema.
	/// </summary>
	[McpServerTool(Name = GetEntitySchemaPropertiesToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("Returns structured properties for a remote Creatio entity schema. "
		+ "Omit package-name for the MERGED/EFFECTIVE view (columns from all packages) — use this for column discovery; "
		+ "an empty column list from a single-package read does NOT prove a column is absent. "
		+ "Supply package-name to inspect one package layer and to read schema-level fields that the merged view returns as null "
		+ "(parent-schema-name, indexes-count, ssp-available, use-record-deactivation, use-deny-record-rights, use-live-editing). "
		+ "The result always includes virtual so callers can verify whether the schema has a physical database table.")]
	public EntitySchemaPropertiesInfo GetEntitySchemaProperties(
		[Description("environment-name, schema-name (required); package-name (optional — omit for the merged all-packages view)")] [Required] GetEntitySchemaPropertiesArgs args) {
		GetEntitySchemaPropertiesOptions options = new() {
			Environment = args.EnvironmentName,
			Package = args.PackageName,
			SchemaName = args.SchemaName
		};

		GetEntitySchemaPropertiesCommand resolvedCommand = ResolveCommand<GetEntitySchemaPropertiesCommand>(options);
		return resolvedCommand.GetSchemaProperties(options);
	}
}

/// <summary>
/// MCP tool surface for setting schema-level properties on a remote entity schema.
/// </summary>
public sealed class SetEntitySchemaPropertiesTool(
	SetEntitySchemaPropertiesCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<SetEntitySchemaPropertiesOptions>(command, logger, commandResolver) {

	internal const string SetEntitySchemaPropertiesToolName = "set-entity-schema-properties";

	/// <summary>
	/// Sets schema-level properties (currently the primary-display column) on a remote entity schema.
	/// </summary>
	[McpServerTool(Name = SetEntitySchemaPropertiesToolName, ReadOnly = false, Destructive = true,
		Idempotent = true, OpenWorld = false)]
	[Description("Sets schema-level properties on a remote Creatio entity schema. "
		+ "Currently supports primary-display-column: the column (own or inherited, resolved by name) shown as the "
		+ "record's display value in lookups and links. The change is saved and published (OData rebuilt) like the "
		+ "other entity-schema tools, then verified by reading it back — a target that does not persist the "
		+ "primary-display column is reported as an error rather than a silent no-op. "
		+ "Read the set value back with get-entity-schema-properties (primary-display-column-name).")]
	public CommandExecutionResult SetEntitySchemaProperties(
		[Description("Parameters: environment-name, package-name, schema-name (all required); primary-display-column (optional)")] [Required]
		SetEntitySchemaPropertiesArgs args) {
		try {
			SetEntitySchemaPropertiesOptions options = new() {
				Environment = args.EnvironmentName,
				Package = args.PackageName,
				SchemaName = args.SchemaName,
				PrimaryDisplayColumn = args.PrimaryDisplayColumn
			};
			return InternalExecute<SetEntitySchemaPropertiesCommand>(options);
		} catch (Exception exception) {
			return new CommandExecutionResult(1, [new ErrorMessage(SensitiveErrorTextRedactor.Redact(exception.Message))], null);
		}
	}
}

/// <summary>
/// MCP tool surface for finding entity schemas by name, pattern, or UId.
/// </summary>
public sealed class FindEntitySchemaTool(
	FindEntitySchemaCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<FindEntitySchemaOptions>(command, logger, commandResolver) {

	internal const string FindEntitySchemaToolName = "find-entity-schema";

	/// <summary>
	/// Finds entity schemas by exact name, substring pattern, or UId.
	/// </summary>
	[McpServerTool(Name = FindEntitySchemaToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description(
		"Searches for entity schemas in a Creatio environment without needing to know the package name. "
		+ "Returns schema name, package, maintainer, and parent schema for each match. "
		+ "Use the returned 'package-name' field directly for follow-up MCP calls. "
		+ "Use 'schema-name' for exact lookup, 'search-pattern' for substring search, or 'uid' for Guid lookup.")]
	public IReadOnlyList<EntitySchemaSearchResult> FindEntitySchema(
		[Description(
			"Parameters: environment-name (required); exactly one of schema-name (exact match), "
			+ "search-pattern (case-insensitive contains), uid (Guid exact match)")]
		[Required]
		FindEntitySchemaArgs args) {
		FindEntitySchemaOptions options = new() {
			Environment = args.EnvironmentName,
			SchemaName = args.SchemaName,
			SearchPattern = args.SearchPattern,
			Uid = args.Uid
		};
		FindEntitySchemaCommand resolvedCommand = ResolveCommand<FindEntitySchemaCommand>(options);
		return resolvedCommand.FindSchemas(options);
	}
}


public sealed class GetEntitySchemaColumnPropertiesTool(
	GetEntitySchemaColumnPropertiesCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<GetEntitySchemaColumnPropertiesOptions>(command, logger, commandResolver) {

	internal const string GetEntitySchemaColumnPropertiesToolName = "get-entity-schema-column-properties";

	/// <summary>
	/// Returns structured properties for a remote entity schema column.
	/// </summary>
	[McpServerTool(Name = GetEntitySchemaColumnPropertiesToolName, ReadOnly = true, Destructive = false,
		Idempotent = true, OpenWorld = false)]
	[Description("Returns structured properties for the specified remote Creatio entity schema column. "
		+ "For a lookup column with a Const default, the returned default-value-config is enriched with "
		+ "display-value (the referenced record's display value, resolved in the connected user's culture) "
		+ "so the GUID can be verified without a second query. When the display value cannot be resolved, "
		+ "record-resolution carries an honest marker (no-access, not-found-or-no-access, or "
		+ "display-column-unavailable) and display-value is null.")]
	public EntitySchemaColumnPropertiesInfo GetEntitySchemaColumnProperties(
		[Description("Parameters: environment-name, package-name, schema-name, column-name (all required)")] [Required]
		GetEntitySchemaColumnPropertiesArgs args) {
		GetEntitySchemaColumnPropertiesOptions options = new() {
			Environment = args.EnvironmentName,
			Package = args.PackageName,
			SchemaName = args.SchemaName,
			ColumnName = args.ColumnName
		};

		GetEntitySchemaColumnPropertiesCommand resolvedCommand =
			ResolveCommand<GetEntitySchemaColumnPropertiesCommand>(options);
		return resolvedCommand.GetColumnProperties(options);
	}
}

/// <summary>
/// MCP tool surface for remote entity schema column mutations.
/// </summary>
public sealed class ModifyEntitySchemaColumnTool(ModifyEntitySchemaColumnCommand command, ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<ModifyEntitySchemaColumnOptions>(command, logger, commandResolver) {

	internal const string ModifyEntitySchemaColumnToolName = "modify-entity-schema-column";

	/// <summary>
	/// Adds, updates, or removes a remote entity schema column.
	/// </summary>
	[McpServerTool(Name = ModifyEntitySchemaColumnToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Adds, modifies, or removes a column in a remote Creatio entity schema. "
		+ "The change is published and the OData entities are rebuilt automatically, so the column becomes reachable "
		+ "over OData (/0/odata/<Entity>) without a compile. That rebuild is asynchronous (~1-2 min): a 404 (or "
		+ "\"The request is invalid\") from an odata-* tool right after the change is the expected async gap — wait "
		+ "briefly and retry, do not compile. Each call publishes once, so to change several columns at once batch "
		+ "them through update-entity-schema rather than one call per column. "
		+ "When setting a Const default on a lookup column, the referenced record's existence is validated "
		+ "before save: a GUID that does not exist in the referenced schema is rejected with a non-zero exit "
		+ "and the schema is not saved. The check is point-in-time (TOCTOU) and is skipped when the referenced "
		+ "record cannot be read. "
		+ "An INHERITED column can have only its caption/description overridden (title-localizations / "
		+ "description-localizations) on a replacing/child schema; its name, type, and flags stay read-only. "
		+ "Entity business rules are separate — call get-guidance with name business-rules.")]
	public CommandExecutionResult ModifyEntitySchemaColumn(
		[Description("Parameters: environment-name, package-name, schema-name, action, column-name (all required); type, title-localizations, description-localizations, reference-schema-name, and many flags (optional)")] [Required] ModifyEntitySchemaColumnArgs args) {
		try {
			string resolvedColumnName = args.ResolveColumnName();
			string context = $"Column '{resolvedColumnName}' action '{args.Action}'";
			IReadOnlyDictionary<string, string>? titleLocalizations =
				EntitySchemaLocalizationContract.NormalizeMutationTitleLocalizations(
					args.Action,
					args.TitleLocalizations,
					args.LegacyTitle,
					args.LegacyCaption,
					resolvedColumnName,
					context);
			TitleLocalizationNormalizationResult titleNormalization =
				EntitySchemaDesignerSupport.NormalizeTitleLocalizations(
					titleLocalizations,
					null,
					"title-localizations");
			ModifyEntitySchemaColumnOptions options = new() {
				Environment = args.EnvironmentName,
				Package = args.PackageName,
				SchemaName = args.SchemaName,
				Action = args.Action,
				ColumnName = resolvedColumnName,
				NewName = args.NewName,
				Type = args.ResolveType(),
				Title = titleNormalization.EffectiveTitle,
				TitleLocalizations = titleNormalization.Localizations,
				DescriptionLocalizations = EntitySchemaLocalizationContract.NormalizeMutationDescriptionLocalizations(
					args.Action,
					args.DescriptionLocalizations,
					args.LegacyDescription,
					context),
				ReferenceSchemaName = args.ResolveReferenceSchemaName(),
				Required = args.ResolveRequired(),
				Indexed = args.Indexed,
				Cloneable = args.Cloneable,
				TrackChanges = args.TrackChanges,
				DefaultValueSource = args.DefaultValueSource,
				DefaultValue = args.DefaultValue,
				DefaultValueConfig = args.DefaultValueConfig,
				MultilineText = args.MultilineText,
				LocalizableText = args.LocalizableText,
				AccentInsensitive = args.AccentInsensitive,
				Masked = args.Masked,
				FormatValidated = args.FormatValidated,
				UseSeconds = args.UseSeconds,
				SimpleLookup = args.SimpleLookup,
				Cascade = args.Cascade,
				DoNotControlIntegrity = args.DoNotControlIntegrity,
				CaptionCulture = args.CaptionCulture,
				UsageType = args.UsageType
			};
			return InternalExecute<ModifyEntitySchemaColumnCommand>(options);
		} catch (Exception exception) {
			return new CommandExecutionResult(1, [new ErrorMessage(SensitiveErrorTextRedactor.Redact(exception.Message))], null);
		}
	}
}

/// <summary>
/// Shared request contract for MCP tools that create remote entity schemas.
/// </summary>
public abstract record EntitySchemaCreateArgsBase(
	[property: JsonPropertyName("package-name")]
	[property: Description("Target package name on the Creatio environment")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("schema-name")]
	[property: Description("Entity schema name. " +
		"Must use the active SchemaNamePrefix as prefix (e.g. 'UsrAlpha' when prefix is 'Usr', 'MyPrefixAlpha' when prefix is 'MyPrefix'). " +
		"When `schema-name-prefix` is empty, use no prefix (plain PascalCase, e.g. 'Alpha'). " +
		"Read the prefix from the `schema-name-prefix` field returned by `get-app-info`, " +
		"or call `get-schema-name-prefix` if you have not called `get-app-info` yet.")]
	[property: Required]
	string SchemaName,

	[property: JsonPropertyName("title-localizations")]
	[property: Description("Entity schema title/caption localizations. Must include en-US.")]
	[property: Required]
	Dictionary<string, string> TitleLocalizations,

	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("columns")]
	[property: Description("Optional initial columns to add to the schema. " +
		"Column codes must also use the active SchemaNamePrefix (e.g. 'UsrEmail' when prefix is 'Usr'). " +
		"When `schema-name-prefix` is empty, use plain column names with no prefix. " +
		"Use the same prefix value from `schema-name-prefix`.")]
	IEnumerable<CreateEntitySchemaColumnArgs>? Columns = null
) {
	[property: JsonPropertyName("title")]
	[property: Description("Legacy scalar title. Not accepted by MCP. Use title-localizations instead.")]
	public string? LegacyTitle { get; init; }

	[property: JsonPropertyName("caption-culture")]
	[property: Description("Optional culture override for generated captions (e.g. 'en-US', 'uk-UA'). Precedence: caption-culture > detected profile culture > en-US. Skips the profile-culture lookup.")]
	public string? CaptionCulture { get; init; }
}

/// <summary>
/// Arguments for the <c>create-entity-schema</c> MCP tool.
/// </summary>
public sealed record CreateEntitySchemaArgs(
	string PackageName,
	string SchemaName,
	Dictionary<string, string> TitleLocalizations,
	string EnvironmentName,

	[property: JsonPropertyName("parent-schema-name")]
	[property: Description("Optional parent schema name")]
	string? ParentSchemaName = null,

	[property: JsonPropertyName("extend-parent")]
	[property: Description("Create a replacement schema. Requires parent-schema-name.")]
	bool ExtendParent = false,

	IEnumerable<CreateEntitySchemaColumnArgs>? Columns = null
) : EntitySchemaCreateArgsBase(PackageName, SchemaName, TitleLocalizations, EnvironmentName, Columns) {
	/// <summary>
	/// Gets whether the entity schema is virtual and must not have a physical database table.
	/// </summary>
	[property: JsonPropertyName("is-virtual")]
	[property: Description("Create a virtual entity schema without a physical database table. Defaults to false.")]
	public bool IsVirtual { get; init; }
}

/// <summary>
/// Arguments for the <c>create-lookup</c> MCP tool.
/// </summary>
public sealed record CreateLookupArgs(
	string PackageName,
	string SchemaName,
	Dictionary<string, string> TitleLocalizations,
	string EnvironmentName,

	IEnumerable<CreateEntitySchemaColumnArgs>? Columns = null
) : EntitySchemaCreateArgsBase(PackageName, SchemaName, TitleLocalizations, EnvironmentName, Columns);

/// <summary>
/// Shared request contract containing environment, package, and schema name properties.
/// </summary>
public abstract record EntitySchemaTargetArgsBase(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("package-name")]
	[property: Description("Target package name on the Creatio environment")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("schema-name")]
	[property: Description("Entity schema name")]
	[property: Required]
	string SchemaName
);

/// <summary>
/// Arguments for the <c>update-entity-schema</c> MCP tool.
/// </summary>
public sealed record UpdateEntitySchemaArgs(
	string EnvironmentName,
	string PackageName,
	string SchemaName,

	[property: JsonPropertyName("operations")]
	[property: Description("Batch column operations to apply in order.")]
	[property: Required]
	IEnumerable<UpdateEntitySchemaOperationArgs> Operations
) : EntitySchemaTargetArgsBase(EnvironmentName, PackageName, SchemaName);

/// <summary>
/// Structured column input for the <c>create-entity-schema</c> MCP tool.
/// </summary>
public sealed record CreateEntitySchemaColumnArgs(
	[property: JsonPropertyName("name")]
	[property: Description("Column code. Must use the active SchemaNamePrefix as prefix " +
		"(e.g. 'UsrStatus' when prefix is 'Usr', 'MyStatus' when prefix is 'My'). " +
		"When `schema-name-prefix` is empty, use plain PascalCase with no prefix (e.g. 'Status'). " +
		"Use the same prefix value from `schema-name-prefix`.")]
	[property: Required]
	string Name,

	[property: JsonPropertyName("type")]
	[property: Description("""
						  Column type. Supported values:
						  Guid, Text, ShortText, MediumText, LongText, MaxSizeText,
						  Integer, Float, Boolean, Date, DateTime, Time, Lookup,
						  Binary, Image, ImageLookup, File, SecureText, Email, Color.
						  Color stores a hex color string (e.g. #RRGGBB) and is not a text column:
						  text-only options (multiline / accent-insensitive / format-validated / masked) do not apply.
						  Blob is also accepted as an alias for Binary.
						  ImageLink is also accepted as an alias for ImageLookup.
						  Encrypted and Password are accepted as aliases for SecureText.
						  EmailAddress is accepted as an alias for Email.
						  For image/photo fields rendered by the crt.ImageInput Freedom UI component,
						  use ImageLookup ("Image link") — NOT the binary Image type, which crt.ImageInput
						  cannot read or write. ImageLookup references the SysImage schema automatically.
						  """)]
	[property: Required]
	string Type,

	[property: JsonPropertyName("title-localizations")]
	[property: Description("Column title/caption localizations. OPTIONAL — when omitted, en-US is auto-derived from a scalar title/caption or the column name. Must include en-US when provided, and the en-US value must be English.")]
	Dictionary<string, string>? TitleLocalizations = null,

	[property: JsonPropertyName("reference-schema-name")]
	[property: Description("Required when type is Lookup. Use an entity schema name like Contact or Account. Do not set for ImageLookup — it references the SysImage schema automatically.")]
	string? ReferenceSchemaName = null
) {
	[property: JsonPropertyName("title")]
	[property: Description("Legacy scalar title. Not accepted by MCP. Use title-localizations instead.")]
	public string? LegacyTitle { get; init; }

	[property: JsonPropertyName("caption")]
	[property: Description("Legacy scalar caption alias. Not accepted by MCP. Use title-localizations instead.")]
	public string? LegacyCaption { get; init; }

	[property: JsonPropertyName("required")]
	[property: Description("Optional required flag for the created column.")]
	public bool? Required { get; init; }

	[property: JsonPropertyName("default-value-source")]
	[property: Description("Legacy default value source shorthand. Supported values: Const or None. Binary, Image, and File columns do not support Const. Use default-value-config for Settings, SystemValue, or Sequence.")]
	public string? DefaultValueSource { get; init; }

	[property: JsonPropertyName("default-value")]
	[property: Description("Legacy constant default value shorthand used together with default-value-source Const. Binary, Image, and File columns do not support constant defaults.")]
	public string? DefaultValue { get; init; }

	/// <summary>
	/// Gets the structured default value metadata used for non-legacy default scenarios.
	/// </summary>
	[property: JsonPropertyName("default-value-config")]
	[property: Description("Structured default value metadata. Settings value-source accepts code/name/id and resolves to code. SystemValue value-source accepts GUID/alias/caption and resolves to GUID.")]
	public EntitySchemaDefaultValueConfig? DefaultValueConfig { get; init; }

	[property: JsonPropertyName("masked")]
	[property: Description("Optional masked flag. Allowed for Text and SecureText columns.")]
	public bool? Masked { get; init; }

	/// <summary>
	/// Gets the read-shape alias for <c>type</c>. <c>get-app-info</c> reports the column type as
	/// <c>data-value-type</c>, so this lets that read shape be reused for a create/add without translation (ENG-90313).
	/// </summary>
	[property: JsonPropertyName("data-value-type")]
	[property: Description("Alias for type. Accepts the get-app-info read shape (which reports the column type as 'data-value-type').")]
	public string? DataValueTypeAlias { get; init; }

	/// <summary>
	/// Gets the read-shape alias for <c>reference-schema-name</c>. <c>get-app-info</c> reports the lookup
	/// reference as <c>reference-schema</c>, so this lets that read shape be reused without translation (ENG-90313).
	/// </summary>
	[property: JsonPropertyName("reference-schema")]
	[property: Description("Alias for reference-schema-name. Accepts the get-app-info read shape (which reports the lookup reference as 'reference-schema').")]
	public string? ReferenceSchemaAlias { get; init; }

	/// <summary>
	/// Resolves the effective column type, preferring the canonical <c>type</c> and falling back to the
	/// <c>data-value-type</c> read-shape alias.
	/// </summary>
	/// <returns>The canonical type, or the alias when the canonical field is absent.</returns>
	public string? ResolveType() =>
		!string.IsNullOrWhiteSpace(Type) ? Type : DataValueTypeAlias;

	/// <summary>
	/// Resolves the effective lookup reference schema name, preferring the canonical
	/// <c>reference-schema-name</c> and falling back to the <c>reference-schema</c> read-shape alias.
	/// </summary>
	/// <returns>The canonical reference schema name, or the alias when the canonical field is absent.</returns>
	public string? ResolveReferenceSchemaName() =>
		!string.IsNullOrWhiteSpace(ReferenceSchemaName) ? ReferenceSchemaName : ReferenceSchemaAlias;

	/// <summary>
	/// Gets the kebab-cased alias for <c>required</c>. Agents naturally spell the <c>IsRequired</c> flag as
	/// <c>is-required</c>, so this accepts that spelling instead of silently dropping it (ENG-90313).
	/// </summary>
	[property: JsonPropertyName("is-required")]
	[property: Description("Alias for required. Accepts the 'is-required' spelling agents commonly send.")]
	public bool? IsRequiredAlias { get; init; }

	/// <summary>
	/// Resolves the effective required flag, preferring the canonical <c>required</c> and falling back to the
	/// <c>is-required</c> alias.
	/// </summary>
	/// <returns>The canonical required flag, or the alias when the canonical field is absent.</returns>
	public bool? ResolveRequired() => Required ?? IsRequiredAlias;

	/// <summary>
	/// Gets the <c>column-name</c> alias for <c>name</c>. The <c>get-tool-contract</c> output advertises
	/// <c>column-name</c> (alias <c>name</c>) for column identity, so an agent following the contract naturally
	/// puts <c>column-name</c> into the read/create-shape <c>columns[]</c> array. Accepting it here keeps that
	/// documented field working instead of silently dropping it (field-test defect #1).
	/// </summary>
	[property: JsonPropertyName("column-name")]
	[property: Description("Alias for name. Accepts the get-tool-contract column identity field 'column-name'.")]
	public string? ColumnNameAlias { get; init; }

	/// <summary>
	/// Resolves the effective column code, preferring the canonical <c>name</c> and falling back to the
	/// <c>column-name</c> alias advertised by <c>get-tool-contract</c>.
	/// </summary>
	/// <returns>The canonical name, or the <c>column-name</c> alias when the canonical field is absent.</returns>
	public string? ResolveName() =>
		!string.IsNullOrWhiteSpace(Name) ? Name : ColumnNameAlias;
}

/// <summary>
/// Shared column-modification properties used by both single-column and batch MCP tools.
/// </summary>
public abstract record ColumnModificationArgsBase(
	[property: JsonPropertyName("action")]
	[property: Description("Column action: add, modify, or remove")]
	[property: Required]
	string Action,

	[property: JsonPropertyName("column-name")]
	[property: Description("Target column name")]
	[property: Required]
	string ColumnName,

	[property: JsonPropertyName("new-name")]
	[property: Description("New column name for rename operations")]
	string? NewName = null,

	[property: JsonPropertyName("type")]
	[property: Description("""
						   Column type. Supported values:
						   Guid, Integer, Float, Boolean, Date, DateTime, Time, Lookup,
						   Text, ShortText, MediumText, LongText, MaxSizeText,
						   Binary, Image, ImageLookup, File, Blob, SecureText,
						   Text50, Text250, Text500, TextUnlimited, PhoneNumber, WebLink, Email, RichText,
						   Decimal0, Decimal1, Decimal2, Decimal3, Decimal4, Decimal8,
						   Currency0, Currency1, Currency2, Currency3, Color.
						   Color stores a hex color string (e.g. #RRGGBB) and is not a text column:
						   text-only options (multiline / accent-insensitive / format-validated / masked) do not apply.
						   Encrypted and Password are accepted as aliases for SecureText.
						   ImageLink is accepted as an alias for ImageLookup.
						   EmailAddress is accepted as an alias for Email.
						   For image/photo fields bound to the crt.ImageInput component, use ImageLookup
						   ("Image link") — the binary Image type does not work with crt.ImageInput.
						   ImageLookup references the SysImage schema automatically (no reference-schema-name).
						   """)]
	string? Type = null,

	[property: JsonPropertyName("title-localizations")]
	[property: Description("Column title/caption localizations. OPTIONAL for add — when omitted, en-US is auto-derived from a scalar title/caption or the column name. Must include en-US when provided, and the en-US value must be English.")]
	Dictionary<string, string>? TitleLocalizations = null,

	[property: JsonPropertyName("description-localizations")]
	[property: Description("Column description localizations. Must include en-US when provided.")]
	Dictionary<string, string>? DescriptionLocalizations = null,

	[property: JsonPropertyName("reference-schema-name")]
	[property: Description("Lookup reference schema name")]
	string? ReferenceSchemaName = null,

	[property: JsonPropertyName("required")]
	[property: Description("Set the required flag")]
	bool? IsRequired = null,

	[property: JsonPropertyName("indexed")]
	[property: Description("Set the indexed flag")]
	bool? Indexed = null,

	[property: JsonPropertyName("cloneable")]
	[property: Description("Set the cloneable flag")]
	bool? Cloneable = null,

	[property: JsonPropertyName("track-changes")]
	[property: Description("Set the track-changes flag")]
	bool? TrackChanges = null,

	[property: JsonPropertyName("default-value")]
	[property: Description("Legacy constant default value shorthand used together with default-value-source Const. Binary, Image, and File columns do not support constant defaults.")]
	string? DefaultValue = null,

	[property: JsonPropertyName("default-value-source")]
	[property: Description("Legacy default value source shorthand. Supported values: Const or None. Binary, Image, and File columns do not support Const. Use default-value-config for Settings, SystemValue, or Sequence.")]
	string? DefaultValueSource = null,

	[property: JsonPropertyName("multiline-text")]
	[property: Description("Set the multi-line text flag")]
	bool? MultilineText = null,

	[property: JsonPropertyName("localizable-text")]
	[property: Description("Set the localizable text flag")]
	bool? LocalizableText = null,

	[property: JsonPropertyName("accent-insensitive")]
	[property: Description("Set the accent-insensitive flag")]
	bool? AccentInsensitive = null,

	[property: JsonPropertyName("masked")]
	[property: Description("Set the masked flag")]
	bool? Masked = null,

	[property: JsonPropertyName("format-validated")]
	[property: Description("Set the format-validated flag")]
	bool? FormatValidated = null,

	[property: JsonPropertyName("use-seconds")]
	[property: Description("Set the use-seconds flag")]
	bool? UseSeconds = null,

	[property: JsonPropertyName("simple-lookup")]
	[property: Description("Set the simple-lookup flag")]
	bool? SimpleLookup = null,

	[property: JsonPropertyName("cascade")]
	[property: Description("Set the cascade-connection flag")]
	bool? Cascade = null,

	[property: JsonPropertyName("do-not-control-integrity")]
	[property: Description("Set the do-not-control-integrity flag")]
	bool? DoNotControlIntegrity = null
) {
	[property: JsonPropertyName("title")]
	[property: Description("Legacy scalar title. For add it is used only as an en-US fallback when title-localizations is omitted; prefer title-localizations.")]
	public string? LegacyTitle { get; init; }

	[property: JsonPropertyName("caption")]
	[property: Description("Legacy scalar caption alias. For add it is used only as an en-US fallback when title-localizations is omitted; prefer title-localizations.")]
	public string? LegacyCaption { get; init; }

	[property: JsonPropertyName("description")]
	[property: Description("Legacy scalar description. Not accepted by MCP. Use description-localizations instead.")]
	public string? LegacyDescription { get; init; }

	/// <summary>
	/// Gets the structured default value metadata used for non-legacy mutation scenarios.
	/// </summary>
	[property: JsonPropertyName("default-value-config")]
	[property: Description("Structured default value metadata. Settings value-source accepts code/name/id and resolves to code. SystemValue value-source accepts GUID/alias/caption and resolves to GUID.")]
	public EntitySchemaDefaultValueConfig? DefaultValueConfig { get; init; }

	/// <summary>
	/// Gets the read-shape alias for <c>column-name</c>. <c>get-app-info</c> reports the column identity as
	/// <c>name</c>, so this lets that read shape be sent back to <c>update-entity</c> without translation (ENG-90313).
	/// </summary>
	[property: JsonPropertyName("name")]
	[property: Description("Alias for column-name. Accepts the get-app-info read shape (which reports the column identity as 'name').")]
	public string? NameAlias { get; init; }

	/// <summary>
	/// Gets the read-shape alias for <c>type</c>. <c>get-app-info</c> reports the column type as
	/// <c>data-value-type</c>, so this lets that read shape be sent back without translation (ENG-90313).
	/// </summary>
	[property: JsonPropertyName("data-value-type")]
	[property: Description("Alias for type. Accepts the get-app-info read shape (which reports the column type as 'data-value-type').")]
	public string? DataValueTypeAlias { get; init; }

	/// <summary>
	/// Gets the read-shape alias for <c>reference-schema-name</c>. <c>get-app-info</c> reports the lookup
	/// reference as <c>reference-schema</c>, so this lets that read shape be sent back without translation (ENG-90313).
	/// </summary>
	[property: JsonPropertyName("reference-schema")]
	[property: Description("Alias for reference-schema-name. Accepts the get-app-info read shape (which reports the lookup reference as 'reference-schema').")]
	public string? ReferenceSchemaAlias { get; init; }

	/// <summary>
	/// Resolves the effective target column name, preferring the canonical <c>column-name</c> and falling
	/// back to the <c>name</c> read-shape alias.
	/// </summary>
	/// <returns>The canonical column name, or the alias when the canonical field is absent.</returns>
	public string? ResolveColumnName() =>
		!string.IsNullOrWhiteSpace(ColumnName) ? ColumnName : NameAlias;

	/// <summary>
	/// Resolves the effective column type, preferring the canonical <c>type</c> and falling back to the
	/// <c>data-value-type</c> read-shape alias.
	/// </summary>
	/// <returns>The canonical type, or the alias when the canonical field is absent.</returns>
	public string? ResolveType() =>
		!string.IsNullOrWhiteSpace(Type) ? Type : DataValueTypeAlias;

	/// <summary>
	/// Resolves the effective lookup reference schema name, preferring the canonical
	/// <c>reference-schema-name</c> and falling back to the <c>reference-schema</c> read-shape alias.
	/// </summary>
	/// <returns>The canonical reference schema name, or the alias when the canonical field is absent.</returns>
	public string? ResolveReferenceSchemaName() =>
		!string.IsNullOrWhiteSpace(ReferenceSchemaName) ? ReferenceSchemaName : ReferenceSchemaAlias;

	/// <summary>
	/// Gets the kebab-cased alias for <c>required</c>. Agents naturally spell the <c>IsRequired</c> flag as
	/// <c>is-required</c>, so this accepts that spelling instead of silently dropping it (ENG-90313).
	/// </summary>
	[property: JsonPropertyName("is-required")]
	[property: Description("Alias for required. Accepts the 'is-required' spelling agents commonly send.")]
	public bool? IsRequiredAlias { get; init; }

	/// <summary>
	/// Resolves the effective required flag, preferring the canonical <c>required</c> and falling back to the
	/// <c>is-required</c> alias.
	/// </summary>
	/// <returns>The canonical required flag, or the alias when the canonical field is absent.</returns>
	public bool? ResolveRequired() => IsRequired ?? IsRequiredAlias;

	[property: JsonPropertyName("caption-culture")]
	[property: Description("Optional culture override for the written column caption/description (e.g. 'en-US', 'uk-UA'). Precedence: caption-culture > detected profile culture > en-US. Skips the profile-culture lookup.")]
	public string? CaptionCulture { get; init; }

	/// <summary>
	/// Gets the column usage type (General/Advanced/None). Type-independent; case-insensitive. On modify the
	/// stored value is left unchanged when omitted.
	/// </summary>
	[property: JsonPropertyName("usage-type")]
	[property: Description("Column usage type: General (default), Advanced, or None. Case-insensitive; applies to any column type. On modify, the stored value is left unchanged when omitted.")]
	public string? UsageType { get; init; }
}

/// <summary>
/// Structured operation input for the <c>update-entity-schema</c> MCP tool.
/// </summary>
public sealed record UpdateEntitySchemaOperationArgs(
	string Action,
	string ColumnName,
	string? NewName = null,
	string? Type = null,
	Dictionary<string, string>? TitleLocalizations = null,
	Dictionary<string, string>? DescriptionLocalizations = null,
	string? ReferenceSchemaName = null,
	bool? IsRequired = null,
	bool? Indexed = null,
	bool? Cloneable = null,
	bool? TrackChanges = null,
	string? DefaultValue = null,
	string? DefaultValueSource = null,
	bool? MultilineText = null,
	bool? LocalizableText = null,
	bool? AccentInsensitive = null,
	bool? Masked = null,
	bool? FormatValidated = null,
	bool? UseSeconds = null,
	bool? SimpleLookup = null,
	bool? Cascade = null,
	bool? DoNotControlIntegrity = null
) : ColumnModificationArgsBase(Action, ColumnName, NewName, Type, TitleLocalizations, DescriptionLocalizations,
	ReferenceSchemaName, IsRequired, Indexed, Cloneable, TrackChanges, DefaultValue,
	DefaultValueSource, MultilineText, LocalizableText, AccentInsensitive, Masked,
	FormatValidated, UseSeconds, SimpleLookup, Cascade, DoNotControlIntegrity);

/// <summary>
/// Arguments for the <c>get-entity-schema-properties</c> MCP tool.
/// <c>package-name</c> is optional: omit it to read the merged/effective schema (columns from every package),
/// or supply it to read only that package layer's slice.
/// </summary>
/// <remarks>
/// This record intentionally does NOT extend <see cref="EntitySchemaTargetArgsBase"/>: that base marks
/// <c>package-name</c> as <c>[Required]</c>, whereas this tool makes the package optional (a <c>null</c>
/// <c>package-name</c> is the signal to return the merged all-packages view). The shared <c>environment-name</c>
/// and <c>schema-name</c> declarations are therefore duplicated here on purpose.
/// </remarks>
public sealed record GetEntitySchemaPropertiesArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("package-name")]
	[property: Description("Optional target package name. Omit to read the merged/effective schema with columns "
		+ "from ALL packages (recommended for column discovery). Supply only to inspect a single package layer's slice.")]
	string? PackageName,

	[property: JsonPropertyName("schema-name")]
	[property: Description("Entity schema name")]
	[property: Required]
	string SchemaName
);

/// <summary>
/// Arguments for the <c>set-entity-schema-properties</c> MCP tool.
/// </summary>
public sealed record SetEntitySchemaPropertiesArgs(
	string EnvironmentName,
	string PackageName,
	string SchemaName,

	[property: JsonPropertyName("primary-display-column")]
	[property: Description("Column name (own or inherited) to set as the schema's primary-display column")]
	string? PrimaryDisplayColumn = null
) : EntitySchemaTargetArgsBase(EnvironmentName, PackageName, SchemaName);

/// <summary>
/// Arguments for the <c>get-entity-schema-column-properties</c> MCP tool.
/// </summary>
public sealed record GetEntitySchemaColumnPropertiesArgs(
	string EnvironmentName,
	string PackageName,
	string SchemaName,

	[property: JsonPropertyName("column-name")]
	[property: Description("Column name")]
	[property: Required]
	string ColumnName
) : EntitySchemaTargetArgsBase(EnvironmentName, PackageName, SchemaName);

/// <summary>
/// Arguments for the <c>modify-entity-schema-column</c> MCP tool.
/// </summary>
public sealed record ModifyEntitySchemaColumnArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("package-name")]
	[property: Description("Target package name on the Creatio environment")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("schema-name")]
	[property: Description("Entity schema name")]
	[property: Required]
	string SchemaName,

	string Action,
	string ColumnName,
	string? NewName = null,
	string? Type = null,
	Dictionary<string, string>? TitleLocalizations = null,
	Dictionary<string, string>? DescriptionLocalizations = null,
	string? ReferenceSchemaName = null,
	bool? IsRequired = null,
	bool? Indexed = null,
	bool? Cloneable = null,
	bool? TrackChanges = null,
	string? DefaultValue = null,
	string? DefaultValueSource = null,
	bool? MultilineText = null,
	bool? LocalizableText = null,
	bool? AccentInsensitive = null,
	bool? Masked = null,
	bool? FormatValidated = null,
	bool? UseSeconds = null,
	bool? SimpleLookup = null,
	bool? Cascade = null,
	bool? DoNotControlIntegrity = null
) : ColumnModificationArgsBase(Action, ColumnName, NewName, Type, TitleLocalizations, DescriptionLocalizations,
	ReferenceSchemaName, IsRequired, Indexed, Cloneable, TrackChanges, DefaultValue,
	DefaultValueSource, MultilineText, LocalizableText, AccentInsensitive, Masked,
	FormatValidated, UseSeconds, SimpleLookup, Cascade, DoNotControlIntegrity);


/// <summary>
/// Arguments for the <c>find-entity-schema</c> MCP tool.
/// </summary>
public sealed record FindEntitySchemaArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("schema-name")]
	[property: Description("Exact entity schema name to find (use instead of search-pattern or uid)")]
	string? SchemaName = null,

	[property: JsonPropertyName("search-pattern")]
	[property: Description("Case-insensitive substring to search in entity schema names (use instead of schema-name or uid)")]
	string? SearchPattern = null,

	[property: JsonPropertyName("uid")]
	[property: Description("Entity schema UId (Guid) for exact lookup (use instead of schema-name or search-pattern)")]
	string? Uid = null
);
