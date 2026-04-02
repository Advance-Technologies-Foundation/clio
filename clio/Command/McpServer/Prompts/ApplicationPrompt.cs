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
		 For the canonical existing-app maintenance flow, read `docs://mcp/guides/existing-app-maintenance`.
		 Pass `environment-name` when you need to target a registered clio environment explicitly.
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
		string? appId = null,
		[Description("Optional installed application code filter")]
		string? appCode = null) =>
		$"""
		 Use clio mcp server `{ApplicationGetInfoTool.ApplicationGetInfoToolName}` to return installed application identity plus the primary package and runtime entity metadata for one installed Creatio application.
		 For the canonical discover -> inspect -> mutate flow, read `docs://mcp/guides/existing-app-maintenance`.
		 Pass `environment-name` `{environmentName}` exactly as provided.
		 Pass exactly one identifier: `app-id` when you already have the installed application GUID, or `app-code` when you have the installed application code.
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
		 Pass `environment-name` `{environmentName}` exactly as provided.
		 Provide `name`, `code`, `template-code`, and `icon-background`.
		 For end-to-end app modeling guardrails, read `docs://mcp/guides/app-modeling`.
		 For a new app with one primary record type, treat the entity returned by `application-create` as the canonical main entity and extend it instead of creating a synonym entity for the same records.
		 `application-create` is a scalar app-shell tool. Keep `name`, `description`, and `optional-template-data-json.appSectionDescription` as plain strings.
		 Do not send `title-localizations`, `description-localizations`, `name-localizations`, or other localization-map fields to `application-create`.
		 If the app needs localized entity or column captions, run follow-up entity-schema tools such as `schema-sync` or `update-entity-schema` after `application-create`.
		 Pass `description` only when the application needs one.
		 Pass `icon-id` only when a specific icon identifier is required.
		 Pass `client-type-id` only when a non-default Creatio client type is required.
		 Pass `optional-template-data-json` only when the selected template requires entity-specific options such as `entitySchemaName`, `useExistingEntitySchema`, `useAIContentGeneration`, or `appSectionDescription`.
		 """;
}
