using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Consolidated MCP tool that updates a schema body or, for entity schemas, applies a batch
/// of column operations. Dispatches by <c>schema-type</c>.
/// </summary>
[McpServerToolType]
public sealed class SchemaUpdateTool {
	internal const string ToolName = "update-schema";

	private readonly SourceCodeSchemaUpdateLogic _sourceCode;
	private readonly ClientUnitSchemaUpdateTool _clientUnit;
	private readonly SqlSchemaUpdateTool _sql;
	private readonly UpdateEntitySchemaTool _entity;

	public SchemaUpdateTool(
		SourceCodeSchemaUpdateCommand sourceCodeCommand,
		ILogger logger,
		IToolCommandResolver commandResolver,
		ClientUnitSchemaUpdateTool clientUnit,
		SqlSchemaUpdateTool sql,
		UpdateEntitySchemaTool entity) {
		_sourceCode = new SourceCodeSchemaUpdateLogic(sourceCodeCommand, logger, commandResolver);
		_clientUnit = clientUnit;
		_sql = sql;
		_entity = entity;
	}

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description(
		"Update a schema on a remote Creatio environment. Dispatches by schema-type: " +
		"'source-code' / 'client-unit' / 'sql' replace the schema body (provide body inline or body-file for large payloads); " +
		"'entity' applies a batch of add/modify/remove column operations via update-entity-schema. " +
		"Prefer environment-name; uri/login/password are emergency fallbacks only.")]
	public async Task<object> Update(
		[Description("Update-schema parameters. Required fields depend on schema-type.")] [Required]
		SchemaUpdateArgs args) {
		CommandExecutionResult modeError = CommandExecutionResult.ValidateExactlyOneMode(
			"schema-type", args.SchemaType,
			SchemaCreateTool.SchemaTypeSourceCode,
			SchemaCreateTool.SchemaTypeEntity,
			SchemaCreateTool.SchemaTypeClientUnit,
			SchemaCreateTool.SchemaTypeSql);
		if (modeError != null) {
			return modeError;
		}

		if (string.Equals(args.SchemaType, SchemaCreateTool.SchemaTypeSourceCode, StringComparison.OrdinalIgnoreCase)) {
			return _sourceCode.Update(args);
		}
		if (string.Equals(args.SchemaType, SchemaCreateTool.SchemaTypeClientUnit, StringComparison.OrdinalIgnoreCase)) {
			return _clientUnit.UpdateSchema(new ClientUnitSchemaUpdateArgs(
				args.SchemaName!,
				args.Body,
				args.BodyFile,
				args.DryRun,
				args.EnvironmentName,
				args.Uri,
				args.Login,
				args.Password));
		}
		if (string.Equals(args.SchemaType, SchemaCreateTool.SchemaTypeSql, StringComparison.OrdinalIgnoreCase)) {
			return _sql.UpdateSchema(new SqlSchemaUpdateArgs(
				args.SchemaName!,
				args.Body,
				args.BodyFile,
				args.DryRun,
				args.EnvironmentName,
				args.Uri,
				args.Login,
				args.Password));
		}
		// entity
		if (args.Operations is null) {
			return CommandExecutionResult.FromError(
				"operations is required when schema-type='entity'.");
		}
		UpdateEntitySchemaArgs entityArgs = new(
			args.EnvironmentName!,
			args.PackageName!,
			args.SchemaName!,
			args.Operations);
		return await _entity.UpdateEntitySchema(entityArgs);
	}

	private sealed class SourceCodeSchemaUpdateLogic : BaseTool<SourceCodeSchemaUpdateOptions> {
		public SourceCodeSchemaUpdateLogic(
			SourceCodeSchemaUpdateCommand command,
			ILogger logger,
			IToolCommandResolver commandResolver)
			: base(command, logger, commandResolver) {
		}

		public SourceCodeSchemaUpdateResponse Update(SchemaUpdateArgs args) {
			SourceCodeSchemaUpdateOptions options = new() {
				SchemaName = args.SchemaName,
				Body = args.Body,
				BodyFile = args.BodyFile,
				DryRun = args.DryRun ?? false,
				Environment = args.EnvironmentName,
				Uri = args.Uri,
				Login = args.Login,
				Password = args.Password
			};
			return ExecuteWithCleanLog(() => {
				SourceCodeSchemaUpdateCommand resolvedCommand;
				try {
					resolvedCommand = ResolveCommand<SourceCodeSchemaUpdateCommand>(options);
				}
				catch (Exception ex) {
					return new SourceCodeSchemaUpdateResponse { Success = false, Error = ex.Message };
				}
				resolvedCommand.TryUpdateSchema(options, out SourceCodeSchemaUpdateResponse response);
				return response;
			});
		}
	}
}

/// <summary>
/// Consolidated arguments for the <c>update-schema</c> MCP tool.
/// </summary>
public sealed record SchemaUpdateArgs(
	[property: JsonPropertyName("schema-type")]
	[property: Description("Discriminator: 'source-code' | 'entity' | 'client-unit' | 'sql'. Selects the update flavor and required fields.")]
	[property: Required]
	string SchemaType,

	[property: JsonPropertyName("schema-name")]
	[property: Description("Schema name to update. Required for every schema-type.")]
	[property: Required]
	string? SchemaName = null,

	[property: JsonPropertyName("package-name")]
	[property: Description("Required when schema-type='entity'. Package on the target environment that owns the schema.")]
	string? PackageName = null,

	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name. Required for schema-type='entity'.")]
	string? EnvironmentName = null,

	[property: JsonPropertyName("body")]
	[property: Description("Replacement body for schema-type=source-code/client-unit/sql. Optional when body-file is provided.")]
	string? Body = null,

	[property: JsonPropertyName("body-file")]
	[property: Description("Absolute path to a file whose contents replace the schema body. Recommended for large bodies. Takes precedence over body.")]
	string? BodyFile = null,

	[property: JsonPropertyName("dry-run")]
	[property: Description("If true (schema-type=source-code/client-unit/sql), validate and resolve the schema without saving. Default: false.")]
	bool? DryRun = null,

	[property: JsonPropertyName("operations")]
	[property: Description("Required when schema-type='entity'. Batch column operations to apply in order.")]
	IEnumerable<UpdateEntitySchemaOperationArgs>? Operations = null,

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
