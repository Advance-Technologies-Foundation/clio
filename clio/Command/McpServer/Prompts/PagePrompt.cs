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
		 Before the first page inspection or mutation tool call in a workflow, call `{ToolContractGetTool.ToolName}` with `tool-names` such as `page-list`, `page-get`, `component-info`, `page-sync`, and `page-update` so the client starts from the authoritative page contract.
		 Use `page-list` first when you need to discover candidate page schemas by `package-name`, `app-code`, or `search-pattern`.
		 Use `{PageGetTool.ToolName}` with `schema-name` `{schemaName}` and `environment-name` `{environmentName ?? "<default or explicit connection args>"}` to inspect the effective merged page structure.
		 Read layout and container hierarchy from `bundle.viewConfig`.
		 When `bundle.viewConfig` contains unfamiliar `crt.*` types, call `{ComponentInfoTool.ToolName}` with `component-type` set to that type before editing nested config or children.
		 Read page metadata from `page`, and treat `raw.body` as the editable JavaScript source of truth.
		 Do not send `bundle` or `bundle.viewConfig` back to page tools; only `raw.body` is the writable payload.
		 When you need to edit the page, take the JavaScript payload from `raw.body`, modify that raw body, and send it through `{PageSyncTool.ToolName}` as the canonical page write path.
		 Keep `{PageSyncTool.ToolName}` `validate` at its default `true`, and enable `verify` only when the workflow needs explicit server read-back inside the same tool call.
		 Pass `resources` as a valid JSON object string when the edited body contains `#ResourceString(key)#` macros that need child-schema localizable strings; do not send a nested object payload there.
		 Use `{PageUpdateTool.ToolName}` only as a fallback for single-page dry-run or legacy save workflows.
		 For standard data-bound form fields, bind `control` or `value` directly to `$Name` or `$PDS_*` attributes and prefer datasource captions like `$Resources.Strings.PDS_UsrStatus`.
		 Do not use proxy bindings like `$UsrStatus -> PDS.UsrStatus` for standard fields, and do not rely on `#ResourceString(Usr*_label)#` shortcuts for data-bound field captions.
		 Reserve `Usr*_label` and `Usr*_caption` resource keys for custom standalone UI that carries explicit `resources` entries.
		 Prefer `page-list -> page-get -> page-sync -> page-get` for canonical page edits.
		 """;
}
