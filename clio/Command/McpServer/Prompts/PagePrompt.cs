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
		 For the canonical existing-app maintenance flow, read `docs://mcp/guides/existing-app-maintenance`.
		 Use `page-list` first when you need to discover candidate page schemas.
		 Use `{PageGetTool.ToolName}` with `schema-name` `{schemaName}` and `environment-name` `{environmentName ?? "<default or explicit connection args>"}` to inspect the effective merged page structure.
		 Read layout and container hierarchy from `bundle.viewConfig`.
		 When `bundle.viewConfig` contains unfamiliar `crt.*` types, call `{ComponentInfoTool.ToolName}` with `component-type` set to that type before editing nested config or children.
		 Read page metadata from `page`.
		 When you need to edit the page, take the JavaScript payload from `raw.body`, modify that raw body, and send it to `{PageUpdateTool.ToolName}`.
		 Pass `resources` to `{PageUpdateTool.ToolName}` as a valid JSON object string when the edited body contains `#ResourceString(key)#` macros that need child-schema localizable strings.
		 Prefer discover -> inspect -> mutate -> verify for minimal edits.
		 Use `page-sync` only when you need to save multiple pages in one workflow.
		 """;
}
