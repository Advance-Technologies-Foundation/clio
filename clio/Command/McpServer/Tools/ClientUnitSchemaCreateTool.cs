using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class ClientUnitSchemaCreateTool(
	ClientUnitSchemaCreateCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<ClientUnitSchemaCreateOptions>(command, logger, commandResolver) {

	internal const string ToolName = "create-client-unit-schema";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[Description("Create a new JavaScript (ClientUnit) schema on a remote Creatio environment. Use this for utility/helper JS modules — not Freedom UI pages (use create-page for those). Prefer `environment-name`; keep direct connection args only for bootstrap flows.")]
	public ClientUnitSchemaCreateResponse CreateClientUnitSchema(
		[Description("Parameters: schema-name, package-name (required); caption, description (optional); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required] ClientUnitSchemaCreateArgs args) {
		ClientUnitSchemaCreateOptions options = new() {
			SchemaName = args.SchemaName,
			PackageName = args.PackageName,
			Caption = args.Caption,
			Description = args.Description,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		ClientUnitSchemaCreateResponse response;
		lock (CommandExecutionSyncRoot) {
			ClientUnitSchemaCreateCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<ClientUnitSchemaCreateCommand>(options);
			} catch (Exception ex) {
				return new ClientUnitSchemaCreateResponse { Success = false, Error = ex.Message };
			}
			resolvedCommand.TryCreate(options, out response);
		}
		return response;
	}
}

public sealed record ClientUnitSchemaCreateArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("New JavaScript schema name, e.g. 'UsrMyHelper'. Must start with a letter; letters, digits and underscores only.")]
	[property: Required]
	string SchemaName,

	[property: JsonPropertyName("package-name")]
	[property: Description("Target package name that will own the new schema.")]
	[property: Required]
	string PackageName
) : SchemaCreateBaseArgs;
