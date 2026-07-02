using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Command.RelatedPages;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class GetRelatedPageAddonTool(
	GetRelatedPageAddonCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<GetRelatedPageAddonOptions>(command, logger, commandResolver) {

	internal const string ToolName = "get-related-page-addon";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Read an object's current RelatedPage configuration: which Freedom UI pages are bound as the default and the add page, per audience (role) and per record type. " +
		"Returns each entry's page-schema-uid + resolved page-schema-name, the role uid + resolved role-name (for the standard 'All employees' / 'All external users' audiences), the is-default / is-add / is-ssp-default flags, and any type-column-value, plus the top-level type-column-uid. " +
		"Read-only — makes no changes. Use this BEFORE create-related-page-addon for a safe read-modify-write: create REPLACES the whole configuration, so read the current pages first, modify, then send the full set back (otherwise the omitted entries are lost). " +
		"Prefer environment-name; keep direct connection args for emergency fallback only.")]
	public GetRelatedPageAddonResponse GetRelatedPageAddon(
		[Description("Parameters: entity-schema-name, package-name (required); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required] GetRelatedPageAddonArgs args) {
		if (string.IsNullOrWhiteSpace(args?.EntitySchemaName)) {
			return new GetRelatedPageAddonResponse { Success = false, Error = RelatedPageAddonMessages.EntitySchemaNameRequired };
		}
		if (string.IsNullOrWhiteSpace(args.PackageName)) {
			return new GetRelatedPageAddonResponse { Success = false, Error = RelatedPageAddonMessages.PackageNameRequired };
		}

		GetRelatedPageAddonOptions options = new() {
			EntitySchemaName = args.EntitySchemaName,
			PackageName = args.PackageName,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};

		return ExecuteWithCleanLog(() => {
			GetRelatedPageAddonCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<GetRelatedPageAddonCommand>(options);
			} catch (Exception ex) {
				return new GetRelatedPageAddonResponse { Success = false, Error = ex.Message };
			}
			resolvedCommand.TryGet(options, out GetRelatedPageAddonResponse response);
			return response;
		});
	}
}

public sealed record GetRelatedPageAddonArgs(
	[property: JsonPropertyName("entity-schema-name")]
	[property: Description("Object (entity schema) name whose related pages to read, e.g. 'UsrDeliveryItem'.")]
	[property: Required]
	string EntitySchemaName,

	[property: JsonPropertyName("package-name")]
	[property: Description("Package that owns the add-on configuration.")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name, e.g. 'local'. Preferred for normal MCP work.")]
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
