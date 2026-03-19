using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
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
	IToolCommandResolver commandResolver)
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
	public CommandExecutionResult CreateEntitySchema(
		[Description("Create-entity-schema parameters")] [Required] CreateEntitySchemaArgs args
	) {
		CreateEntitySchemaOptions options = new() {
			Package = args.PackageName,
			SchemaName = args.SchemaName,
			Title = args.Title,
			ParentSchemaName = args.ParentSchemaName,
			ExtendParent = args.ExtendParent,
			Columns = SerializeColumns(args.Columns),
			Environment = args.EnvironmentName
		};
		return InternalExecute<CreateEntitySchemaCommand>(options);
	}

	internal static IEnumerable<string>? SerializeColumns(IEnumerable<CreateEntitySchemaColumnArgs>? columns) {
		return columns?
			.Select(SerializeColumn)
			.ToList();
	}

	private static string SerializeColumn(CreateEntitySchemaColumnArgs column) {
		if (RequiresStructuredSerialization(column)) {
			return JsonSerializer.Serialize(new Dictionary<string, object?> {
				["name"] = column.Name?.Trim(),
				["type"] = column.Type?.Trim(),
				["title"] = GetEffectiveColumnTitle(column),
				["reference-schema-name"] = string.IsNullOrWhiteSpace(column.ReferenceSchemaName)
					? null
					: column.ReferenceSchemaName.Trim(),
				["required"] = column.Required,
				["default-value-source"] = column.DefaultValueSource,
				["default-value"] = column.DefaultValue
			});
		}

		List<string?> segments = [column.Name?.Trim(), column.Type?.Trim()];
		if (!string.IsNullOrWhiteSpace(column.ReferenceSchemaName)) {
			segments.Add(GetEffectiveColumnTitle(column)?.Trim() ?? string.Empty);
			segments.Add(column.ReferenceSchemaName.Trim());
		} else if (!string.IsNullOrWhiteSpace(GetEffectiveColumnTitle(column))) {
			segments.Add(GetEffectiveColumnTitle(column)!.Trim());
		}
		return string.Join(":", segments);
	}

	private static string? GetEffectiveColumnTitle(CreateEntitySchemaColumnArgs column) {
		return !string.IsNullOrWhiteSpace(column.Title)
			? column.Title
			: column.Caption;
	}

	private static bool RequiresStructuredSerialization(CreateEntitySchemaColumnArgs column) {
		return column.Required.HasValue
			|| !string.IsNullOrWhiteSpace(column.DefaultValueSource)
			|| column.DefaultValue != null;
	}
}

/// <summary>
/// MCP tool surface for remote lookup schema creation.
/// </summary>
public sealed class CreateLookupTool(
	CreateEntitySchemaCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<CreateEntitySchemaOptions>(command, logger, commandResolver) {

	internal const string CreateLookupToolName = "create-lookup";
	private const string BaseLookupParentSchemaName = "BaseLookup";

	/// <summary>
	/// Creates a remote lookup schema in a package on the requested Creatio environment.
	/// </summary>
	[McpServerTool(Name = CreateLookupToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("""
				 Creates a remote lookup schema in an existing Creatio package through EntitySchemaDesignerService.
				 
				 The schema always inherits from BaseLookup. Use this when the caller explicitly requested a lookup
				 entity instead of a generic entity schema.
				 """)]
	public CommandExecutionResult CreateLookup(
		[Description("Create-lookup parameters")] [Required] CreateLookupArgs args
	) {
		CreateEntitySchemaOptions options = new() {
			Package = args.PackageName,
			SchemaName = args.SchemaName,
			Title = args.Title,
			ParentSchemaName = BaseLookupParentSchemaName,
			ExtendParent = false,
			Columns = CreateEntitySchemaTool.SerializeColumns(args.Columns),
			Environment = args.EnvironmentName
		};
		return InternalExecute<CreateEntitySchemaCommand>(options);
	}
}

/// <summary>
/// MCP tool surface for batch remote entity schema column mutations.
/// </summary>
public sealed class UpdateEntitySchemaTool(
	UpdateEntitySchemaCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<UpdateEntitySchemaOptions>(command, logger, commandResolver) {

	internal const string UpdateEntitySchemaToolName = "update-entity-schema";

	/// <summary>
	/// Applies a batch of add/modify/remove column operations to a remote entity schema.
	/// </summary>
	[McpServerTool(Name = UpdateEntitySchemaToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Applies a batch of add, modify, and remove column operations to a remote Creatio entity schema.")]
	public CommandExecutionResult UpdateEntitySchema(
		[Description("Update-entity-schema parameters")] [Required] UpdateEntitySchemaArgs args) {
		UpdateEntitySchemaOptions options = new() {
			Environment = args.EnvironmentName,
			Package = args.PackageName,
			SchemaName = args.SchemaName,
			Operations = args.Operations.Select(operation => JsonSerializer.Serialize(operation)).ToList()
		};
		return InternalExecute<UpdateEntitySchemaCommand>(options);
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
		[Description("Get-entity-schema-properties parameters")] [Required] GetEntitySchemaPropertiesArgs args) {
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
/// MCP tool surface for reading structured remote entity schema column properties.
/// </summary>
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
		[Description("Get-entity-schema-column-properties parameters")] [Required]
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
		[Description("Modify-entity-schema-column parameters")] [Required] ModifyEntitySchemaColumnArgs args) {
		ModifyEntitySchemaColumnOptions options = new() {
			Environment = args.EnvironmentName,
			Package = args.PackageName,
			SchemaName = args.SchemaName,
			Action = args.Action,
			ColumnName = args.ColumnName,
			NewName = args.NewName,
			Type = args.Type,
			Title = args.Title,
			Description = args.Description,
			ReferenceSchemaName = args.ReferenceSchemaName,
			Required = args.IsRequired,
			Indexed = args.Indexed,
			Cloneable = args.Cloneable,
			TrackChanges = args.TrackChanges,
			DefaultValueSource = args.DefaultValueSource,
			DefaultValue = args.DefaultValue,
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
	}
}

/// <summary>
/// Arguments for the <c>create-entity-schema</c> MCP tool.
/// </summary>
public sealed record CreateEntitySchemaArgs(
	[property: JsonPropertyName("package-name")]
	[property: Description("Target package name on the Creatio environment")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("schema-name")]
	[property: Description("Entity schema name. Maximum length is 22 characters.")]
	[property: Required]
	string SchemaName,

	[property: JsonPropertyName("title")]
	[property: Description("Entity schema title or caption")]
	[property: Required]
	string Title,

	[property: JsonPropertyName("environment-name")]
	[property: Description("Creatio environment name")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("parent-schema-name")]
	[property: Description("Optional parent schema name")]
	string? ParentSchemaName = null,

	[property: JsonPropertyName("extend-parent")]
	[property: Description("Create a replacement schema. Requires parent-schema-name.")]
	bool ExtendParent = false,

	[property: JsonPropertyName("columns")]
	[property: Description("Optional initial columns to add to the schema.")]
	IEnumerable<CreateEntitySchemaColumnArgs>? Columns = null
);

/// <summary>
/// Arguments for the <c>create-lookup</c> MCP tool.
/// </summary>
public sealed record CreateLookupArgs(
	[property: JsonPropertyName("package-name")]
	[property: Description("Target package name on the Creatio environment")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("schema-name")]
	[property: Description("Lookup schema name. Maximum length is 22 characters.")]
	[property: Required]
	string SchemaName,

	[property: JsonPropertyName("title")]
	[property: Description("Lookup schema title or caption")]
	[property: Required]
	string Title,

	[property: JsonPropertyName("environment-name")]
	[property: Description("Creatio environment name")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("columns")]
	[property: Description("Optional initial columns to add to the lookup schema.")]
	IEnumerable<CreateEntitySchemaColumnArgs>? Columns = null
);

/// <summary>
/// Arguments for the <c>update-entity-schema</c> MCP tool.
/// </summary>
public sealed record UpdateEntitySchemaArgs(
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

	[property: JsonPropertyName("operations")]
	[property: Description("Batch column operations to apply in order.")]
	[property: Required]
	IEnumerable<UpdateEntitySchemaOperationArgs> Operations
);

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
						  Integer, Float, Boolean, Date, DateTime, Time, Lookup.
						  """)]
	[property: Required]
	string Type,

	[property: JsonPropertyName("title")]
	[property: Description("Optional column title or caption")]
	string? Title = null,

	[property: JsonPropertyName("reference-schema-name")]
	[property: Description("Required when type is Lookup. Use an entity schema name like Contact or Account.")]
	string? ReferenceSchemaName = null
) {
	[property: JsonPropertyName("caption")]
	[property: Description("Optional alias for title when the caller uses caption terminology.")]
	public string? Caption { get; init; }

	[property: JsonPropertyName("required")]
	[property: Description("Optional required flag for the created column.")]
	public bool? Required { get; init; }

	[property: JsonPropertyName("default-value-source")]
	[property: Description("Optional default value source. Supported values: Const, None.")]
	public string? DefaultValueSource { get; init; }

	[property: JsonPropertyName("default-value")]
	[property: Description("Optional constant default value.")]
	public string? DefaultValue { get; init; }
}

/// <summary>
/// Structured operation input for the <c>update-entity-schema</c> MCP tool.
/// </summary>
public sealed record UpdateEntitySchemaOperationArgs(
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
	[property: Description("Column type for add operations or explicit type changes.")]
	string? Type = null,

	[property: JsonPropertyName("title")]
	[property: Description("Column title or caption")]
	string? Title = null,

	[property: JsonPropertyName("description")]
	[property: Description("Column description")]
	string? Description = null,

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
	[property: Description("Set a constant default value")]
	string? DefaultValue = null,

	[property: JsonPropertyName("default-value-source")]
	[property: Description("Default value source: Const or None")]
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
);

/// <summary>
/// Arguments for the <c>get-entity-schema-properties</c> MCP tool.
/// </summary>
public sealed record GetEntitySchemaPropertiesArgs(
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
/// Arguments for the <c>get-entity-schema-column-properties</c> MCP tool.
/// </summary>
public sealed record GetEntitySchemaColumnPropertiesArgs(
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

	[property: JsonPropertyName("column-name")]
	[property: Description("Column name")]
	[property: Required]
	string ColumnName
);

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
						   Text50, Text250, Text500, TextUnlimited, PhoneNumber, WebLink, Email, RichText, 
						   Decimal0, Decimal1, Decimal2, Decimal3, Decimal4, Decimal8, 
						   Currency0, Currency1, Currency2, Currency3
						   """)]
	string? Type = null,

	[property: JsonPropertyName("title")]
	[property: Description("Column title or caption")]
	string? Title = null,

	[property: JsonPropertyName("description")]
	[property: Description("Column description")]
	string? Description = null,

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
	[property: Description("Set a constant default value")]
	string? DefaultValue = null,

	[property: JsonPropertyName("default-value-source")]
	[property: Description("Default value source: Const or None")]
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
);
