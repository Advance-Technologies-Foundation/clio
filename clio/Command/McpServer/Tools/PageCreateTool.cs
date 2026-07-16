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
	[Description("Create a new Freedom UI page schema from a supported template. Use `list-page-templates` first to discover valid template values. Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows. " +
		"To create a DASHBOARD, use `template` `BaseDashboardTemplate` and pass its link-back properties through `optional-properties` (DashboardsEntitySchemaName / DashboardsElementName / DashboardsClientUnitSchemaUId) — call get-guidance with name `dashboard-creation` FIRST to learn how to retrieve each value (including the root-schema UId rule). " +
		"Page business rules (conditional visibility/editability/required) are separate artifacts — call get-guidance with name business-rules to learn more.")]
	public PageCreateResponse CreatePage(
		[Description("Parameters: schema-name, template, package-name (required); caption, description, entity-schema-name, optional-properties (optional); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required] PageCreateArgs args) {
		PageCreateOptions options = new() {
			SchemaName = args.SchemaName,
			Template = args.Template,
			PackageName = args.PackageName,
			Caption = args.Caption,
			Description = args.Description,
			EntitySchemaName = args.EntitySchemaName,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password,
			CaptionCulture = args.CaptionCulture,
			OptionalProperties = args.OptionalProperties
		};
		if (string.IsNullOrWhiteSpace(options.SchemaName)) {
			return new PageCreateResponse {
				Success = false,
				Error = "schema-name is required"
			};
		}
		if (!PageSchemaMetadataHelper.IsValidSchemaName(options.SchemaName)) {
			return new PageCreateResponse {
				Success = false,
				Error = PageSchemaMetadataHelper.SchemaNameFormatError
			};
		}
		return ExecuteWithCleanLog(options, () => {
			PageCreateCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<PageCreateCommand>(options);
			} catch (Exception ex) {
				return new PageCreateResponse { Success = false, Error = SensitiveErrorTextRedactor.Redact(ex.Message) };
			}
			resolvedCommand.TryCreatePage(options, out PageCreateResponse response);
			if (response is { Success: true }) {
				response.Note = CommandExecutionResult.CompileNotRequiredNote;
			}
			return response;
		});
	}
}

public sealed record PageCreateArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("New page schema name, e.g. 'UsrMyApp_BlankPage'. Must start with a letter; letters, digits and underscores only. " +
		"Must use the active SchemaNamePrefix as prefix (e.g. 'UsrAlpha_FormPage' when prefix is 'Usr', 'MyPrefixAlpha_FormPage' when prefix is 'MyPrefix'). " +
		"When `schema-name-prefix` is empty, use plain PascalCase with no prefix (e.g. 'Alpha_FormPage'). " +
		"Read the prefix from the `schema-name-prefix` field returned by `get-app-info`, " +
		"or call `get-schema-name-prefix` if you have not called `get-app-info` yet.")]
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
	string? Password,

	[property: JsonPropertyName("caption-culture")]
	[property: Description("Optional culture override for the page caption (e.g. 'en-US', 'uk-UA'). Precedence: caption-culture > detected profile culture > en-US. Skips the profile-culture lookup.")]
	string? CaptionCulture = null,

	[property: JsonPropertyName("optional-properties")]
	[property: Description("Optional JSON array of {key, value} objects to seed into the new schema optionalProperties, e.g. '[{\"key\":\"DashboardsEntitySchemaName\",\"value\":\"UsrMyEntity\"}]'. Used to create a dashboard (template BaseDashboardTemplate): set DashboardsEntitySchemaName, DashboardsElementName, DashboardsClientUnitSchemaUId — read get-guidance name `dashboard-creation` for how to obtain each value.")]
	string? OptionalProperties = null
);
