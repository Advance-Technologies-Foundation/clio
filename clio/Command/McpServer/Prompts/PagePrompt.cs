using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Command.McpServer.Tools;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for Freedom UI page MCP tools.
/// </summary>
[McpServerPromptType, Description("Prompts for reading and updating Freedom UI pages")]
public static class PagePrompt {
	/// <summary>
	/// Builds a prompt for inspecting a Freedom UI page before editing it.
	/// </summary>
	[McpServerPrompt(Name = PageGetTool.ToolName), Description("Prompt to inspect a Freedom UI page bundle and raw body")]
	public static string GetPage(
		[Required] [Description("Freedom UI page schema name")] string schemaName,
		[Description("Optional Creatio environment name")] string? environmentName = null) =>
		$"""
		 Use `page-list` first when you need to discover candidate page schemas.
		 Use `{PageGetTool.ToolName}` with `schemaName` `{schemaName}` and `environmentName` `{environmentName ?? "<default or explicit connection args>"}` to inspect the effective merged page structure.
		 Read layout and container hierarchy from `bundle.viewConfig`.
		 Read page metadata from `page`.
		 When you need to edit the page, take the JavaScript payload from `raw.body`, modify that raw body, and send it to `{PageUpdateTool.ToolName}`.
		 Use `page-sync` only when you need to save multiple pages in one workflow.
		 """;
}
