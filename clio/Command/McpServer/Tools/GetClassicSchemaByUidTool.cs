using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class GetClassicSchemaByUidTool(
	GetClassicSchemaByUidCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<GetClassicSchemaByUidOptions>(command, logger, commandResolver) {

	internal const string ToolName = "get-classic-schema-by-uid";

	// ReadOnly=false: the tool writes the schema body to disk when output-file is set.
	// Destructive stays false in line with the schema-read family because it does not mutate Creatio state.
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Read the JavaScript body and metadata of a single Classic client unit schema record by its UId. " +
		"Unlike get-client-unit-schema (resolves the top schema by NAME), this loads a specific schema by SysSchema.UId — " +
		"use it to read each schema record in a Classic schema's hierarchy (diff / businessRules / methods) during a Classic->Freedom migration. " +
		"Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows.")]
	public GetClassicSchemaByUidResponse GetSchema(
		[Description("Parameters: schema-uid (required); output-file (optional); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required]
		GetClassicSchemaByUidArgs args) {
		return ExecuteWithCleanLog(() => {
			if (args is null) {
				return new GetClassicSchemaByUidResponse { Success = false, Error = "args is required" };
			}
			GetClassicSchemaByUidOptions options = new() {
				SchemaUId = args.SchemaUId,
				OutputFile = args.OutputFile,
				Environment = args.EnvironmentName,
				Uri = args.Uri,
				Login = args.Login,
				Password = args.Password
			};
			GetClassicSchemaByUidCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<GetClassicSchemaByUidCommand>(options);
			}
			catch (Exception ex) {
				return new GetClassicSchemaByUidResponse { Success = false, Error = SensitiveErrorTextRedactor.Redact(ex.Message) };
			}
			resolvedCommand.TryGetSchema(options, out GetClassicSchemaByUidResponse response);
			if (!response.Success && !string.IsNullOrEmpty(response.Error))
				response.Error = SensitiveErrorTextRedactor.Redact(response.Error);
			return response;
		});
	}
}

public sealed record GetClassicSchemaByUidArgs(
	[property: JsonPropertyName("schema-uid")]
	[property: Description("Client unit schema UId (a specific schema's SysSchema.UId), e.g. '948080fc-031e-4d88-9239-47bcedaa92bc'")]
	[property: Required]
	string SchemaUId
) : SchemaGetBaseArgs;
