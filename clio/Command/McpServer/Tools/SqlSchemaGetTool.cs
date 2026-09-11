using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class SqlSchemaGetTool(
	SqlSchemaGetCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<SqlSchemaGetOptions>(command, logger, commandResolver) {

	internal const string ToolName = "get-sql-schema";

	// ReadOnly=false: with output-file set the tool writes the schema body to disk (a side effect), so it must
	// not advertise readOnlyHint=true. Destructive stays false — the write is confined to a trusted workspace
	// anchor or the OS temp directory (OutputPathConfinement, symlinks resolved), rejected before any write
	// otherwise. ReadOnly is not consumed by ClioRing (it parses only {Resident, Destructive}), so this flip
	// changes no Ring-consumed contract.
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description(
		"Read the body and metadata of a SQL script schema from a remote Creatio environment. " +
		"Use before update-sql-schema to inspect current content. " +
		"Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows.")]
	public SqlSchemaGetResponse GetSchema(
		[Description("Parameters: schema-name (required); output-file (optional); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required]
		SqlSchemaGetArgs args) {
		SqlSchemaGetOptions options = new() {
			SchemaName = args.SchemaName,
			OutputFile = args.OutputFile,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		return ExecuteWithCleanLog(options, () => {
			SqlSchemaGetCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<SqlSchemaGetCommand>(options);
			}
			catch (Exception ex) {
				return new SqlSchemaGetResponse { Success = false, Error = SensitiveErrorTextRedactor.Redact(ex.Message) };
			}
			resolvedCommand.TryGetSchema(options, out SqlSchemaGetResponse response);
			return response;
		});
	}
}

public sealed record SqlSchemaGetArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("SQL script schema name, e.g. 'UsrMySqlScript'")]
	[property: Required]
	string SchemaName
) : SchemaGetBaseArgs;
