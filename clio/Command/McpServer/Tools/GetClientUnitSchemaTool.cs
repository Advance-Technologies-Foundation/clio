using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class GetClientUnitSchemaTool(
	GetClientUnitSchemaCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<GetClientUnitSchemaOptions>(command, logger, commandResolver) {

	internal const string ToolName = "get-client-unit-schema";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Read the JavaScript body and metadata of a client unit schema from a remote Creatio environment. " +
		"Use before update-client-unit-schema to inspect current content. " +
		"Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows.")]
	public GetClientUnitSchemaResponse GetSchema(
		[Description("Parameters: schema-name (required); output-file (optional); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required]
		GetClientUnitSchemaArgs args) {
		GetClientUnitSchemaOptions options = new() {
			SchemaName = args.SchemaName,
			OutputFile = args.OutputFile,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		lock (CommandExecutionSyncRoot) {
			GetClientUnitSchemaCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<GetClientUnitSchemaCommand>(options);
			}
			catch (Exception ex) {
				return new GetClientUnitSchemaResponse { Success = false, Error = ex.Message };
			}
			resolvedCommand.TryGetSchema(options, out GetClientUnitSchemaResponse response);
			return response;
		}
	}
}

public sealed record GetClientUnitSchemaArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("Client unit schema name, e.g. 'NetworkUtilities'")]
	[property: Required]
	string SchemaName
) : SchemaGetBaseArgs;
