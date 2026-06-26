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
		 Before the first application discovery or existing-app maintenance tool call in a workflow, call `{ToolContractGetTool.ToolName}` with `tool-names` such as `list-apps`, `get-app-info`, and `create-app-section` so the client starts from the authoritative clio MCP contract.
		 For the canonical existing-app maintenance flow, call `{GuidanceGetTool.ToolName}` with `name` set to `existing-app-maintenance`.
		 Prefer a registered clio environment for application work. If the target site is not registered yet, call `reg-web-app` first and then continue with `environment-name`.
		 Use direct connection args only on tools that still expose them, and only when local bootstrap is broken or you are in an emergency recovery flow.
		 Pass `environment-name` when you need to target a registered clio environment explicitly.
		 Wrap tool arguments under the top-level `args` JSON object exactly as advertised by the tool schema (the wire shape places `environment-name` inside the required `args` object).
		 Do not pass application filters; this tool always returns the full installed application list for the selected environment.
		 Prefer `{FindAppTool.FindAppToolName}` when you only have an imprecise or partial app name: it matches application name, code, description, and section captions and returns each matching app WITH its sections in a single call, so you usually do not need a separate per-app section lookup.
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
		 This call can run long; it streams notifications/progress while working — await completion and do not retry on a perceived timeout.
		 If this is the first application-related MCP call in the workflow, call `{ToolContractGetTool.ToolName}` first with `tool-names` such as `list-apps`, `get-app-info`, and `create-app-section` so the client starts from the authoritative contract.
		 For the canonical discover -> inspect -> mutate flow, call `{GuidanceGetTool.ToolName}` with `name` set to `existing-app-maintenance`.
		 Pass `environment-name` `{environmentName}` exactly as provided.
		 Pass exactly one identifier: `id` when you already have the installed application GUID, or `code` when you have the installed application code.
		 Do not include both identifiers in the same call.
		 Use this after `{ApplicationGetListTool.ApplicationGetListToolName}` (or `{FindAppTool.FindAppToolName}`, which maps an imprecise app name to a code and returns its sections in one call) when the target app is not fully known.
		 """;

	/// <summary>
	/// Builds a prompt that directs the agent to create a Creatio application through MCP.
	/// </summary>
	[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
		Justification = "Prompt parameters intentionally mirror the create-app MCP contract.")]
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
		[Description("Create mobile pages for the main entity (default true; set false for a web-only app)")]
		bool withMobilePages = true,
		[Description("Optional template data JSON")]
		string? optionalTemplateDataJson = null) =>
		$"""
		 Use clio mcp server `{ApplicationCreateTool.ApplicationCreateToolName}` to create a Creatio application and return its primary package and entity metadata.
		 This call can run for minutes; it streams notifications/progress while working — await completion and do not retry on a perceived timeout.
		 Before the first app-modeling tool call in a workflow, call `{ToolContractGetTool.ToolName}` with `tool-names` such as `create-app`, `sync-schemas`, and `sync-pages` so the client starts from the authoritative contract and follow-up flow.
		 Pass `environment-name` `{environmentName}` exactly as provided.
		 Provide `name`, `code`, and `template-code`.
		 Wrap those fields inside the top-level `args` JSON object as advertised by the tool schema (the wire shape places `name`, `code`, and `template-code` inside the required `args` object).
		 Pass `icon-background` only when the user explicitly specified a color; omit it otherwise — a random Freedom UI palette color is assigned automatically.
		 For end-to-end app modeling guardrails, call `{GuidanceGetTool.ToolName}` with `name` set to `app-modeling`.
		 `create-app` already performs an internal Data Forge enrichment step and returns optional `dataforge` diagnostics with health, coverage, warnings, and a compact context summary.
		 Do not add a separate mandatory Data Forge preflight outside the canonical `create-app` flow unless the workflow explicitly needs standalone Data Forge inspection or remediation.
		 For a new app with one primary record type, treat the entity returned by `create-app` as the canonical main entity and extend it instead of creating a synonym entity for the same records.
		 `create-app` is a scalar app-shell tool. Keep `name`, `description`, and `optional-template-data-json.appSectionDescription` as plain strings.
		 `template-code` is the technical template name, not the display name. Known values include `AppFreedomUI`, `AppFreedomUIv2`, `AppWithHomePage`, and `EmptyApp`.
		 Do not send `title-localizations`, `description-localizations`, `name-localizations`, or other localization-map fields to `create-app`.
		 If the app needs localized entity or column captions, run follow-up entity-schema tools such as `sync-schemas` or `update-entity-schema` after `create-app`.
		 Pass `description` only when the application needs one.
		 Pass `icon-id` only when a specific icon identifier is required.
		 Pass `client-type-id` only when a non-default Creatio client type is required. When supplied it takes precedence over `with-mobile-pages`.
		 Keep `with-mobile-pages` as a top-level boolean. When omitted it defaults to `true`, generating the main entity `_MobileFormPage` and `_MobileListPage` alongside the web pages.
		 When the user's plan is web-only (no mobile app target), proactively set `with-mobile-pages` to `false` before calling `create-app` so the mobile pages are not created and do not need manual cleanup afterwards.
		 Pass `optional-template-data-json` only when the selected template requires entity-specific options such as `entitySchemaName`, `useExistingEntitySchema`, `useAIContentGeneration`, or `appSectionDescription`.
		 Detect the connected user's profile language ONCE per session via `get-user-culture` and reuse it for the application name and captions; if it returns `success:false`, ASK the user which language to use — do NOT silently use the host locale or `en-US`.
		 The detected culture is the LANGUAGE of the text, not just a key: author the name and captions IN that language (an `en-US` profile means English text), regardless of the conversation/task language.
		 """;

	/// <summary>
	/// Builds a prompt that directs the agent to create a section inside an existing Creatio application through MCP.
	/// </summary>
	[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
		Justification = "Prompt parameters intentionally mirror the create-app-section MCP contract.")]
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
		 This call can run for minutes; it streams notifications/progress while working — await completion and do not retry on a perceived timeout.
		 Before the first existing-app mutation call in a workflow, call `{ToolContractGetTool.ToolName}` with `tool-names` such as `list-apps`, `get-app-info`, and `create-app-section` so the client starts from the authoritative contract and preferred flow.
		 For the canonical existing-app maintenance flow, call `{GuidanceGetTool.ToolName}` with `name` set to `existing-app-maintenance`.
		 Pass `environment-name` `{environmentName}` exactly as provided.
		 Pass `application-code` `{applicationCode}` as the installed application selector.
		 Provide `caption` as a plain scalar string.
		 Wrap all tool arguments under the top-level `args` JSON object exactly as advertised by the tool schema; do not flatten or rename canonical fields.
		 When `entity-schema-name` is provided, the section reuses that existing entity. When it is omitted, Creatio creates a new object for the section.
		 The section `code` is generated from the caption; a non-Latin caption (for example `Контакти`) cannot produce a valid Latin code, so pass an explicit `code` such as `Contacts`. Several sections may target the same `entity-schema-name`, so reuse is allowed; the object must exist, and a missing object fails with a clear `does not exist` error. If creation reports that a section with that code already exists, change the caption or pass a different `code`, or call `{ApplicationSectionGetListTool.ApplicationSectionGetListToolName}` to inspect existing sections.
		 If creation fails with a classified error, read `error-class` before deciding: `transport` means the request never reached Creatio and retrying is safe; `creatio-timeout` means the section may still be created server-side — wait, call `{ApplicationSectionGetListTool.ApplicationSectionGetListToolName}`, and retry only if the section is still absent; `server-error` means Creatio rejected the operation. Follow the returned `retry-guidance`; never blind-retry the same call.
		 A `section-created: in-progress` value (with `error-class: creatio-timeout`) is NOT a failure — the response deadline elapsed but the section keeps being created server-side. Do NOT retry `{ApplicationSectionCreateTool.ApplicationSectionCreateToolName}` (it would duplicate the section) and do NOT fall back to `create-page`/`sync-pages`; poll `{ApplicationSectionGetListTool.ApplicationSectionGetListToolName}` and `{ApplicationGetInfoTool.ApplicationGetInfoToolName}` until the section and its generated List and Form pages appear, then continue.
		 Keep `with-mobile-pages` as a top-level boolean. When omitted it defaults to `true`.
		 Do not send `title-localizations`, `description-localizations`, `caption-localizations`, or other localization-map fields to `create-app-section`.
		 If the target app is not fully known, use `{ApplicationGetListTool.ApplicationGetListToolName}` first, then `{ApplicationGetInfoTool.ApplicationGetInfoToolName}`, then `{ApplicationSectionCreateTool.ApplicationSectionCreateToolName}`.
		 Detect the connected user's profile language ONCE per session via `get-user-culture` and reuse it for the section caption; if it returns `success:false`, ASK the user which language to use — do NOT silently use the host locale or `en-US`. Override per call with `caption-culture`.
		 The detected culture is the LANGUAGE of the caption text, not just a key: author the caption IN that language (an `en-US` profile means an English caption), regardless of the conversation/task language. The stored section caption is localized under the user's PROFILE culture, so clio rejects a caption whose script does not match it (e.g. Cyrillic for an `en-US` profile). `caption-culture` only changes which value the readback surfaces — it does NOT change the stored language and is NOT an escape hatch here; author the caption in the profile language.
		 """;

	/// </summary>
	[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
		Justification = "Prompt parameters intentionally mirror the update-app-section MCP contract.")]
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
		 This call can run long; it streams notifications/progress while working — await completion and do not retry on a perceived timeout.
		 Before the first existing-app mutation call in a workflow, call `{ToolContractGetTool.ToolName}` with `tool-names` such as `list-apps`, `get-app-info`, and `update-app-section` so the client starts from the authoritative contract and preferred flow.
		 For the canonical existing-app maintenance flow, call `{GuidanceGetTool.ToolName}` with `name` set to `existing-app-maintenance`.
		 Pass `environment-name` `{environmentName}` exactly as provided.
		 Pass `application-code` `{applicationCode}` as the installed application selector.
		 Pass `section-code` `{sectionCode}` as the existing section selector inside that application.
		 Use `caption`, `description`, `icon-id`, and `icon-background` as optional top-level partial update fields. Omit any field that should remain unchanged.
		 Author the `caption`/`description` in the connected user's profile language (detect once via `get-user-culture`): the caption is localized under the profile, so clio rejects a caption whose script does not match it (e.g. Cyrillic for an `en-US` profile).
		 When updating a broken JSON-style section heading, provide a new plain-text `caption`.
		 Wrap all tool arguments under the top-level `args` JSON object exactly as advertised by the tool schema; do not flatten or rename canonical fields.
		 Do not send `title-localizations`, `description-localizations`, `caption-localizations`, or other localization-map fields to `update-app-section`.
		 If the target app is not fully known, use `{ApplicationGetListTool.ApplicationGetListToolName}` first, then `{ApplicationGetInfoTool.ApplicationGetInfoToolName}`, then `{ApplicationSectionUpdateTool.ApplicationSectionUpdateToolName}`.
		 """;

	[McpServerPrompt(Name = ApplicationSectionDeleteTool.ApplicationSectionDeleteToolName),
		Description("Prompt to delete a section from an existing application")]
	public static string ApplicationSectionDelete(
		[Description("Creatio environment name")]
		string environmentName,
		[Description("Installed application code")]
		string applicationCode,
		[Description("Existing section code to delete")]
		string sectionCode) =>
		$"""
		 Use clio mcp server `{ApplicationSectionDeleteTool.ApplicationSectionDeleteToolName}` to delete an existing section from an installed Creatio application and return structured readback of the deleted section.
		 This call can run long; it streams notifications/progress while working — await completion and do not retry on a perceived timeout.
		 Before the first destructive call in a workflow, call `{ToolContractGetTool.ToolName}` with `tool-names` such as `list-apps`, `get-app-info`, and `delete-app-section` so the client starts from the authoritative contract and preferred flow.
		 For the canonical existing-app maintenance flow, call `{GuidanceGetTool.ToolName}` with `name` set to `existing-app-maintenance`.
		 Warn the user before deleting — this operation is destructive and cannot be undone.
		 Pass `environment-name` `{environmentName}` exactly as provided.
		 Pass `application-code` `{applicationCode}` as the installed application selector.
		 Pass `section-code` `{sectionCode}` as the existing section code to delete inside that application.
		 Wrap all tool arguments under the top-level `args` JSON object exactly as advertised by the tool schema; do not flatten or rename canonical fields.
		 If the target app is not fully known, use `{ApplicationGetListTool.ApplicationGetListToolName}` first, then `{ApplicationGetInfoTool.ApplicationGetInfoToolName}`, then `{ApplicationSectionDeleteTool.ApplicationSectionDeleteToolName}`.
		 """;

	[McpServerPrompt(Name = ApplicationSectionGetListTool.ApplicationSectionGetListToolName),
		Description("Prompt to list sections of an existing application")]
	public static string ApplicationSectionGetList(
		[Description("Creatio environment name")]
		string environmentName,
		[Description("Installed application code")]
		string applicationCode) =>
		$"""
		 Use clio mcp server `{ApplicationSectionGetListTool.ApplicationSectionGetListToolName}` to list all sections of an installed Creatio application and return structured section list data.
		 This call can run long; it streams notifications/progress while working — await completion and do not retry on a perceived timeout.
		 Before the first existing-app read call in a workflow, call `{ToolContractGetTool.ToolName}` with `tool-names` such as `list-apps`, `get-app-info`, and `list-app-sections` so the client starts from the authoritative contract and preferred flow.
		 For the canonical existing-app maintenance flow, call `{GuidanceGetTool.ToolName}` with `name` set to `existing-app-maintenance`.
		 Pass `environment-name` `{environmentName}` exactly as provided.
		 Pass `application-code` `{applicationCode}` as the installed application selector.
		 Wrap all tool arguments under the top-level `args` JSON object exactly as advertised by the tool schema; do not flatten or rename canonical fields.
		 If the target app is not fully known, use `{ApplicationGetListTool.ApplicationGetListToolName}` first, then `{ApplicationGetInfoTool.ApplicationGetInfoToolName}`, then `{ApplicationSectionGetListTool.ApplicationSectionGetListToolName}`.
		 """;
}
