using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP arguments for the <c>list-apps</c> tool.
/// </summary>
public sealed record ApplicationGetListArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Creatio environment name")]
	[property: Required]
	string? EnvironmentName = null
);

/// <summary>
/// MCP arguments for the <c>get-app-info</c> tool.
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
/// MCP arguments for the <c>create-app</c> tool.
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
	string? OptionalTemplateDataJson = null,

	[property: JsonPropertyName("title-localizations")]
	[property: Description("Rejected. create-app is scalar-only and does not accept localization maps.")]
	IReadOnlyDictionary<string, string>? TitleLocalizations = null,

	[property: JsonPropertyName("description-localizations")]
	[property: Description("Rejected. create-app is scalar-only and does not accept localization maps.")]
	IReadOnlyDictionary<string, string>? DescriptionLocalizations = null,

	[property: JsonPropertyName("caption-localizations")]
	[property: Description("Rejected. create-app is scalar-only and does not accept localization maps.")]
	IReadOnlyDictionary<string, string>? CaptionLocalizations = null,

	[property: JsonPropertyName("name-localizations")]
	[property: Description("Rejected. create-app is scalar-only and does not accept localization maps.")]
	IReadOnlyDictionary<string, string>? NameLocalizations = null
);

/// <summary>
/// Optional CreateApp template data for the <c>create-app</c> tool.
/// </summary>
public sealed record ApplicationOptionalTemplateDataJsonArgs(
	string? EntitySchemaName = null,
	bool? UseExistingEntitySchema = null,
	bool? UseAiContentGeneration = null,
	string? AppSectionDescription = null);

/// <summary>
/// MCP arguments for the <c>create-app-section</c> tool.
/// </summary>
public sealed record ApplicationSectionCreateArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name, e.g. 'local'")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("application-code")]
	[property: Description("Installed application code.")]
	[property: Required]
	string ApplicationCode,

	[property: JsonPropertyName("caption")]
	[property: Description("Section caption, e.g. 'Orders'")]
	[property: Required]
	string Caption = "",

	[property: JsonPropertyName("description")]
	[property: Description("Optional section description")]
	string? Description = null,

	[property: JsonPropertyName("entity-schema-name")]
	[property: Description("Optional existing entity schema name. When provided, the section reuses that entity.")]
	string? EntitySchemaName = null,

	[property: JsonPropertyName("icon-background")]
	[property: Description("Optional icon background color in #RRGGBB format, e.g. '#1F5F8B'. Defaults to a random color when omitted.")]
	string? IconBackground = null,

	[property: JsonPropertyName("with-mobile-pages")]
	[property: Description("Create mobile pages in addition to web pages. Default: true.")]
	bool WithMobilePages = true,

	[property: JsonPropertyName("title-localizations")]
	[property: Description("Rejected. create-app-section is scalar-only and does not accept localization maps.")]
	IReadOnlyDictionary<string, string>? TitleLocalizations = null,

	[property: JsonPropertyName("description-localizations")]
	[property: Description("Rejected. create-app-section is scalar-only and does not accept localization maps.")]
	IReadOnlyDictionary<string, string>? DescriptionLocalizations = null,

	[property: JsonPropertyName("caption-localizations")]
	[property: Description("Rejected. create-app-section is scalar-only and does not accept localization maps.")]
	IReadOnlyDictionary<string, string>? CaptionLocalizations = null,

	[property: JsonPropertyName("name-localizations")]
	[property: Description("Rejected. create-app-section is scalar-only and does not accept localization maps.")]
	IReadOnlyDictionary<string, string>? NameLocalizations = null
);

/// <summary>
/// MCP arguments for the <c>delete-app-section</c> tool.
/// </summary>
public sealed record ApplicationSectionDeleteArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name, e.g. 'local'")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("application-code")]
	[property: Description("Installed application code.")]
	[property: Required]
	string ApplicationCode,

	[property: JsonPropertyName("section-code")]
	[property: Description("Existing section code inside the installed application.")]
	[property: Required]
	string SectionCode,

	[property: JsonPropertyName("delete-entity-schema")]
	[property: Description("When true, also deletes the entity schema record. Requires explicit opt-in. WARNING: destructive and irreversible.")]
	bool? DeleteEntitySchema
);

/// <summary>
/// MCP arguments for the <c>application-section-get-list</c> tool.
/// </summary>
public sealed record ApplicationSectionGetListArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name, e.g. 'local'")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("application-code")]
	[property: Description("Installed application code.")]
	[property: Required]
	string ApplicationCode
);

/// <summary>
/// MCP arguments for the <c>update-app-section</c> tool.
/// </summary>
public sealed record ApplicationSectionUpdateArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name, e.g. 'local'")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("application-code")]
	[property: Description("Installed application code.")]
	[property: Required]
	string ApplicationCode,

	[property: JsonPropertyName("section-code")]
	[property: Description("Existing section code inside the installed application.")]
	[property: Required]
	string SectionCode,

	[property: JsonPropertyName("caption")]
	[property: Description("Optional updated section caption.")]
	string? Caption = null,

	[property: JsonPropertyName("description")]
	[property: Description("Optional updated section description.")]
	string? Description = null,

	[property: JsonPropertyName("icon-id")]
	[property: Description("Optional updated icon GUID.")]
	string? IconId = null,

	[property: JsonPropertyName("icon-background")]
	[property: Description("Optional updated icon background color in #RRGGBB format.")]
	string? IconBackground = null,

	[property: JsonPropertyName("title-localizations")]
	[property: Description("Rejected. update-app-section is scalar-only and does not accept localization maps.")]
	IReadOnlyDictionary<string, string>? TitleLocalizations = null,

	[property: JsonPropertyName("description-localizations")]
	[property: Description("Rejected. update-app-section is scalar-only and does not accept localization maps.")]
	IReadOnlyDictionary<string, string>? DescriptionLocalizations = null,

	[property: JsonPropertyName("caption-localizations")]
	[property: Description("Rejected. update-app-section is scalar-only and does not accept localization maps.")]
	IReadOnlyDictionary<string, string>? CaptionLocalizations = null,

	[property: JsonPropertyName("name-localizations")]
	[property: Description("Rejected. update-app-section is scalar-only and does not accept localization maps.")]
	IReadOnlyDictionary<string, string>? NameLocalizations = null
);
