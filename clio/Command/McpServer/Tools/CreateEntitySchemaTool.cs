using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for remote entity schema creation.
/// </summary>
public class CreateEntitySchemaTool(
	CreateEntitySchemaCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<CreateEntitySchemaOptions>(command, logger, commandResolver) {

	/// <summary>
	/// Creates a remote entity schema in a package on the requested Creatio environment.
	/// </summary>
	[McpServerTool(Name = "create-entity-schema", ReadOnly = false, Destructive = false, Idempotent = false,
		OpenWorld = false)]
	[Description("""
				 Creates a remote entity schema in an existing Creatio package through EntitySchemaDesignerService.
				 
				 Use this when the schema should be created directly on the target environment instead of generating
				 local source files. The package must already exist on the target environment.
				 """)]
	public CommandExecutionResult CreateEntitySchema(
		[Description("create-entity-schema parameters")] [Required] CreateEntitySchemaArgs args
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

	private static IEnumerable<string> SerializeColumns(IEnumerable<CreateEntitySchemaColumnArgs> columns) {
		return columns?
			.Select(SerializeColumn)
			.ToList();
	}

	private static string SerializeColumn(CreateEntitySchemaColumnArgs column) {
		List<string> segments = [column.Name?.Trim(), column.Type?.Trim()];
		if (!string.IsNullOrWhiteSpace(column.ReferenceSchemaName)) {
			segments.Add(column.Title?.Trim() ?? string.Empty);
			segments.Add(column.ReferenceSchemaName.Trim());
		} else if (!string.IsNullOrWhiteSpace(column.Title)) {
			segments.Add(column.Title.Trim());
		}
		return string.Join(":", segments);
	}
}

/// <summary>
/// Arguments for the <c>create-entity-schema</c> MCP tool.
/// </summary>
public record CreateEntitySchemaArgs(
	[property:JsonPropertyName("package-name")]
	[Description("Target package name on the Creatio environment")]
	[Required]
	string PackageName,

	[property:JsonPropertyName("schema-name")]
	[Description("Entity schema name. Maximum length is 22 characters.")]
	[Required]
	string SchemaName,

	[property:JsonPropertyName("title")]
	[Description("Entity schema title/caption")]
	[Required]
	string Title,

	[property:JsonPropertyName("environment-name")]
	[Description("Creatio environment name")]
	[Required]
	string EnvironmentName,

	[property:JsonPropertyName("parent-schema-name")]
	[Description("Optional parent schema name")]
	string ParentSchemaName = null,

	[property:JsonPropertyName("extend-parent")]
	[Description("Create a replacement schema. Requires parent-schema-name.")]
	bool ExtendParent = false,

	[property:JsonPropertyName("columns")]
	[Description("Optional initial columns to add to the schema.")]
	IEnumerable<CreateEntitySchemaColumnArgs> Columns = null
);

/// <summary>
/// Structured column input for the <c>create-entity-schema</c> MCP tool.
/// </summary>
public record CreateEntitySchemaColumnArgs(
	[property:JsonPropertyName("name")]
	[Description("Column name")]
	[Required]
	string Name,

	[property:JsonPropertyName("type")]
	[Description("Column type. Supported values: Guid, Text, Integer, Boolean, DateTime, Lookup.")]
	[Required]
	string Type,

	[property:JsonPropertyName("title")]
	[Description("Optional column title/caption")]
	string Title = null,

	[property:JsonPropertyName("reference-schema-name")]
	[Description("Required when type is Lookup. Use an entity schema name like Contact or Account.")]
	string ReferenceSchemaName = null
);
