using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class GetClassicSchemaTool(
	GetClassicSchemaCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<GetClassicSchemaOptions>(command, logger, commandResolver) {

	internal const string ToolName = "get-classic-schema";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Read the JavaScript body and metadata of a Classic client unit schema LAYER by its UId. " +
		"Unlike get-client-unit-schema (resolves the top schema by NAME), this loads a specific layer by SysSchema.UId — " +
		"use it to read every package layer of a Classic schema (diff / businessRules / methods) during a Classic->Freedom migration. " +
		"Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows.")]
	public GetClassicSchemaResponse GetSchema(
		[Description("Parameters: schema-uid (required); output-file (optional); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required]
		GetClassicSchemaArgs args) {
		GetClassicSchemaOptions options = new() {
			SchemaUId = args.SchemaUId,
			OutputFile = args.OutputFile,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		return ExecuteWithCleanLog(() => {
			GetClassicSchemaCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<GetClassicSchemaCommand>(options);
			}
			catch (Exception ex) {
				return new GetClassicSchemaResponse { Success = false, Error = SensitiveErrorTextRedactor.Redact(ex.Message) };
			}
			resolvedCommand.TryGetSchema(options, out GetClassicSchemaResponse response);
			if (!response.Success && !string.IsNullOrEmpty(response.Error))
				response.Error = SensitiveErrorTextRedactor.Redact(response.Error);
			return response;
		});
	}
}

public sealed record GetClassicSchemaArgs(
	[property: JsonPropertyName("schema-uid")]
	[property: Description("Client unit schema UId (a specific layer's SysSchema.UId), e.g. '948080fc-031e-4d88-9239-47bcedaa92bc'")]
	[property: Required]
	string SchemaUId
) : SchemaGetBaseArgs;
