using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Command;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class SchemaUpdateTool(
	SourceCodeSchemaUpdateCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<SourceCodeSchemaUpdateOptions>(command, logger, commandResolver) {

	internal const string ToolName = "update-schema";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description(
		"Update the body of a C# source-code schema on a remote Creatio environment via SourceCodeSchemaDesignerService. " +
		"Provide the body inline via `body` or, for large bodies, as an absolute file path via `body-file`. " +
		"Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows.")]
	public SourceCodeSchemaUpdateResponse UpdateSchema(
		[Description("Parameters: schema-name (required); one of body or body-file (required); dry-run (optional); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required]
		SchemaUpdateArgs args) {
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
		return ExecuteWithCleanLog(options, () => {
			SourceCodeSchemaUpdateCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<SourceCodeSchemaUpdateCommand>(options);
			}
			catch (Exception ex) {
				return new SourceCodeSchemaUpdateResponse { Success = false, Error = SensitiveErrorTextRedactor.Redact(ex.Message) };
			}
			resolvedCommand.TryUpdateSchema(options, out SourceCodeSchemaUpdateResponse response);
			return response;
		});
	}
}

public sealed record SchemaUpdateArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("C# source-code schema name, e.g. 'UsrMyHelper'")]
	[property: Required]
	string SchemaName,

	[property: JsonPropertyName("body")]
	[property: Description("Full C# body to save as the schema body. Optional when body-file is provided.")]
	string? Body,

	[property: JsonPropertyName("body-file")]
	[property: Description("Absolute path to a file whose contents are used as the new schema body. Recommended for large bodies (over a few KB). Takes precedence over body when both are provided.")]
	string? BodyFile,

	[property: JsonPropertyName("dry-run")]
	[property: Description("If true, validate and resolve the schema without saving. Default: false")]
	bool? DryRun,

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
