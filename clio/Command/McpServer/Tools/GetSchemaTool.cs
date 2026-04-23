using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class GetSchemaTool(
	GetSourceCodeSchemaCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<GetSourceCodeSchemaOptions>(command, logger, commandResolver) {

	internal const string ToolName = "get-schema";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Read the C# body and metadata of a source-code schema from a remote Creatio environment. " +
		"Use before update-schema to inspect current content. " +
		"Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows.")]
	public GetSourceCodeSchemaResponse GetSchema(
		[Description("Parameters: schema-name (required); output-file (optional); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required]
		GetSchemaArgs args) {
		GetSourceCodeSchemaOptions options = new() {
			SchemaName = args.SchemaName,
			OutputFile = args.OutputFile,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		lock (CommandExecutionSyncRoot) {
			GetSourceCodeSchemaCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<GetSourceCodeSchemaCommand>(options);
			}
			catch (Exception ex) {
				return new GetSourceCodeSchemaResponse { Success = false, Error = ex.Message };
			}
			resolvedCommand.TryGetSchema(options, out GetSourceCodeSchemaResponse response);
			return response;
		}
	}
}

public sealed record GetSchemaArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("C# source-code schema name, e.g. 'UsrMyHelper'")]
	[property: Required]
	string SchemaName
) : SchemaGetBaseArgs;
