using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class SqlSchemaInstallTool(
	SqlSchemaInstallCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<SqlSchemaInstallOptions>(command, logger, commandResolver) {

	internal const string ToolName = "install-sql-schema";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description(
		"Execute a SQL script schema on a remote Creatio environment. " +
		"WARNING: executes raw SQL directly on the database. Irreversible. " +
		"Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows.")]
	public SqlSchemaInstallResponse InstallSchema(
		[Description("Parameters: schema-name (required); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required]
		SqlSchemaInstallArgs args) {
		SqlSchemaInstallOptions options = new() {
			SchemaName = args.SchemaName,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		return ExecuteWithCleanLog(options, () => {
			SqlSchemaInstallCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<SqlSchemaInstallCommand>(options);
			}
			catch (Exception ex) {
				return new SqlSchemaInstallResponse { Success = false, Error = SensitiveErrorTextRedactor.Redact(ex.Message) };
			}
			resolvedCommand.TryInstall(options, out SqlSchemaInstallResponse response);
			return response;
		});
	}
}

public sealed record SqlSchemaInstallArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("SQL script schema name to execute, e.g. 'UsrMySqlScript'")]
	[property: Required]
	string SchemaName,

	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	string? EnvironmentName,

	[property: JsonPropertyName("uri")]
	[property: Description(McpToolDescriptions.Uri)]
	string? Uri,

	[property: JsonPropertyName("login")]
	[property: Description(McpToolDescriptions.Login)]
	string? Login,

	[property: JsonPropertyName("password")]
	[property: Description(McpToolDescriptions.Password)]
	string? Password
);
