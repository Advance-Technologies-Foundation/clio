using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Clio.Command.McpServer.Tools;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for application MCP tools.
/// </summary>
[McpServerPromptType, Description("Prompts for working with Creatio applications through MCP")]
public static class ApplicationPrompt {
	/// <summary>
	/// Builds a prompt that directs the agent to list installed Creatio applications through MCP.
	/// </summary>
	[McpServerPrompt(Name = ApplicationGetListTool.ApplicationGetListToolName),
		Description("Prompt to list installed Creatio applications")]
	public static string ApplicationGetList(
		[Description("Creatio environment name")]
		string? environmentName = null) =>
		$"""
		 Use clio mcp server `{ApplicationGetListTool.ApplicationGetListToolName}` to return installed Creatio applications as structured JSON.
		 Before the first application discovery or existing-app maintenance tool call in a workflow, call `{ToolContractGetTool.ToolName}` with `tool-names` such as `application-get-list`, `application-get-info`, and `application-section-create` so the client starts from the authoritative clio MCP contract.
		 For the canonical existing-app maintenance flow, read `docs://mcp/guides/existing-app-maintenance`.
		 Pass `environment-name` when you need to target a registered clio environment explicitly.
		 Pass tool arguments at the top level of the MCP request; do not wrap `environment-name` inside an `args` object.
		 Do not pass application filters; this tool always returns the full installed application list for the selected environment.
		 Use this discovery step before `{ApplicationGetInfoTool.ApplicationGetInfoToolName}` when the target app is not fully known.
		 """;

	/// <summary>
	/// Builds a prompt that directs the agent to read application package and entity metadata through MCP.
	/// </summary>
	[McpServerPrompt(Name = ApplicationGetInfoTool.ApplicationGetInfoToolName),
		Description("Prompt to read detailed Creatio application info")]
	public static string ApplicationGetInfo(
		[Description("Creatio environment name")]
		string environmentName,
		[Description("Optional installed application id filter")]
		string? id = null,
		[Description("Optional installed application code filter")]
		string? code = null) =>
		$"""
		 Use clio mcp server `{ApplicationGetInfoTool.ApplicationGetInfoToolName}` to return installed application identity plus the primary package and runtime entity metadata for one installed Creatio application.
		 If this is the first application-related MCP call in the workflow, call `{ToolContractGetTool.ToolName}` first with `tool-names` such as `application-get-list`, `application-get-info`, and `application-section-create` so the client starts from the authoritative contract.
		 For the canonical discover -> inspect -> mutate flow, read `docs://mcp/guides/existing-app-maintenance`.
		 Pass `environment-name` `{environmentName}` exactly as provided.
		 Pass exactly one identifier: `id` when you already have the installed application GUID, or `code` when you have the installed application code.
		 Do not include both identifiers in the same call.
		 Use this after `{ApplicationGetListTool.ApplicationGetListToolName}` when the target app is not fully known.
		 """;

	/// <summary>
	/// Builds a prompt that directs the agent to create a Creatio application through MCP.
	/// </summary>
	[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
		Justification = "Prompt parameters intentionally mirror the application-create MCP contract.")]
	[McpServerPrompt(Name = ApplicationCreateTool.ApplicationCreateToolName),
		Description("Prompt to create a Creatio application")]
	public static string ApplicationCreate(
		[Description("Creatio environment name")]
		string environmentName,
		[Description("Application name")]
		string name,
		[Description("Application code")]
		string code,
		[Description("Application template code")]
		string templateCode,
		[Description("Application icon background color")]
		string iconBackground,
		[Description("Application description")]
		string? description = null,
		[Description("Application icon identifier")]
		string? iconId = null,
		[Description("Optional client type identifier")]
		string? clientTypeId = null,
		[Description("Optional template data JSON")]
		string? optionalTemplateDataJson = null) =>
		$"""
		 Use clio mcp server `{ApplicationCreateTool.ApplicationCreateToolName}` to create a Creatio application and return its primary package and entity metadata.
		 Before the first app-modeling tool call in a workflow, call `{ToolContractGetTool.ToolName}` with `tool-names` such as `application-create`, `schema-sync`, and `page-sync` so the client starts from the authoritative contract and follow-up flow.
		 Pass `environment-name` `{environmentName}` exactly as provided.
		 Provide `name`, `code`, `template-code`, and `icon-background`.
		 Pass those fields at the top level of the MCP request; do not nest them under `args`.
		 For end-to-end app modeling guardrails, read `docs://mcp/guides/app-modeling`.
		 For a new app with one primary record type, treat the entity returned by `application-create` as the canonical main entity and extend it instead of creating a synonym entity for the same records.
		 `application-create` is a scalar app-shell tool. Keep `name`, `description`, and `optional-template-data-json.appSectionDescription` as plain strings.
		 `template-code` is the technical template name, not the display name. Known values include `AppFreedomUI`, `AppFreedomUIv2`, `AppWithHomePage`, and `EmptyApp`.
		 Do not send `title-localizations`, `description-localizations`, `name-localizations`, or other localization-map fields to `application-create`.
		 If the app needs localized entity or column captions, run follow-up entity-schema tools such as `schema-sync` or `update-entity-schema` after `application-create`.
		 Pass `description` only when the application needs one.
		 Pass `icon-id` only when a specific icon identifier is required.
		 Pass `client-type-id` only when a non-default Creatio client type is required.
		 Pass `optional-template-data-json` only when the selected template requires entity-specific options such as `entitySchemaName`, `useExistingEntitySchema`, `useAIContentGeneration`, or `appSectionDescription`.
		 """;

	/// <summary>
	/// Builds a prompt that directs the agent to create a section inside an existing Creatio application through MCP.
	/// </summary>
	[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
		Justification = "Prompt parameters intentionally mirror the application-section-create MCP contract.")]
	[McpServerPrompt(Name = ApplicationSectionCreateTool.ApplicationSectionCreateToolName),
		Description("Prompt to create a section inside an existing application")]
	public static string ApplicationSectionCreate(
		[Description("Creatio environment name")]
		string environmentName,
		[Description("Installed application code")]
		string applicationCode,
		[Description("Section caption")]
		string caption,
		[Description("Optional section description")]
		string? description = null,
		[Description("Optional existing entity schema name")]
		string? entitySchemaName = null,
		[Description("Create mobile pages")]
		bool withMobilePages = true) =>
		$"""
		 Use clio mcp server `{ApplicationSectionCreateTool.ApplicationSectionCreateToolName}` to create a section inside an existing installed Creatio application and return structured section, entity, and page readback data.
		 Before the first existing-app mutation call in a workflow, call `{ToolContractGetTool.ToolName}` with `tool-names` such as `application-get-list`, `application-get-info`, and `application-section-create` so the client starts from the authoritative contract and preferred flow.
		 For the canonical existing-app maintenance flow, read `docs://mcp/guides/existing-app-maintenance`.
		 Pass `environment-name` `{environmentName}` exactly as provided.
		 Pass `application-code` `{applicationCode}` as the installed application selector.
		 Provide `caption` as a plain scalar string.
		 Pass all tool arguments at the top level of the MCP request; do not wrap them inside `args`.
		 When `entity-schema-name` is provided, the section reuses that existing entity. When it is omitted, Creatio creates a new object for the section.
		 Keep `with-mobile-pages` as a top-level boolean. When omitted it defaults to `true`.
		 Do not send `title-localizations`, `description-localizations`, `caption-localizations`, or other localization-map fields to `application-section-create`.
		 If the target app is not fully known, use `{ApplicationGetListTool.ApplicationGetListToolName}` first, then `{ApplicationGetInfoTool.ApplicationGetInfoToolName}`, then `{ApplicationSectionCreateTool.ApplicationSectionCreateToolName}`.
		 """;

	/// </summary>
	[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
		Justification = "Prompt parameters intentionally mirror the application-section-update MCP contract.")]
	[McpServerPrompt(Name = ApplicationSectionUpdateTool.ApplicationSectionUpdateToolName),
		Description("Prompt to update a section inside an existing application")]
	public static string ApplicationSectionUpdate(
		[Description("Creatio environment name")]
		string environmentName,
		[Description("Installed application code")]
		string applicationCode,
		[Description("Existing section code")]
		string sectionCode,
		[Description("Optional updated caption")]
		string? caption = null,
		[Description("Optional updated description")]
		string? description = null,
		[Description("Optional updated icon GUID")]
		string? iconId = null,
		[Description("Optional updated icon background")]
		string? iconBackground = null) =>
		$"""
		 Use clio mcp server `{ApplicationSectionUpdateTool.ApplicationSectionUpdateToolName}` to update metadata of an existing section inside an installed Creatio application and return structured section readback data before and after the update.
		 Before the first existing-app mutation call in a workflow, call `{ToolContractGetTool.ToolName}` with `tool-names` such as `application-get-list`, `application-get-info`, and `application-section-update` so the client starts from the authoritative contract and preferred flow.
		 For the canonical existing-app maintenance flow, read `docs://mcp/guides/existing-app-maintenance`.
		 Pass `environment-name` `{environmentName}` exactly as provided.
		 Pass `application-code` `{applicationCode}` as the installed application selector.
		 Pass `section-code` `{sectionCode}` as the existing section selector inside that application.
		 Use `caption`, `description`, `icon-id`, and `icon-background` as optional top-level partial update fields. Omit any field that should remain unchanged.
		 When updating a broken JSON-style section heading, provide a new plain-text `caption`.
		 Pass all tool arguments at the top level of the MCP request; do not wrap them inside `args`.
		 Do not send `title-localizations`, `description-localizations`, `caption-localizations`, or other localization-map fields to `application-section-update`.
		 If the target app is not fully known, use `{ApplicationGetListTool.ApplicationGetListToolName}` first, then `{ApplicationGetInfoTool.ApplicationGetInfoToolName}`, then `{ApplicationSectionUpdateTool.ApplicationSectionUpdateToolName}`.
		 """;
}
