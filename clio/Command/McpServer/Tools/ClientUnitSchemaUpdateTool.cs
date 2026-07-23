using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that updates the raw body of any client unit schema (classic 7x JS modules, mixins,
/// utilities, Freedom UI pages, etc.) via <c>ClientUnitSchemaDesignerService</c>,
/// bypassing Freedom UI-specific marker/bundle validation performed by <see cref="PageUpdateTool"/>.
/// </summary>
[McpServerToolType]
public sealed class ClientUnitSchemaUpdateTool(
	ClientUnitSchemaUpdateCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<ClientUnitSchemaUpdateOptions>(command, logger, commandResolver) {

	internal const string ToolName = "update-client-unit-schema";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description(
		"Update the raw body of any client unit schema (classic 7x JS mixins/modules/utilities or Freedom UI) " +
		"without Freedom UI bundle/marker validation. Use when the target is not a Freedom UI page schema, e.g. 'NetworkUtilities'. " +
		"A schema name that exists in several packages resolves deterministically to the top (most-derived) layer — " +
		"the same layer get-client-unit-schema reads. " +
		"Provide the body inline via `body` or, for large bodies, as an absolute file path via `body-file`. " +
		"Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows.")]
	public ClientUnitSchemaUpdateResponse UpdateSchema(
		[Description("Parameters: schema-name (required); one of body or body-file (required); dry-run (optional); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required]
		ClientUnitSchemaUpdateArgs args) {
		ClientUnitSchemaUpdateOptions options = new() {
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
			ClientUnitSchemaUpdateCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<ClientUnitSchemaUpdateCommand>(options);
			}
			catch (Exception ex) {
				return new ClientUnitSchemaUpdateResponse { Success = false, Error = SensitiveErrorTextRedactor.Redact(ex.Message) };
			}
			resolvedCommand.TryUpdateSchema(options, out ClientUnitSchemaUpdateResponse response);
			if (!string.IsNullOrEmpty(response?.Error)) {
				// The command's inner error can carry an HTTP/DataService message with the environment URI/host;
				// redact before it lands in the MCP transcript (parity with the get-* schema tools).
				response.Error = SensitiveErrorTextRedactor.Redact(response.Error);
			}
			return response;
		});
	}
}

/// <summary>Arguments for the <c>update-client-unit-schema</c> MCP tool.</summary>
public sealed record ClientUnitSchemaUpdateArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("Client unit schema name, e.g. 'NetworkUtilities'")]
	[property: Required]
	string SchemaName,

	[property: JsonPropertyName("body")]
	[property: Description("Full raw JavaScript body to save as the schema body. Optional when body-file is provided.")]
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
