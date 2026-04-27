using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class SqlSchemaCreateTool(
	SqlSchemaCreateCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<SqlSchemaCreateOptions>(command, logger, commandResolver) {

	internal const string ToolName = "create-sql-schema";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[Description(
		"Create a new SQL script schema on a remote Creatio environment via ScriptSchemaDesignerService. " +
		"The schema is saved directly to the server — no local workspace files are created. " +
		"Prefer `environment-name`; keep direct connection args only for bootstrap flows.")]
	public SqlSchemaCreateResponse CreateSchema(
		[Description("Parameters: schema-name, package-name (required); caption, description (optional); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required]
		SqlSchemaCreateArgs args) {
		SqlSchemaCreateOptions options = new() {
			SchemaName = args.SchemaName,
			PackageName = args.PackageName,
			Caption = args.Caption,
			Description = args.Description,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		lock (CommandExecutionSyncRoot) {
			SqlSchemaCreateCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<SqlSchemaCreateCommand>(options);
			}
			catch (Exception ex) {
				return new SqlSchemaCreateResponse { Success = false, Error = ex.Message };
			}
			resolvedCommand.TryCreate(options, out SqlSchemaCreateResponse response);
			return response;
		}
	}
}

public sealed record SqlSchemaCreateArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("New SQL script schema name, e.g. 'UsrMySqlScript'. Must start with a letter; letters, digits and underscores only.")]
	[property: Required]
	string SchemaName,

	[property: JsonPropertyName("package-name")]
	[property: Description("Target package name that will own the new schema.")]
	[property: Required]
	string PackageName
) : SchemaCreateBaseArgs;
