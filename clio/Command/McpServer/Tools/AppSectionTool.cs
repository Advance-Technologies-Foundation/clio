using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Consolidated MCP tool that creates, updates, deletes, or lists sections of an installed Creatio
/// application. Folds the legacy <c>create-app-section</c>, <c>update-app-section</c>,
/// <c>delete-app-section</c>, and <c>list-app-sections</c> tools.
/// </summary>
[McpServerToolType]
public sealed class AppSectionTool(
	ApplicationSectionCreateTool createTool,
	ApplicationSectionUpdateTool updateTool,
	ApplicationSectionDeleteTool deleteTool,
	ApplicationSectionGetListTool listTool) {

	internal const string ToolName = "app-section";
	internal const string ActionCreate = "create";
	internal const string ActionUpdate = "update";
	internal const string ActionDelete = "delete";
	internal const string ActionList = "list";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Creates, updates, deletes, or lists sections of a Creatio application. Required fields depend on action. action='create' requires caption; action='update' requires section-code plus at least one mutable field; action='delete' requires section-code; action='list' lists every section in the application.")]
	public async Task<object> Apply(
		[Description("app-section parameters")] [Required] AppSectionArgs args,
		global::ModelContextProtocol.Server.McpServer server,
		CancellationToken cancellationToken = default) {
		switch ((args.Action ?? string.Empty).ToLowerInvariant()) {
			case ActionCreate:
				return await createTool.ApplicationSectionCreate(
					new ApplicationSectionCreateArgs(
						args.EnvironmentName!,
						args.ApplicationCode!,
						args.Caption ?? string.Empty) {
						Description = args.Description,
						EntitySchemaName = args.EntitySchemaName,
						IconBackground = args.IconBackground!,
						WithMobilePages = args.WithMobilePages ?? false
					},
					server,
					cancellationToken);
			case ActionUpdate:
				return await updateTool.ApplicationSectionUpdate(
					new ApplicationSectionUpdateArgs(
						args.EnvironmentName!,
						args.ApplicationCode!,
						args.SectionCode!) {
						Caption = args.Caption,
						Description = args.Description,
						IconId = args.IconId,
						IconBackground = args.IconBackground
					},
					server,
					cancellationToken);
			case ActionDelete:
				return deleteTool.ApplicationSectionDelete(new ApplicationSectionDeleteArgs(
					args.EnvironmentName!,
					args.ApplicationCode!,
					args.SectionCode!,
					args.DeleteEntitySchema));
			case ActionList:
				return listTool.ApplicationSectionGetList(new ApplicationSectionGetListArgs(
					args.EnvironmentName!,
					args.ApplicationCode!));
			default:
				return CommandExecutionResult.ValidateExactlyOneMode(
					"action", args.Action, ActionCreate, ActionUpdate, ActionDelete, ActionList)!;
		}
	}
}

/// <summary>
/// Arguments for the consolidated <c>app-section</c> MCP tool.
/// </summary>
public sealed record AppSectionArgs(
	[property: JsonPropertyName("action")]
	[property: Description("Discriminator: 'create' | 'update' | 'delete' | 'list'. Required fields depend on action.")]
	[property: Required]
	string Action,

	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("application-code")]
	[property: Description("Installed application code.")]
	[property: Required]
	string ApplicationCode,

	[property: JsonPropertyName("section-code")]
	[property: Description("Section code. Required for action='update' and action='delete'.")]
	string? SectionCode = null,

	[property: JsonPropertyName("caption")]
	[property: Description("Section caption. Required for action='create'. Optional mutable field for action='update'.")]
	string? Caption = null,

	[property: JsonPropertyName("description")]
	[property: Description("Optional section description.")]
	string? Description = null,

	[property: JsonPropertyName("entity-schema-name")]
	[property: Description("Optional entity schema name. Honored only for action='create'.")]
	string? EntitySchemaName = null,

	[property: JsonPropertyName("icon-id")]
	[property: Description("Optional icon id. Honored for action='update'.")]
	string? IconId = null,

	[property: JsonPropertyName("icon-background")]
	[property: Description("Optional icon background colour. Honored for action='create' and action='update'.")]
	string? IconBackground = null,

	[property: JsonPropertyName("with-mobile-pages")]
	[property: Description("Optional flag (action='create' only) to also generate mobile pages.")]
	bool? WithMobilePages = null,

	[property: JsonPropertyName("delete-entity-schema")]
	[property: Description("Optional flag (action='delete' only) to also remove the backing entity schema.")]
	bool? DeleteEntitySchema = null
);
