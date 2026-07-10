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
		 For the canonical existing-app maintenance flow, call `{GuidanceGetTool.ToolName}` with `name` set to `existing-app-maintenance`.
		 For any page-body modification, call `{GuidanceGetTool.ToolName}` with `name` set to `page-modification` before editing `raw.body`; follow its pre-edit checklist to route localizable strings, converters, handlers, validators, SDK calls, and business-rule requirements to specialized guides.
		 If the page-body task edits `handlers`, you must call `{GuidanceGetTool.ToolName}` with `name` set to `page-schema-handlers` before proposing or applying changes, and you must not author handler changes until that guidance has been read.
		 If the page-body task edits `validators`, you must call `{GuidanceGetTool.ToolName}` with `name` set to `page-schema-validators` before proposing or applying changes, and you must not author validator changes until that guidance has been read; never use handler signatures like `handler(request, next)` inside SCHEMA_VALIDATORS — validators must return a function that accepts a control argument, not a request/next pair.
		 If the requirement is field-value validation such as max/min/length/range/regex, including when the threshold comes from a system setting or other async SDK read, treat it as `validators` work and read `page-schema-validators`, not `page-schema-handlers`.
		 If the requirement changes dynamic `required`, `visible`, or `readonly` state, or you are about to author `crt.HandleViewModelAttributeChangeRequest`, `request.value`, `request.$context`, or `setAttributePropertyValue(...)`, you must call `{GuidanceGetTool.ToolName}` with `name` set to `page-schema-handlers` first and you must not guess handler APIs from memory.
		 If the page-body task adds or edits `@creatio-devkit/common` usage in handlers or validators, you must also call `{GuidanceGetTool.ToolName}` with `name` set to `page-schema-creatio-devkit-common` before authoring `SCHEMA_DEPS`, `SCHEMA_ARGS`, or SDK service calls.
		 Before the first page inspection or mutation tool call in a workflow, call `{ToolContractGetTool.ToolName}` with `tool-names` such as `list-pages`, `get-page`, `get-component-info`, `sync-pages`, and `update-page` so the client starts from the authoritative page contract.
		 Use `list-pages` first when you need to discover candidate page schemas by `package-name`, `code`, or `search-pattern`. Skip `list-pages` entirely when the exact schema name is already known — call `get-page` directly with that schema name.
		 Prefer a registered clio environment for page work. If the target site is not registered yet, call `reg-web-app` first and then continue with `environment-name`.
		 Use direct connection args only when local bootstrap is broken or you are in an emergency recovery flow.
		 Use `{PageGetTool.ToolName}` with `schema-name` `{schemaName}` and `environment-name` `{environmentName ?? "<registered environment name>"}` to inspect the effective merged page structure.
		 `{PageGetTool.ToolName}` writes three files to `.clio-pages/<schema-name>/`: `body.js` (editable body), `bundle.json` (merged hierarchy), `meta.json` (page metadata). Read them via `files.bodyFile`, `files.bundleFile`, `files.metaFile` in the response.
		 Read layout and container hierarchy from `bundle.json` at `files.bundleFile`.
		 When `bundle.json` contains unfamiliar `crt.*` types, call `{ComponentInfoTool.ToolName}` with `component-type` set to that type before editing nested config or children.
		 Resolve the target platform version BEFORE planning any component work: pass `environment-name` (the page's environment) to `{ComponentInfoTool.ToolName}` and read `resolvedFrom` on the response. If the response carries `requiresVersionConfirmation: true` (`resolvedFrom` is `latest-fallback` — the platform version could NOT be determined), STOP and do not silently assume the `latest` component set: tell the user the platform version is unknown and request explicit confirmation before generating an implementation plan or inserting any component. Use `resolvedFromReason` to phrase the ask — `probe-error` is transient (a retry, or a reachable/registered environment, may resolve it), while `no-active-environment` / `core-version-missing` / `core-version-unparseable` need a clearer input such as an explicit version. On `environment-superset`, surface the soft `versionWarning` and verify critical component types against the target before committing to a plan.
		 Before inserting ANY new component, call `{ComponentInfoTool.ToolName}` with `component-type` set to the exact type you intend to use (and `environment-name` set to the page's environment) and confirm it exists: `{PageSyncTool.ToolName}`/`update-page` accept an unknown `crt.*` type and report success, but Freedom UI renders it as a broken placeholder box. Never invent or guess a component type from memory. If no existing component matches the requirement, stop and ask the user whether to use one of the existing components or build a custom one — do not silently substitute or fabricate a type.
		 Read page metadata from `files.metaFile`, and treat `files.bodyFile` as the editable JavaScript source of truth.
		 Do not send bundle data back to page tools; only the content of `body.js` is the writable payload.
		 When you need to edit the page, read `files.bodyFile`, modify it, and send the updated body through `{PageSyncTool.ToolName}` as the canonical page write path.
		 Keep `{PageSyncTool.ToolName}` `validate` at its default `true`, and enable `verify` only when the workflow needs explicit server read-back inside the same tool call.
		 Both `{PageSyncTool.ToolName}` and `{PageUpdateTool.ToolName}` run a deterministic AST lint pass on every web page body after the regex validators and before sampling. The lint pass ships rules that have no regex counterpart — duplicate detection is intentionally NOT shipped (the regex layer catches anti-shapes like array/object mismatch on `handlers`/`validators`/`converters`, empty validator `params`, and the forbidden `request.viewModel` / `request.sender` / `request.$context.get` / `request.$get` / `request.$set` handler-API patterns with its own established wording). Error findings block the save (body is not sent to Creatio): custom converter names cannot use the reserved `crt.*` prefix, validator returns must not be literal `true` / `false` / string / empty object (null/undefined are allowed and signal "no error"), and bodies whose AST nests deeper than the safe traversal cap are rejected as `body-too-deeply-nested`. Warning findings are reported but do not block: direct `fetch(...)` inside a converter, and `request.$context.executeRequest(...)` in handlers (not part of the documented @creatio-devkit/common public API — prefer `sdk.HandlerChainService.instance.process(...)` from `@creatio-devkit/common`). When a finding fires, fix the body and re-read the matching `page-schema-handlers` / `page-schema-validators` / `page-schema-converters` guidance — never bypass by stripping the offending block.
		 Before passing `resources`, you must call `{GuidanceGetTool.ToolName}` with `name` set to `page-schema-resources`. The body containing `$Resources.Strings.*` or `#ResourceString(...)#` is NOT sufficient justification. The guide specifies both the decision algorithm (when to register vs skip) and the required payload shape.
		 Use `{PageUpdateTool.ToolName}` only as a fallback for single-page dry-run or legacy save workflows.
		 EXTERNAL-MODIFICATION CONFLICTS: `{PageGetTool.ToolName}` stores a checksum baseline in `meta.json`; `{PageSyncTool.ToolName}` and `{PageUpdateTool.ToolName}` compare it against the server before saving. A write that fails with `conflict: true` (per-page `conflict`/`conflict-details` in sync-pages) means the page was modified outside this session — do NOT retry with the same body: re-run `{PageGetTool.ToolName}`, re-apply your change on top of the fresh body (preserve the user's external edits — never restore components they deleted), then retry. Set `force: true` ONLY after informing the user about the external changes and receiving their explicit confirmation to overwrite.
		 For standard data-bound form fields, bind `control` or `value` to the declared view-model attribute from `viewModelConfig` / `viewModelConfigDiff` and prefer datasource captions keyed by the view-model attribute name,
		 i.e. `$Resources.Strings.<bindingAttribute>` — the SAME attribute the control binds to, which must have a DS-bound `modelConfig.path` (e.g. `$Resources.Strings.PDS_UsrStatus` for a `$PDS_UsrStatus` control, or `$Resources.Strings.Name` for `$Name`).
		 The platform auto-provides the caption from the bound column. The bare entity column code is NOT auto-provided unless it equals the attribute name; on `operation:"insert"` a label that is neither the binding-attribute key nor registered is rejected.
		 If validator or handler logic moves to a different declared attribute for the same field, rebind the control to that same attribute. If the control is inherited from a parent schema and there is no local `viewConfigDiff` item for it yet, add one local `merge` for that control name.
		 Do not infer correctness from naming patterns such as `$PDS_*`, and do not rely on `#ResourceString(Usr*_label)#` shortcuts for data-bound field captions.
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
		 `{PageCreateTool.ToolName}` required fields: `schema-name`, `template`, `package-name`. Optional: `caption` (defaults to schema-name), `description`, `entity-schema-name`, `optional-properties` (JSON array of key/value objects seeded into the schema).
		 To create a DASHBOARD, call `{GuidanceGetTool.ToolName}` with `name` set to `dashboard-creation` FIRST, then `{PageCreateTool.ToolName}` with `template` `BaseDashboardTemplate` and its link-back properties (DashboardsEntitySchemaName / DashboardsElementName / DashboardsClientUnitSchemaUId) passed through `optional-properties`.
		 `{PageCreateTool.ToolName}` validates inputs (schema-name format, template existence, package existence, schema-name uniqueness, entity-schema existence) before calling the designer service; invalid inputs fail fast with a readable error.
		 After a successful `{PageCreateTool.ToolName}`, read the page back with `{PageGetTool.ToolName}` with the same `schema-name` to confirm the created page loads and has the expected parent template.
		 Known failure modes: duplicate schema-name, unknown template (call `{PageTemplatesListTool.ToolName}`), missing package. Each returns a readable `error` in the tool response.
		 Keep created page bodies inherited from the template; add fields or columns by editing the `body.js` returned from `{PageGetTool.ToolName}`, validate with `validate-page`, and persist the body via `{PageSyncTool.ToolName}` only as a follow-up step.
		 Detect the connected user's profile language ONCE per session via `get-user-culture` and reuse it for the page caption; if it returns `success:false`, ASK the user which language to use — do NOT silently use the host locale or `en-US`. Override per call with `caption-culture`.
		 """;
}
