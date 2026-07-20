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

	// ReadOnly=false: the tool writes the schema body (or the full-hierarchy contract file) to disk when
	// output-file is set. Destructive stays false in line with the schema-read family.
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Read the JavaScript body and metadata of a client unit schema from a remote Creatio environment. " +
		"Use before update-client-unit-schema to inspect current content. A schema name that exists in several " +
		"packages resolves deterministically to the top (most-derived) layer. " +
		"Pass full-hierarchy=true to ALSO return the localizable strings merged across the whole inheritance/package " +
		"chain (with parentSchemaUId provenance); the body still reflects this schema's own top layer (the view is " +
		"not folded). " +
		"Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows.")]
	public GetClientUnitSchemaResponse GetSchema(
		[Description("Parameters: schema-name (required unless schema-uid is provided); output-file (optional); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required]
		GetClientUnitSchemaArgs args) {
		GetClientUnitSchemaOptions options = new() {
			SchemaName = args.SchemaName,
			OutputFile = args.OutputFile,
			FullHierarchy = args.FullHierarchy,
			SchemaUId = args.SchemaUId,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		return ExecuteWithCleanLog(options, () => {
			GetClientUnitSchemaCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<GetClientUnitSchemaCommand>(options);
			}
			catch (Exception ex) {
				return new GetClientUnitSchemaResponse { Success = false, Error = SensitiveErrorTextRedactor.Redact(ex.Message) };
			}
			resolvedCommand.TryGetSchema(options, out GetClientUnitSchemaResponse response);
			if (!string.IsNullOrEmpty(response?.Error)) {
				// The command's inner error can carry an HTTP/DataService message with the environment URI/host;
				// redact before it lands in the MCP transcript (parity with the resolution-failure path above).
				response.Error = SensitiveErrorTextRedactor.Redact(response.Error);
			}
			return response;
		});
	}
}

public sealed record GetClientUnitSchemaArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("Client unit schema name, e.g. 'NetworkUtilities'. Required unless schema-uid is provided.")]
	string SchemaName = null,
	[property: JsonPropertyName("full-hierarchy")]
	[property: Description("When true, also return the localizable strings merged across the full inheritance/" +
		"package hierarchy (with parentSchemaUId provenance). The body stays this schema's own top layer — the " +
		"merge folds localization and metadata, not the view. Default false.")]
	bool FullHierarchy = false,
	[property: JsonPropertyName("schema-uid")]
	[property: Description("Fetch this exact schema UId directly, bypassing name resolution — targets a " +
		"specific layer of a multi-layer classic schema deterministically. Default null.")]
	string SchemaUId = null
) : SchemaGetBaseArgs;
