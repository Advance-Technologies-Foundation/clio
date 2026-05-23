using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class SqlSchemaUpdateTool(
	SqlSchemaUpdateCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<SqlSchemaUpdateOptions>(command, logger, commandResolver) {

	internal const string ToolName = "update-sql-schema";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description(
		"Update the body of a SQL script schema on a remote Creatio environment via ScriptSchemaDesignerService. " +
		"Provide the body inline via `body` or, for large bodies, as an absolute file path via `body-file`. " +
		"Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows.")]
	public SqlSchemaUpdateResponse UpdateSchema(
		[Description("Parameters: schema-name (required); one of body or body-file (required); dry-run (optional); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required]
		SqlSchemaUpdateArgs args) {
		SqlSchemaUpdateOptions options = new() {
			SchemaName = args.SchemaName,
			Body = args.Body,
			BodyFile = args.BodyFile,
			DryRun = args.DryRun ?? false,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		lock (CommandExecutionSyncRoot) {
			SqlSchemaUpdateCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<SqlSchemaUpdateCommand>(options);
			}
			catch (Exception ex) {
				return new SqlSchemaUpdateResponse { Success = false, Error = ex.Message };
			}
			resolvedCommand.TryUpdateSchema(options, out SqlSchemaUpdateResponse response);
			return response;
		}
	}
}

public sealed record SqlSchemaUpdateArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("SQL script schema name, e.g. 'UsrMySqlScript'")]
	[property: Required]
	string SchemaName,

	[property: JsonPropertyName("body")]
	[property: Description("Full SQL body to save as the schema body. Optional when body-file is provided.")]
	string? Body,

	[property: JsonPropertyName("body-file")]
	[property: Description("Absolute path to a file whose contents are used as the new schema body. Recommended for large bodies. Takes precedence over body when both are provided.")]
	string? BodyFile,

	[property: JsonPropertyName("dry-run")]
	[property: Description("If true, validate and resolve the schema without saving. Default: false")]
	bool? DryRun,

	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name, e.g. 'dev_5001'. Preferred for normal MCP work.")]
	string? EnvironmentName,

	[property: JsonPropertyName("uri")]
	[property: Description("Direct Creatio URL. Use only when bootstrap is broken or before the environment can be registered through reg-web-app.")]
	string? Uri,

	[property: JsonPropertyName("login")]
	[property: Description("Direct Creatio login paired with `uri`. Emergency fallback only.")]
	string? Login,

	[property: JsonPropertyName("password")]
	[property: Description("Direct Creatio password paired with `uri`. Emergency fallback only.")]
	string? Password
);
