using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for the create entity schema MCP tool.
/// </summary>
[McpServerPromptType, Description("Prompts for creating remote entity schemas")]
public class CreateEntitySchemaPrompt {

	/// <summary>
	/// Builds a prompt that directs the agent to create a remote entity schema through MCP.
	/// </summary>
	[McpServerPrompt(Name = "create-entity-schema"), Description("Prompt to create a remote entity schema")]
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
		 Use clio mcp server `create-entity-schema` tool to create entity schema `{schemaName}` in package
		 `{packageName}` for environment `{environmentName}` with title `{title}`.
		 Set `parent-schema-name` only when inheritance or replacement behavior was explicitly requested.
		 Set `extend-parent` to `true` only when the request is specifically for a replacement schema, and only
		 together with `parent-schema-name`.
		 Include `columns` only when the request explicitly describes initial fields. For `Lookup` columns,
		 provide `reference-schema-name`.
		 Current parent request: `{parentSchemaName ?? "<not provided>"}`. Current replacement request:
		 `{extendParent}`.
		 """;
}
