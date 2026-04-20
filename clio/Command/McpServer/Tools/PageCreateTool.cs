using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class PageCreateTool(
	PageCreateCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<PageCreateOptions>(command, logger, commandResolver) {

	internal const string ToolName = "create-page";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Create a new Freedom UI page schema from a supported template. Use `list-page-templates` first to discover valid template values. Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows.")]
	public PageCreateResponse CreatePage(
		[Description("Parameters: schema-name, template, package-name (required); caption, description, entity-schema-name, dry-run (optional); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required] PageCreateArgs args) {
		PageCreateOptions options = new() {
			SchemaName = args.SchemaName,
			Template = args.Template,
			PackageName = args.PackageName,
			Caption = args.Caption,
			Description = args.Description,
			EntitySchemaName = args.EntitySchemaName,
			DryRun = args.DryRun ?? false,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		PageCreateResponse response;
		lock (CommandExecutionSyncRoot) {
			PageCreateCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<PageCreateCommand>(options);
			} catch (Exception ex) {
				return new PageCreateResponse { Success = false, Error = ex.Message };
			}
			resolvedCommand.TryCreatePage(options, out response);
		}
		return response;
	}
}

public sealed record PageCreateArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("New page schema name, e.g. 'UsrMyApp_BlankPage'. Must start with a letter; letters, digits and underscores only.")]
	[property: Required]
	string SchemaName,

	[property: JsonPropertyName("template")]
	[property: Description("Template name or UId returned by list-page-templates, e.g. 'BlankPageTemplate'.")]
	[property: Required]
	string Template,

	[property: JsonPropertyName("package-name")]
	[property: Description("Target package name that will own the new page schema.")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("caption")]
	[property: Description("Optional display caption. Defaults to schema-name when omitted.")]
	string? Caption,

	[property: JsonPropertyName("description")]
	[property: Description("Optional schema description.")]
	string? Description,

	[property: JsonPropertyName("entity-schema-name")]
	[property: Description("Optional entity schema name to record in the new page dependencies.")]
	string? EntitySchemaName,

	[property: JsonPropertyName("dry-run")]
	[property: Description("If true, validate inputs and resolve references without creating the page. Default: false.")]
	bool? DryRun,

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
