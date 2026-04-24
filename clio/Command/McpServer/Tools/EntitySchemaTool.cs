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
				 """)]
	public async Task<CommandExecutionResult> CreateEntitySchema(
		[Description("Parameters: environment-name, package-name, schema-name, title-localizations (all required); columns, parent-schema-name (optional, defaults to BaseEntity unless extend-parent is true), extend-parent (optional, requires parent-schema-name when true)")] [Required] CreateEntitySchemaArgs args
	) {
		ApplicationDataForgeResult? dataForge = enrichmentService is not null
			? await enrichmentService.EnrichAsync(
				args.EnvironmentName,
				BuildCandidateTerms(args.SchemaName, args.TitleLocalizations))
			: null;
		try {
			CreateEntitySchemaOptions options = CreateOptions(args, args.ParentSchemaName, args.ExtendParent);
			CommandExecutionResult result = InternalExecute<CreateEntitySchemaCommand>(options);
			return result with { DataForge = dataForge };
		} catch (Exception exception) {
			return new CommandExecutionResult(1, [new ErrorMessage(exception.Message)], null, dataForge);
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
		bool extendParent) {
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
			Columns = SerializeColumns(args.Columns, context),
			Environment = args.EnvironmentName
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
			context);
		return JsonSerializer.Serialize(new Dictionary<string, object?> {
			["name"] = column.Name?.Trim(),
			["type"] = column.Type?.Trim(),
			["title-localizations"] = titleLocalizations,
			["reference-schema-name"] = string.IsNullOrWhiteSpace(column.ReferenceSchemaName)
				? null
				: column.ReferenceSchemaName.Trim(),
			["required"] = column.Required,
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
				 not send them as custom columns.
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
			? await _enrichmentService.EnrichAsync(args.EnvironmentName, lookupHints, lookupHints)
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
			lock (CommandExecutionSyncRoot) {
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
						[.. _logger.FlushAndSnapshotMessages(clearMessages: true)],
						null,
						dataForge);
					return returnResult;
				}
				catch (Exception exception) {
					List<LogMessage> logMessages = [.. _logger.FlushAndSnapshotMessages(clearMessages: true), new ErrorMessage(exception.Message)];
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
			}
		} catch (Exception exception) {
			return new CommandExecutionResult(1, [new ErrorMessage(exception.Message)], null, dataForge);
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
	[Description("Applies a batch of add, modify, and remove column operations to a remote Creatio entity schema.")]
	public async Task<CommandExecutionResult> UpdateEntitySchema(
		[Description("Parameters: environment-name, package-name, schema-name, operations (all required)")] [Required] UpdateEntitySchemaArgs args) {
		ApplicationDataForgeResult? dataForge = null;
		try {
			if (enrichmentService is not null) {
				dataForge = await enrichmentService.EnrichAsync(
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
			return result with { DataForge = dataForge };
		} catch (Exception exception) {
			return new CommandExecutionResult(1, [new ErrorMessage(exception.Message)], null, dataForge);
		}
	}

	private static IReadOnlyList<string> BuildCandidateTerms(UpdateEntitySchemaArgs args) {
		return new[] { args.SchemaName }
			.Concat(args.Operations
				.Where(op => string.Equals(op.Action, "add", StringComparison.OrdinalIgnoreCase)
					&& !string.IsNullOrWhiteSpace(op.ColumnName))
				.Select(op => op.ColumnName!.Trim()))
			.Where(term => !string.IsNullOrWhiteSpace(term))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static IReadOnlyList<string> BuildLookupHints(UpdateEntitySchemaArgs args) {
		return args.Operations
			.Where(op => string.Equals(op.Action, "add", StringComparison.OrdinalIgnoreCase)
				&& !string.IsNullOrWhiteSpace(op.ReferenceSchemaName))
			.Select(op => op.ReferenceSchemaName!.Trim())
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
				context);
		IReadOnlyDictionary<string, string>? descriptionLocalizations =
			EntitySchemaLocalizationContract.NormalizeMutationDescriptionLocalizations(
				operation.Action,
				operation.DescriptionLocalizations,
				operation.LegacyDescription,
				context);
		return new Dictionary<string, object?> {
			["action"] = operation.Action,
			["column-name"] = operation.ColumnName,
			["new-name"] = operation.NewName,
			["type"] = operation.Type,
			["title-localizations"] = titleLocalizations,
			["description-localizations"] = descriptionLocalizations,
			["reference-schema-name"] = operation.ReferenceSchemaName,
			["required"] = operation.IsRequired,
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
			["do-not-control-integrity"] = operation.DoNotControlIntegrity
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
	[Description("Returns structured properties for the specified remote Creatio entity schema.")]
	public EntitySchemaPropertiesInfo GetEntitySchemaProperties(
		[Description("Parameters: environment-name, package-name, schema-name (all required)")] [Required] GetEntitySchemaPropertiesArgs args) {
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
	[Description("Returns structured properties for the specified remote Creatio entity schema column.")]
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
	[Description("Adds, modifies, or removes a column in a remote Creatio entity schema.")]
	public CommandExecutionResult ModifyEntitySchemaColumn(
		[Description("Parameters: environment-name, package-name, schema-name, action, column-name (all required); type, title-localizations, description-localizations, reference-schema-name, and many flags (optional)")] [Required] ModifyEntitySchemaColumnArgs args) {
		try {
			string context = $"Column '{args.ColumnName}' action '{args.Action}'";
			IReadOnlyDictionary<string, string>? titleLocalizations =
				EntitySchemaLocalizationContract.NormalizeMutationTitleLocalizations(
					args.Action,
					args.TitleLocalizations,
					args.LegacyTitle,
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
				ColumnName = args.ColumnName,
				NewName = args.NewName,
				Type = args.Type,
				Title = titleNormalization.EffectiveTitle,
				TitleLocalizations = titleNormalization.Localizations,
				DescriptionLocalizations = EntitySchemaLocalizationContract.NormalizeMutationDescriptionLocalizations(
					args.Action,
					args.DescriptionLocalizations,
					args.LegacyDescription,
					context),
				ReferenceSchemaName = args.ReferenceSchemaName,
				Required = args.IsRequired,
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
				DoNotControlIntegrity = args.DoNotControlIntegrity
			};
			return InternalExecute<ModifyEntitySchemaColumnCommand>(options);
		} catch (Exception exception) {
			return new CommandExecutionResult(1, [new ErrorMessage(exception.Message)], null);
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
	[property: Description("Entity schema name. Maximum length is 22 characters.")]
	[property: Required]
	string SchemaName,

	[property: JsonPropertyName("title-localizations")]
	[property: Description("Entity schema title/caption localizations. Must include en-US.")]
	[property: Required]
	Dictionary<string, string> TitleLocalizations,

	[property: JsonPropertyName("environment-name")]
	[property: Description("Creatio environment name")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("columns")]
	[property: Description("Optional initial columns to add to the schema.")]
	IEnumerable<CreateEntitySchemaColumnArgs>? Columns = null
) {
	[property: JsonPropertyName("title")]
	[property: Description("Legacy scalar title. Not accepted by MCP. Use title-localizations instead.")]
	public string? LegacyTitle { get; init; }
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
) : EntitySchemaCreateArgsBase(PackageName, SchemaName, TitleLocalizations, EnvironmentName, Columns);

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
	[property: Description("Creatio environment name")]
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
	[property: Description("Column name")]
	[property: Required]
	string Name,

	[property: JsonPropertyName("type")]
	[property: Description("""
						  Column type. Supported values:
						  Guid, Text, ShortText, MediumText, LongText, MaxSizeText,
						  Integer, Float, Boolean, Date, DateTime, Time, Lookup,
						  Binary, Image, File, SecureText, Email.
						  Blob is also accepted as an alias for Binary.
						  Encrypted and Password are accepted as aliases for SecureText.
						  EmailAddress is accepted as an alias for Email.
						  """)]
	[property: Required]
	string Type,

	[property: JsonPropertyName("title-localizations")]
	[property: Description("Column title/caption localizations. Must include en-US.")]
	[property: Required]
	Dictionary<string, string> TitleLocalizations,

	[property: JsonPropertyName("reference-schema-name")]
	[property: Description("Required when type is Lookup. Use an entity schema name like Contact or Account.")]
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
						   Binary, Image, File, Blob, SecureText,
						   Text50, Text250, Text500, TextUnlimited, PhoneNumber, WebLink, Email, RichText, 
						   Decimal0, Decimal1, Decimal2, Decimal3, Decimal4, Decimal8, 
						   Currency0, Currency1, Currency2, Currency3.
						   Encrypted and Password are accepted as aliases for SecureText.
						   EmailAddress is accepted as an alias for Email.
						   """)]
	string? Type = null,

	[property: JsonPropertyName("title-localizations")]
	[property: Description("Column title/caption localizations. Required for add. Must include en-US when provided.")]
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
	[property: Description("Legacy scalar title. Not accepted by MCP. Use title-localizations instead.")]
	public string? LegacyTitle { get; init; }

	[property: JsonPropertyName("description")]
	[property: Description("Legacy scalar description. Not accepted by MCP. Use description-localizations instead.")]
	public string? LegacyDescription { get; init; }

	/// <summary>
	/// Gets the structured default value metadata used for non-legacy mutation scenarios.
	/// </summary>
	[property: JsonPropertyName("default-value-config")]
	[property: Description("Structured default value metadata. Settings value-source accepts code/name/id and resolves to code. SystemValue value-source accepts GUID/alias/caption and resolves to GUID.")]
	public EntitySchemaDefaultValueConfig? DefaultValueConfig { get; init; }
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
/// </summary>
public sealed record GetEntitySchemaPropertiesArgs(
	string EnvironmentName,
	string PackageName,
	string SchemaName
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
	[property: Description("Creatio environment name")]
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
	[property: Description("Creatio environment name")]
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
