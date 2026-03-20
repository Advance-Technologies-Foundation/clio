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
	[property: Description("Creatio environment name")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("app-id")]
	[property: Description("Application ID (GUID). Optional filter.")]
	string? AppId = null,

	[property: JsonPropertyName("app-code")]
	[property: Description("Application code. Optional filter.")]
	string? AppCode = null
);

/// <summary>
/// MCP arguments for the <c>application-create</c> tool.
/// </summary>
public sealed record ApplicationCreateArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Creatio environment name")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("name")]
	[property: Description("Application name")]
	[property: Required]
	string Name,

	[property: JsonPropertyName("code")]
	[property: Description("Application code")]
	[property: Required]
	string Code,

	[property: JsonPropertyName("template-code")]
	[property: Description("Application template code")]
	[property: Required]
	string TemplateCode,

	[property: JsonPropertyName("icon-background")]
	[property: Description("Application icon background color in #RRGGBB format.")]
	[property: Required]
	string IconBackground,

	[property: JsonPropertyName("description")]
	[property: Description("Application description")]
	string? Description = null,

	[property: JsonPropertyName("icon-id")]
	[property: Description("Optional application icon identifier, or 'auto' to resolve a random icon.")]
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
