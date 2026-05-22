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
public sealed class ClientUnitSchemaUpdateTool(
	ClientUnitSchemaUpdateCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<ClientUnitSchemaUpdateOptions>(command, logger, commandResolver) {

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
		return ExecuteWithCleanLog(() => {
			ClientUnitSchemaUpdateCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<ClientUnitSchemaUpdateCommand>(options);
			}
			catch (Exception ex) {
				return new ClientUnitSchemaUpdateResponse { Success = false, Error = ex.Message };
			}
			resolvedCommand.TryUpdateSchema(options, out ClientUnitSchemaUpdateResponse response);
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
	[property: Description("Registered clio environment name, e.g. 'dev_5001'. Preferred for normal MCP work.")]
	string? EnvironmentName,

	[property: JsonPropertyName("uri")]
	[property: Description("Direct Creatio URL. Use only when bootstrap is broken or before the environment can be registered through reg-web-app.")]
	string? Uri,

	[property: JsonPropertyName("login")]
	[property: Description("Direct Creatio login paired with `uri`. Emergency fallback only.")]
	string? Login,

	[property: JsonPropertyName("password")]
	[property: Description("Direct Creatio password paired with `uri`. Emergency fallback only.")]
	string? Password
);
