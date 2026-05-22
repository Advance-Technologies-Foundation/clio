using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Consolidated MCP tool that creates a schema of the requested type. Dispatches by
/// <c>schema-type</c> to the underlying per-type tool implementation. Per-type tools
/// keep their public methods (and CLI verbs) so the underlying logic is reused
/// verbatim — only the MCP entry point is unified.
/// </summary>
[McpServerToolType]
public sealed class SchemaCreateTool {
	internal const string ToolName = "create-schema";

	internal const string SchemaTypeSourceCode = "source-code";
	internal const string SchemaTypeEntity = "entity";
	internal const string SchemaTypeLookup = "lookup";
	internal const string SchemaTypeClientUnit = "client-unit";
	internal const string SchemaTypeSql = "sql";

	private readonly SourceCodeSchemaCreateLogic _sourceCode;
	private readonly ClientUnitSchemaCreateTool _clientUnit;
	private readonly SqlSchemaCreateTool _sql;
	private readonly CreateEntitySchemaTool _entity;
	private readonly CreateLookupTool _lookup;

	public SchemaCreateTool(
		SourceCodeSchemaCreateCommand sourceCodeCommand,
		ILogger logger,
		IToolCommandResolver commandResolver,
		ClientUnitSchemaCreateTool clientUnit,
		SqlSchemaCreateTool sql,
		CreateEntitySchemaTool entity,
		CreateLookupTool lookup) {
		_sourceCode = new SourceCodeSchemaCreateLogic(sourceCodeCommand, logger, commandResolver);
		_clientUnit = clientUnit;
		_sql = sql;
		_entity = entity;
		_lookup = lookup;
	}

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description(
		"Create a schema on a remote Creatio environment. Dispatches by schema-type: " +
		"'source-code' creates a C# source-code schema; 'client-unit' creates a JavaScript client unit schema; " +
		"'sql' creates a SQL script schema; 'entity' creates an entity schema (requires title-localizations and " +
		"optional columns/parent-schema-name); 'lookup' creates a BaseLookup-derived schema. " +
		"Prefer environment-name; uri/login/password are emergency fallbacks only.")]
	public async Task<object> Create(
		[Description("Create-schema parameters. Required fields depend on schema-type.")] [Required]
		SchemaCreateArgs args) {
		CommandExecutionResult modeError = CommandExecutionResult.ValidateExactlyOneMode(
			"schema-type", args.SchemaType,
			SchemaTypeSourceCode, SchemaTypeEntity, SchemaTypeLookup, SchemaTypeClientUnit, SchemaTypeSql);
		if (modeError != null) {
			return modeError;
		}

		if (string.Equals(args.SchemaType, SchemaTypeSourceCode, StringComparison.OrdinalIgnoreCase)) {
			return _sourceCode.Create(args);
		}
		if (string.Equals(args.SchemaType, SchemaTypeClientUnit, StringComparison.OrdinalIgnoreCase)) {
			return _clientUnit.CreateClientUnitSchema(new ClientUnitSchemaCreateArgs(
				args.SchemaName!, args.PackageName!) {
				Caption = args.Caption,
				Description = args.Description,
				EnvironmentName = args.EnvironmentName,
				Uri = args.Uri,
				Login = args.Login,
				Password = args.Password
			});
		}
		if (string.Equals(args.SchemaType, SchemaTypeSql, StringComparison.OrdinalIgnoreCase)) {
			return _sql.CreateSchema(new SqlSchemaCreateArgs(
				args.SchemaName!, args.PackageName!) {
				Caption = args.Caption,
				Description = args.Description,
				EnvironmentName = args.EnvironmentName,
				Uri = args.Uri,
				Login = args.Login,
				Password = args.Password
			});
		}
		if (string.Equals(args.SchemaType, SchemaTypeEntity, StringComparison.OrdinalIgnoreCase)) {
			CreateEntitySchemaArgs entityArgs = new(
				args.PackageName!,
				args.SchemaName!,
				args.TitleLocalizations ?? new Dictionary<string, string>(),
				args.EnvironmentName!,
				args.ParentSchemaName,
				args.ExtendParent ?? false,
				args.Columns);
			return await _entity.CreateEntitySchema(entityArgs);
		}
		// lookup
		CreateLookupArgs lookupArgs = new(
			args.PackageName!,
			args.SchemaName!,
			args.TitleLocalizations ?? new Dictionary<string, string>(),
			args.EnvironmentName!,
			args.Columns);
		return await _lookup.CreateLookup(lookupArgs);
	}

	/// <summary>
	/// Source-code create logic, kept as an internal helper so the consolidated tool can dispatch
	/// without taking a dependency on a separate tool class.
	/// </summary>
	private sealed class SourceCodeSchemaCreateLogic : BaseTool<SourceCodeSchemaCreateOptions> {
		public SourceCodeSchemaCreateLogic(
			SourceCodeSchemaCreateCommand command,
			ILogger logger,
			IToolCommandResolver commandResolver)
			: base(command, logger, commandResolver) {
		}

		public SourceCodeSchemaCreateResponse Create(SchemaCreateArgs args) {
			SourceCodeSchemaCreateOptions options = new() {
				SchemaName = args.SchemaName,
				PackageName = args.PackageName,
				Caption = args.Caption,
				Description = args.Description,
				Environment = args.EnvironmentName,
				Uri = args.Uri,
				Login = args.Login,
				Password = args.Password
			};
			return ExecuteWithCleanLog(() => {
				SourceCodeSchemaCreateCommand resolvedCommand;
				try {
					resolvedCommand = ResolveCommand<SourceCodeSchemaCreateCommand>(options);
				} catch (Exception ex) {
					return new SourceCodeSchemaCreateResponse { Success = false, Error = ex.Message };
				}
				resolvedCommand.TryCreate(options, out SourceCodeSchemaCreateResponse response);
				return response;
			});
		}
	}
}

/// <summary>
/// Consolidated arguments for the <c>create-schema</c> MCP tool. The required and meaningful
/// fields depend on <c>schema-type</c>.
/// </summary>
public sealed record SchemaCreateArgs(
	[property: JsonPropertyName("schema-type")]
	[property: Description("Discriminator: 'source-code' | 'entity' | 'lookup' | 'client-unit' | 'sql'. " +
		"Selects the create flavor and required fields.")]
	[property: Required]
	string SchemaType,

	[property: JsonPropertyName("schema-name")]
	[property: Description("Schema name. Required for every schema-type. Must start with a letter; " +
		"letters, digits and underscores only. Entity/lookup names must respect the active schema-name prefix.")]
	[property: Required]
	string? SchemaName = null,

	[property: JsonPropertyName("package-name")]
	[property: Description("Target package name that will own the new schema. Required for every schema-type.")]
	[property: Required]
	string? PackageName = null,

	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name. Preferred for normal MCP work. Required for entity/lookup.")]
	string? EnvironmentName = null,

	[property: JsonPropertyName("caption")]
	[property: Description("Optional display caption. Honored for schema-type=source-code/client-unit/sql; ignored for entity/lookup (use title-localizations).")]
	string? Caption = null,

	[property: JsonPropertyName("description")]
	[property: Description("Optional schema description. Honored for schema-type=source-code/client-unit/sql.")]
	string? Description = null,

	[property: JsonPropertyName("title-localizations")]
	[property: Description("Required when schema-type=entity or schema-type=lookup. Title/caption localizations; must include en-US.")]
	Dictionary<string, string>? TitleLocalizations = null,

	[property: JsonPropertyName("columns")]
	[property: Description("Optional initial columns for schema-type=entity or schema-type=lookup. Column codes must respect the active schema-name prefix.")]
	IEnumerable<CreateEntitySchemaColumnArgs>? Columns = null,

	[property: JsonPropertyName("parent-schema-name")]
	[property: Description("Optional for schema-type=entity. Parent entity schema name; defaults to 'BaseEntity' when extend-parent is false. Ignored for schema-type=lookup (always BaseLookup).")]
	string? ParentSchemaName = null,

	[property: JsonPropertyName("extend-parent")]
	[property: Description("Optional for schema-type=entity. When true, requires parent-schema-name and extends the existing parent schema rather than creating a fresh hierarchy. Ignored for schema-type=lookup.")]
	bool? ExtendParent = null,

	[property: JsonPropertyName("uri")]
	[property: Description("Direct Creatio URL. Use only for bootstrap or before environment registration.")]
	string? Uri = null,

	[property: JsonPropertyName("login")]
	[property: Description("Direct Creatio login paired with `uri`. Emergency fallback only.")]
	string? Login = null,

	[property: JsonPropertyName("password")]
	[property: Description("Direct Creatio password paired with `uri`. Emergency fallback only.")]
	string? Password = null
);
