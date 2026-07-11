using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP arguments for the <c>list-apps</c> tool. <c>environment-name</c> is schema-optional (FR-05a,
/// ENG-93347): under credential passthrough the target tenant comes from the
/// <c>X-Integration-Credentials</c> header, while on non-passthrough transports runtime requiredness
/// is enforced by the resolver (<see cref="EnvironmentResolutionException"/> when no environment or
/// URI is resolvable).
/// </summary>
public sealed record ApplicationGetListArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName + " Optional under credential passthrough.")]
	string? EnvironmentName = null
);

/// <summary>
/// MCP arguments for the <c>get-app-info</c> tool. <c>environment-name</c> is schema-optional (FR-05a,
/// ENG-93347): under credential passthrough the target tenant comes from the
/// <c>X-Integration-Credentials</c> header, while on non-passthrough transports runtime requiredness
/// is enforced by the resolver (<see cref="EnvironmentResolutionException"/> when no environment or
/// URI is resolvable).
/// </summary>
public sealed record ApplicationGetInfoArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName + " Optional under credential passthrough.")]
	string? EnvironmentName = null,

	[property: JsonPropertyName("id")]
	[property: Description("Application ID (GUID). Optional filter.")]
	string? Id = null,

	[property: JsonPropertyName("code")]
	[property: Description("Application code, e.g. 'UsrMyApp'. Optional filter.")]
	string? Code = null
);

/// <summary>
/// MCP arguments for the <c>create-app</c> tool. <c>environment-name</c> is schema-optional (FR-05a,
/// ENG-93347): under credential passthrough the target tenant comes from the
/// <c>X-Integration-Credentials</c> header, while on non-passthrough transports runtime requiredness
/// is enforced by the resolver (<see cref="EnvironmentResolutionException"/> when no environment or
/// URI is resolvable). Declared after the required <c>name</c>/<c>code</c> parameters because C#
/// optional parameters must follow required ones; every call site uses named arguments.
/// </summary>
public sealed record ApplicationCreateArgs(
	[property: JsonPropertyName("name")]
	[property: Description("Application display name, e.g. 'My App'")]
	[property: Required]
	string Name,

	[property: JsonPropertyName("code")]
	[property: Description(
		"Application code. Pass the business-meaningful part only (e.g. 'TodoList'). " +
		"clio reads SchemaNamePrefix from the environment and applies it automatically. " +
		"If you pass an already-prefixed code (e.g. 'UsrTodoList'), the prefix is not duplicated. " +
		"The effective prefix and the resulting application code are returned in the response as 'schema-name-prefix' and 'application-code'. " +
		"Use 'schema-name-prefix' from the response as the prefix for ALL subsequent schema names (lookups, entity columns, supporting entities). " +
		"Creatio derives the package name, main entity schema name, and page schema names ({prefix}{code}_FormPage, {prefix}{code}_ListPage, etc.) directly from this code.")]
	[property: Required]
	string Code,

	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName + " Optional under credential passthrough.")]
	string? EnvironmentName = null,

	[property: JsonPropertyName("template-code")]
	[property: Description("Technical template name (NOT display name). Known values: AppFreedomUI, AppFreedomUIv2, AppWithHomePage, EmptyApp. Defaults to AppFreedomUI when omitted — use this default unless you have a specific reason to change it.")]
	string? TemplateCode = "AppFreedomUI",

	[property: JsonPropertyName("description")]
	[property: Description("Application description")]
	string? Description = null,

	[property: JsonPropertyName("icon-background")]
	[property: Description("Optional Freedom UI palette color in #RRGGBB format. Must be one of: #A6DE00, #20A959, #22AC14, #FFAC07, #FF8800, #F9307F, #FF602E, #FF4013, #B87CCF, #7848EE, #247EE5, #0058EF, #009DE3, #4F43C2, #08857E, #00BFA5. Omit unless the user explicitly requested a specific color — a random palette color is assigned automatically when absent.")]
	string? IconBackground = null,

	[property: JsonPropertyName("icon-id")]
	[property: Description("Optional application icon GUID (e.g. '00000000-0000-0000-0000-000000000000'), or 'auto' to resolve a random icon.")]
	string? IconId = null,

	[property: JsonPropertyName("client-type-id")]
	[property: Description("Optional client type identifier. When provided it takes precedence over with-mobile-pages.")]
	string? ClientTypeId = null,

	[property: JsonPropertyName("with-mobile-pages")]
	[property: Description("Create mobile pages (_MobileFormPage, _MobileListPage) for the main entity in addition to web pages. Default: true. Set to false for a web-only application to skip mobile page generation.")]
	bool WithMobilePages = true,

	[property: JsonPropertyName("optional-template-data-json")]
	[property: Description(
		"Optional JSON object: {useExistingEntitySchema, entitySchemaName, appSectionDescription, useAIContentGeneration}. " +
		"IMPORTANT: entitySchemaName is only valid together with useExistingEntitySchema=true, and the entity MUST already exist in Creatio " +
		"before create-app is called. Providing both fields wires the app to that existing entity and suppresses auto-creation of a new one. " +
		"Passing entitySchemaName without useExistingEntitySchema=true, or for an entity that does not yet exist, " +
		"is unsupported and may fail server-side. useAIContentGeneration is not supported and will be rejected.")]
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
/// MCP arguments for the <c>create-app-section</c> tool. <c>environment-name</c> is schema-optional (FR-05a,
/// ENG-93347): under credential passthrough the target tenant comes from the
/// <c>X-Integration-Credentials</c> header, while on non-passthrough transports runtime requiredness
/// is enforced by the resolver (<see cref="EnvironmentResolutionException"/> when no environment or
/// URI is resolvable). Declared after the required <c>application-code</c>/<c>caption</c> parameters
/// because C# optional parameters must follow required ones; every call site uses named arguments.
/// </summary>
public sealed record ApplicationSectionCreateArgs(
	[property: JsonPropertyName("application-code")]
	[property: Description("Installed application code.")]
	[property: Required]
	string ApplicationCode,

	[property: JsonPropertyName("caption")]
	[property: Description("Section caption, e.g. 'Orders'")]
	[property: Required]
	string Caption,

	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName + " Optional under credential passthrough.")]
	string? EnvironmentName = null,

	[property: JsonPropertyName("description")]
	[property: Description("Optional section description")]
	string? Description = null,

	[property: JsonPropertyName("entity-schema-name")]
	[property: Description("Optional existing entity schema name. When provided, the section reuses that entity; the object must exist (validated before creation). Several sections may target the same object.")]
	string? EntitySchemaName = null,

	[property: JsonPropertyName("code")]
	[property: Description("Optional explicit section code (Latin identifier). When omitted, the code is generated from the caption; required when the caption has no Latin letters or digits — for a non-Latin caption such as 'Контакти' pass an English code like 'Contacts'.")]
	string? Code = null,

	[property: JsonPropertyName("icon-background")]
	[property: Description("Optional icon background color in #RRGGBB format. Must be one of the Freedom UI palette values that render as gradient tiles: #A6DE00, #20A959, #22AC14, #FFAC07, #FF8800, #F9307F, #FF602E, #FF4013, #B87CCF, #7848EE, #247EE5, #0058EF, #009DE3, #4F43C2, #08857E, #00BFA5. Defaults to a random palette color when omitted.")]
	string? IconBackground = null,

	[property: JsonPropertyName("with-mobile-pages")]
	[property: Description("Create mobile pages in addition to web pages. Default: true.")]
	bool WithMobilePages = true,

	[property: JsonPropertyName("caption-culture")]
	[property: Description("Optional culture override for the section caption readback (e.g. 'en-US', 'uk-UA'). Precedence: caption-culture > detected profile culture > en-US. Skips the profile-culture lookup.")]
	string? CaptionCulture = null,

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
	[property: JsonPropertyName("application-code")]
	[property: Description("Installed application code.")]
	[property: Required]
	string ApplicationCode,

	[property: JsonPropertyName("section-code")]
	[property: Description("Existing section code inside the installed application.")]
	[property: Required]
	string SectionCode,

	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName + " Optional under credential passthrough.")]
	string? EnvironmentName = null,

	[property: JsonPropertyName("delete-entity-schema")]
	[property: Description("When true, also deletes the entity schema record. Requires explicit opt-in. WARNING: destructive and irreversible.")]
	bool? DeleteEntitySchema = null
);

/// <summary>
/// MCP arguments for the <c>application-section-get-list</c> tool.
/// </summary>
public sealed record ApplicationSectionGetListArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("application-code")]
	[property: Description("Installed application code.")]
	[property: Required]
	string ApplicationCode
);

/// <summary>
/// MCP arguments for the <c>update-app-section</c> tool. <c>environment-name</c> is schema-optional
/// (FR-05a, ENG-93347): under credential passthrough the target tenant comes from the
/// <c>X-Integration-Credentials</c> header, while on non-passthrough transports runtime requiredness
/// is enforced by the resolver (<see cref="EnvironmentResolutionException"/> when no environment or
/// URI is resolvable). Declared after the required <c>application-code</c>/<c>section-code</c>
/// parameters because C# optional parameters must follow required ones; every call site uses named
/// arguments.
/// </summary>
public sealed record ApplicationSectionUpdateArgs(
	[property: JsonPropertyName("application-code")]
	[property: Description("Installed application code.")]
	[property: Required]
	string ApplicationCode,

	[property: JsonPropertyName("section-code")]
	[property: Description("Existing section code inside the installed application.")]
	[property: Required]
	string SectionCode,

	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName + " Optional under credential passthrough.")]
	string? EnvironmentName = null,

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
	[property: Description("Optional updated icon background color in #RRGGBB format. Must be one of the Freedom UI palette values that render as gradient tiles: #A6DE00, #20A959, #22AC14, #FFAC07, #FF8800, #F9307F, #FF602E, #FF4013, #B87CCF, #7848EE, #247EE5, #0058EF, #009DE3, #4F43C2, #08857E, #00BFA5.")]
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
