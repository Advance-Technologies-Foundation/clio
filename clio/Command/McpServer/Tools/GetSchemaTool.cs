using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Consolidated MCP tool that reads a schema. Dispatches by <c>schema-type</c>.
/// For <c>schema-type=entity</c>, supplying <c>column</c> returns the column properties
/// instead of the full schema properties (folds the legacy <c>get-entity-schema-column-properties</c> tool).
/// </summary>
[McpServerToolType]
public sealed class GetSchemaTool {
	internal const string ToolName = "get-schema";

	private readonly SourceCodeSchemaGetLogic _sourceCode;
	private readonly GetClientUnitSchemaTool _clientUnit;
	private readonly SqlSchemaGetTool _sql;
	private readonly GetEntitySchemaPropertiesTool _entityProperties;
	private readonly GetEntitySchemaColumnPropertiesTool _entityColumnProperties;

	public GetSchemaTool(
		GetSourceCodeSchemaCommand sourceCodeCommand,
		ILogger logger,
		IToolCommandResolver commandResolver,
		GetClientUnitSchemaTool clientUnit,
		SqlSchemaGetTool sql,
		GetEntitySchemaPropertiesTool entityProperties,
		GetEntitySchemaColumnPropertiesTool entityColumnProperties) {
		_sourceCode = new SourceCodeSchemaGetLogic(sourceCodeCommand, logger, commandResolver);
		_clientUnit = clientUnit;
		_sql = sql;
		_entityProperties = entityProperties;
		_entityColumnProperties = entityColumnProperties;
	}

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Read a schema from a remote Creatio environment. Dispatches by schema-type: " +
		"'source-code' / 'client-unit' / 'sql' return the schema body + metadata; " +
		"'entity' returns structured schema properties (requires package-name + environment-name). " +
		"When schema-type='entity' and a non-empty `column` is provided, returns that column's properties " +
		"(folds the legacy get-entity-schema-column-properties tool). " +
		"`column` is only honored when schema-type='entity'; supplying it for other schema-types is an error.")]
	public object Get(
		[Description("Get-schema parameters. Required fields depend on schema-type.")] [Required]
		GetSchemaArgs args) {
		CommandExecutionResult modeError = CommandExecutionResult.ValidateExactlyOneMode(
			"schema-type", args.SchemaType,
			SchemaCreateTool.SchemaTypeSourceCode,
			SchemaCreateTool.SchemaTypeEntity,
			SchemaCreateTool.SchemaTypeClientUnit,
			SchemaCreateTool.SchemaTypeSql);
		if (modeError != null) {
			return modeError;
		}

		bool isEntity = string.Equals(args.SchemaType, SchemaCreateTool.SchemaTypeEntity, StringComparison.OrdinalIgnoreCase);
		if (!string.IsNullOrWhiteSpace(args.Column) && !isEntity) {
			return CommandExecutionResult.FromError(
				$"column is only supported when schema-type='entity'. Got schema-type='{args.SchemaType}'.");
		}

		if (string.Equals(args.SchemaType, SchemaCreateTool.SchemaTypeSourceCode, StringComparison.OrdinalIgnoreCase)) {
			return _sourceCode.Get(args);
		}
		if (string.Equals(args.SchemaType, SchemaCreateTool.SchemaTypeClientUnit, StringComparison.OrdinalIgnoreCase)) {
			return _clientUnit.GetSchema(new GetClientUnitSchemaArgs(args.SchemaName!) {
				OutputFile = args.OutputFile,
				EnvironmentName = args.EnvironmentName,
				Uri = args.Uri,
				Login = args.Login,
				Password = args.Password
			});
		}
		if (string.Equals(args.SchemaType, SchemaCreateTool.SchemaTypeSql, StringComparison.OrdinalIgnoreCase)) {
			return _sql.GetSchema(new SqlSchemaGetArgs(args.SchemaName!) {
				OutputFile = args.OutputFile,
				EnvironmentName = args.EnvironmentName,
				Uri = args.Uri,
				Login = args.Login,
				Password = args.Password
			});
		}
		// entity
		if (string.IsNullOrWhiteSpace(args.PackageName)) {
			return CommandExecutionResult.FromError(
				"package-name is required when schema-type='entity'.");
		}
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return CommandExecutionResult.FromError(
				"environment-name is required when schema-type='entity'.");
		}
		if (!string.IsNullOrWhiteSpace(args.Column)) {
			return _entityColumnProperties.GetEntitySchemaColumnProperties(new GetEntitySchemaColumnPropertiesArgs(
				args.EnvironmentName,
				args.PackageName,
				args.SchemaName!,
				args.Column));
		}
		return _entityProperties.GetEntitySchemaProperties(new GetEntitySchemaPropertiesArgs(
			args.EnvironmentName,
			args.PackageName,
			args.SchemaName!));
	}

	private sealed class SourceCodeSchemaGetLogic : BaseTool<GetSourceCodeSchemaOptions> {
		public SourceCodeSchemaGetLogic(
			GetSourceCodeSchemaCommand command,
			ILogger logger,
			IToolCommandResolver commandResolver)
			: base(command, logger, commandResolver) {
		}

		public GetSourceCodeSchemaResponse Get(GetSchemaArgs args) {
			GetSourceCodeSchemaOptions options = new() {
				SchemaName = args.SchemaName,
				OutputFile = args.OutputFile,
				Environment = args.EnvironmentName,
				Uri = args.Uri,
				Login = args.Login,
				Password = args.Password
			};
			return ExecuteWithCleanLog(() => {
				GetSourceCodeSchemaCommand resolvedCommand;
				try {
					resolvedCommand = ResolveCommand<GetSourceCodeSchemaCommand>(options);
				}
				catch (Exception ex) {
					return new GetSourceCodeSchemaResponse { Success = false, Error = ex.Message };
				}
				resolvedCommand.TryGetSchema(options, out GetSourceCodeSchemaResponse response);
				return response;
			});
		}
	}
}

/// <summary>
/// Consolidated arguments for the <c>get-schema</c> MCP tool.
/// </summary>
public sealed record GetSchemaArgs(
	[property: JsonPropertyName("schema-type")]
	[property: Description("Discriminator: 'source-code' | 'entity' | 'client-unit' | 'sql'. Selects the read flavor and required fields.")]
	[property: Required]
	string SchemaType,

	[property: JsonPropertyName("schema-name")]
	[property: Description("Schema name to read. Required for every schema-type.")]
	[property: Required]
	string? SchemaName = null,

	[property: JsonPropertyName("package-name")]
	[property: Description("Required when schema-type='entity'. Package on the target environment that owns the schema.")]
	string? PackageName = null,

	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name. Required for schema-type='entity'.")]
	string? EnvironmentName = null,

	[property: JsonPropertyName("column")]
	[property: Description("Optional, only honored when schema-type='entity'. When provided, returns properties for that column instead of the full schema. Folds the legacy get-entity-schema-column-properties tool.")]
	string? Column = null,

	[property: JsonPropertyName("output-file")]
	[property: Description("Optional absolute path to write the schema body to (schema-type=source-code/client-unit/sql). When set, body is omitted from the response.")]
	string? OutputFile = null,

	[property: JsonPropertyName("uri")]
	[property: Description("Direct Creatio URL. Use only when bootstrap is broken or before the environment can be registered through reg-web-app.")]
	string? Uri = null,

	[property: JsonPropertyName("login")]
	[property: Description("Direct Creatio login paired with `uri`. Emergency fallback only.")]
	string? Login = null,

	[property: JsonPropertyName("password")]
	[property: Description("Direct Creatio password paired with `uri`. Emergency fallback only.")]
	string? Password = null
);
