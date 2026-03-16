using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Command.McpServer.Tools;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for entity schema MCP tools.
/// </summary>
[McpServerPromptType, Description("Prompts for creating, reading, and modifying remote entity schemas")]
public static class EntitySchemaPrompt {

	/// <summary>
	/// Builds a prompt that directs the agent to create a remote entity schema through MCP.
	/// </summary>
	[McpServerPrompt(Name = CreateEntitySchemaTool.CreateEntitySchemaToolName),
		Description("Prompt to create a remote entity schema")]
	public static string CreateEntitySchema(
		[Required]
		[Description("Target package name")]
		string packageName,
		[Required]
		[Description("Entity schema name")]
		string schemaName,
		[Required]
		[Description("Entity schema title")]
		string title,
		[Required]
		[Description("Creatio environment name")]
		string environmentName,
		[Description("Optional parent schema name")]
		string parentSchemaName = null,
		[Description("Whether to create a replacement schema")]
		bool extendParent = false) =>
		$"""
		 Use clio mcp server `{CreateEntitySchemaTool.CreateEntitySchemaToolName}` to create entity schema
		 `{schemaName}` in package `{packageName}` for environment `{environmentName}` with title `{title}`.
		 Set `parent-schema-name` only when inheritance or replacement behavior was explicitly requested.
		 Set `extend-parent` to `true` only when the request is specifically for a replacement schema, and only
		 together with `parent-schema-name`.
		 Include `columns` only when the request explicitly describes initial fields. For `Lookup` columns,
		 provide `reference-schema-name`.
		 Current parent request: `{parentSchemaName ?? "<not provided>"}`. Current replacement request:
		 `{extendParent}`.
		 """;

	/// <summary>
	/// Builds a prompt that directs the agent to read structured schema properties through MCP.
	/// </summary>
	[McpServerPrompt(Name = GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName),
		Description("Prompt to read structured remote entity schema properties")]
	public static string GetEntitySchemaProperties(
		[Required]
		[Description("Target package name")]
		string packageName,
		[Required]
		[Description("Entity schema name")]
		string schemaName,
		[Required]
		[Description("Creatio environment name")]
		string environmentName) =>
		$"""
		 Use clio mcp server `{GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName}` to read structured
		 properties for entity schema `{schemaName}` in package `{packageName}` from environment
		 `{environmentName}`.
		 Pass `package-name`, `schema-name`, and `environment-name` exactly as provided.
		 """;

	/// <summary>
	/// Builds a prompt that directs the agent to read structured column properties through MCP.
	/// </summary>
	[McpServerPrompt(Name = GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName),
		Description("Prompt to read structured remote entity schema column properties")]
	public static string GetEntitySchemaColumnProperties(
		[Required]
		[Description("Target package name")]
		string packageName,
		[Required]
		[Description("Entity schema name")]
		string schemaName,
		[Required]
		[Description("Column name")]
		string columnName,
		[Required]
		[Description("Creatio environment name")]
		string environmentName) =>
		$"""
		 Use clio mcp server `{GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName}` to read
		 structured properties for column `{columnName}` in entity schema `{schemaName}` from package
		 `{packageName}` on environment `{environmentName}`.
		 Pass `package-name`, `schema-name`, `column-name`, and `environment-name` exactly as provided.
		 """;

	/// <summary>
	/// Builds a prompt that directs the agent to mutate a remote entity schema column through MCP.
	/// </summary>
	[McpServerPrompt(Name = ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName),
		Description("Prompt to add, modify, or remove a remote entity schema column")]
	public static string ModifyEntitySchemaColumn(
		[Required]
		[Description("Target package name")]
		string packageName,
		[Required]
		[Description("Entity schema name")]
		string schemaName,
		[Required]
		[Description("Column action: add, modify, or remove")]
		string action,
		[Required]
		[Description("Column name")]
		string columnName,
		[Required]
		[Description("Creatio environment name")]
		string environmentName) =>
		$"""
		 Use clio mcp server `{ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName}` to perform action
		 `{action}` on column `{columnName}` in entity schema `{schemaName}` from package `{packageName}` on
		 environment `{environmentName}`.
		 Pass only the option fields required for the requested action. For `add`, supply `type`; for `Lookup`,
		 also supply `reference-schema-name`. For `modify`, include only the fields that should change. For
		 `remove`, do not pass property-change options.
		 """;
}
