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
		 Pass `environment-name` when you need to target a registered clio environment explicitly.
		 Do not pass application filters; this tool always returns the full installed application list for the selected environment.
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
		 Use clio mcp server `{ApplicationGetInfoTool.ApplicationGetInfoToolName}` to return the primary package and runtime entity metadata for one installed Creatio application.
		 Pass `environment-name` `{environmentName}` exactly as provided.
		 Pass exactly one identifier: `app-id` when you already have the installed application GUID, or `app-code` when you have the installed application code.
		 Do not include both identifiers in the same call.
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
		[Description("Optional template data JSON")]
		string? optionalTemplateDataJson = null) =>
		$"""
		 Use clio mcp server `{ApplicationCreateTool.ApplicationCreateToolName}` to create a Creatio application and return its primary package and entity metadata.
		 Pass `environment-name` `{environmentName}` exactly as provided.
		 Provide `name`, `code`, `template-code`, and `icon-background`.
		 Pass `description` only when the application needs one.
		 Pass `icon-id` only when a specific icon identifier is required.
		 Pass `client-type-id` only when a non-default Creatio client type is required.
		 Pass `optional-template-data-json` only when the selected template requires entity-specific options such as `entitySchemaName`, `useExistingEntitySchema`, `useAIContentGeneration`, or `appSectionDescription`.
		 """;
}
