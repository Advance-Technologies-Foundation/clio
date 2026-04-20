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
		 Before the first page inspection or mutation tool call in a workflow, call `{ToolContractGetTool.ToolName}` with `tool-names` such as `list-pages`, `get-page`, `get-component-info`, `sync-pages`, and `update-page` so the client starts from the authoritative page contract.
		 Use `list-pages` first when you need to discover candidate page schemas by `package-name`, `code`, or `search-pattern`. Skip `list-pages` entirely when the exact schema name is already known — call `get-page` directly with that schema name.
		 Prefer a registered clio environment for page work. If the target site is not registered yet, call `reg-web-app` first and then continue with `environment-name`.
		 Use direct connection args only when local bootstrap is broken or you are in an emergency recovery flow.
		 Use `{PageGetTool.ToolName}` with `schema-name` `{schemaName}` and `environment-name` `{environmentName ?? "<registered environment name>"}` to inspect the effective merged page structure.
		 `{PageGetTool.ToolName}` writes three files to `.clio-pages/<schema-name>/`: `body.js` (editable body), `bundle.json` (merged hierarchy), `meta.json` (page metadata). Read them via `files.bodyFile`, `files.bundleFile`, `files.metaFile` in the response.
		 Read layout and container hierarchy from `bundle.json` at `files.bundleFile`.
		 When `bundle.json` contains unfamiliar `crt.*` types, call `{ComponentInfoTool.ToolName}` with `component-type` set to that type before editing nested config or children.
		 Read page metadata from `files.metaFile`, and treat `files.bodyFile` as the editable JavaScript source of truth.
		 Do not send bundle data back to page tools; only the content of `body.js` is the writable payload.
		 When you need to edit the page, read `files.bodyFile`, modify it, and send the updated body through `{PageSyncTool.ToolName}` as the canonical page write path.
		 Keep `{PageSyncTool.ToolName}` `validate` at its default `true`, and enable `verify` only when the workflow needs explicit server read-back inside the same tool call.
		 Pass `resources` as a valid JSON object string when the edited body contains `#ResourceString(key)#` macros that need child-schema localizable strings; do not send a nested object payload there.
		 Use `{PageUpdateTool.ToolName}` only as a fallback for single-page dry-run or legacy save workflows.
		 For standard data-bound form fields, bind `control` or `value` directly to `$Name` or `$PDS_*` attributes and prefer datasource captions like `$Resources.Strings.PDS_UsrStatus`.
		 Do not use proxy bindings like `$UsrStatus -> PDS.UsrStatus` for standard fields, and do not rely on `#ResourceString(Usr*_label)#` shortcuts for data-bound field captions.
		 Reserve `Usr*_label` and `Usr*_caption` resource keys for custom standalone UI that carries explicit `resources` entries.
		 Prefer `list-pages -> get-page -> sync-pages -> get-page` for canonical page edits.
		 """;

	/// <summary>
	/// Builds a prompt for creating a new Freedom UI page from a supported template.
	/// </summary>
	[McpServerPrompt(Name = PageCreateTool.ToolName), Description("Prompt to create a new Freedom UI page from a supported template")]
	public static string CreatePage(
		[Required] [Description("New page schema name, e.g. 'UsrMyApp_BlankPage'")] string schemaName,
		[Required] [Description("Target package name")] string packageName,
		[Description("Template name or UId (optional; resolve via list-page-templates first if unknown)")] string? template = null,
		[Description("Optional Creatio environment name")] string? environmentName = null) =>
		$"""
		 For the canonical existing-app maintenance flow, read `docs://mcp/guides/existing-app-maintenance`.
		 For the canonical page-creation matrix and validation rules, read `docs://mcp/guides/page-creation`.
		 Before the first page-creation tool call in a workflow, call `{ToolContractGetTool.ToolName}` with `tool-names` such as `list-page-templates`, `create-page`, and `get-page` so the client starts from the authoritative contract.
		 Always call `{PageTemplatesListTool.ToolName}` first to discover the live template catalog. Pick the `name` (or `uId`) of the desired template for the `template` argument of `{PageCreateTool.ToolName}`.
		 Prefer a registered clio environment for page work. If the target site is not registered yet, call `reg-web-app` first and then continue with `environment-name`.
		 Use `{PageCreateTool.ToolName}` with `schema-name` `{schemaName}`, `template` `{template ?? "<template-name>"}`, `package-name` `{packageName}`, and `environment-name` `{environmentName ?? "<registered environment name>"}` to create the page.
		 `{PageCreateTool.ToolName}` required fields: `schema-name`, `template`, `package-name`. Optional: `caption` (defaults to schema-name), `description`, `entity-schema-name`, `dry-run`.
		 Use `dry-run` to validate inputs (schema-name format, template existence, package existence, schema-name uniqueness, entity-schema existence) without writing to the environment.
		 After a successful `{PageCreateTool.ToolName}`, read the page back with `{PageGetTool.ToolName}` with the same `schema-name` to confirm the created page loads and has the expected parent template.
		 Known failure modes: duplicate schema-name, unknown template (call `{PageTemplatesListTool.ToolName}`), missing package. Each returns a readable `error` in the tool response.
		 Keep created page bodies inherited from the template; add fields or columns with `add-form-fields` / `add-list-columns` or edit the body via `{PageSyncTool.ToolName}` only as a follow-up step.
		 """;
}
