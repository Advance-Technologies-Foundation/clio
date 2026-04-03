using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP arguments for the <c>application-get-list</c> tool.
/// </summary>
public sealed record ApplicationGetListArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Creatio environment name")]
	[property: Required]
	string? EnvironmentName = null
);

/// <summary>
/// MCP arguments for the <c>application-get-info</c> tool.
/// </summary>
public sealed record ApplicationGetInfoArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name, e.g. 'local'")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("id")]
	[property: Description("Application ID (GUID). Optional filter.")]
	string? Id = null,

	[property: JsonPropertyName("code")]
	[property: Description("Application code, e.g. 'UsrMyApp'. Optional filter.")]
	string? Code = null
);

/// <summary>
/// MCP arguments for the <c>application-create</c> tool.
/// </summary>
public sealed record ApplicationCreateArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name, e.g. 'local'")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("name")]
	[property: Description("Application display name, e.g. 'My App'")]
	[property: Required]
	string Name,

	[property: JsonPropertyName("code")]
	[property: Description("Application code starting with 'Usr' prefix, e.g. 'UsrMyApp'")]
	[property: Required]
	string Code,

	[property: JsonPropertyName("template-code")]
	[property: Description("Technical template name (NOT display name). Known values: AppFreedomUI, AppFreedomUIv2, AppWithHomePage, EmptyApp")]
	[property: Required]
	string TemplateCode,

	[property: JsonPropertyName("icon-background")]
	[property: Description("Application icon background color in #RRGGBB format, e.g. '#1F5F8B'")]
	[property: Required]
	string IconBackground,

	[property: JsonPropertyName("description")]
	[property: Description("Application description")]
	string? Description = null,

	[property: JsonPropertyName("icon-id")]
	[property: Description("Optional application icon GUID (e.g. '00000000-0000-0000-0000-000000000000'), or 'auto' to resolve a random icon.")]
	string? IconId = null,

	[property: JsonPropertyName("client-type-id")]
	[property: Description("Optional client type identifier")]
	string? ClientTypeId = null,

	[property: JsonPropertyName("optional-template-data-json")]
	[property: Description("Optional JSON object: {useExistingEntitySchema, entitySchemaName, appSectionDescription, useAIContentGeneration}")]
	string? OptionalTemplateDataJson = null
);

/// <summary>
/// Optional CreateApp template data for the <c>application-create</c> tool.
/// </summary>
public sealed record ApplicationOptionalTemplateDataJsonArgs(
	string? EntitySchemaName = null,
	bool? UseExistingEntitySchema = null,
	bool? UseAiContentGeneration = null,
	string? AppSectionDescription = null);
