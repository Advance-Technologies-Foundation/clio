using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Focused sub-guide of the <c>page-modification</c> family: the page-body save lifecycle — the
/// canonical read/edit/write flow, replacing-schema concept, design-package resolution, update-page
/// write modes, external-modification conflicts, body formatting, and known limitations.
/// </summary>
[McpServerResourceType]
public sealed class PageModificationOverviewGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/page-modification-overview";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP page modification overview guide

		       This is a focused sub-guide of `page-modification`. Read `page-modification` FIRST and follow its
		       pre-edit GATE checklist; this guide owns the page-body SAVE LIFECYCLE — how get-page / update-page /
		       sync-pages read, merge, and persist a body. For the data-bound field contract read
		       `page-modification-field-contract`; for container / parentName selection read
		       `page-modification-containers`; for buttons, handlers, and viewConfigDiff insert rules read
		       `page-modification-components`.

		       Canonical page modification flow
		       0. WEB, MOBILE, OR BOTH (see Step 0 in `page-modification`): decide whether the requirement targets web, mobile, or both (default to web if unspecified); edit each affected page's web and/or mobile variant accordingly.
		       1. `list-pages` — discover the page schema name.
		       2. `get-page` — read `raw.body` (the editable replacing schema body) and `page` metadata.
		       3. `get-component-info` — for each component type you will insert, call it for that exact type and treat its response (including its embedded `documentation`) as the authoritative source for how to insert and configure the component. Build the insert from that response, not from this guide, from memory, or by copying a deployed page's compiled bundle: a bundle shows components already expanded as plain `viewConfigDiff` elements and hides the catalog-only signals (`compositeOnly`, `appliesToCustomEntities`, the composite recipe, `references.docs[]`), so reverse-engineering one is NOT a substitute for `get-component-info` and silently lands implementations off-spec. See the COMPONENT-TYPE VERIFICATION step in `page-modification`.
		       4. Edit `raw.body` as needed.
		       5. `update-page` or `sync-pages` — save the modified body back.
		       6. Optionally use `verify: true` in `update-page` to read back the page metadata after saving.

		       Replacing-schema concept
		       - When a Freedom UI designer saves changes to a page, Creatio creates a replacing schema in a "design package".
		       - The replacing schema inherits from the original and contains only the diff applied by the designer.
		       - The design package is NOT the same as the package that owns the original page schema.
		       - `get-page` and `update-page` automatically resolve the replacing schema through the design package.
		       - The editable target is always `hierarchy[0]` — the most-derived schema in the hierarchy.
		       - If no replacing schema exists, `hierarchy[0]` is the original schema and the design package is the original package.

		       get-page response structure
		       - `page` — metadata of the editable replacing schema: `schemaName`, `schemaUId`, `packageName`, `packageUId`, `parentSchemaName`.
		       - `raw.body` — full JavaScript body of the replacing schema (with markers). Read-only reference; see the CRITICAL warning below before reusing it as the write payload.
		       - `bundle` — read-only merged view across the full hierarchy. Do not send `bundle` or `bundle.viewConfig` as the body payload. (For the detailed bundle.json shape and jq recipes, read `page-modification-containers`.)

		       Design-package resolution (trust the backend)
		       - `get-page` returns `page.designPackageUId` — the package where `update-page` will save. `page.willCreateReplacingInDesignPackage: true` means a NEW replacing schema will be materialized on save (the package itself is virtual until then; the backend creates it from the cached `AppVirtualPackageInfo` at SaveSchema time).
		       - The backend resolves the design package deterministically from the locked schema's owning app via `SysPackageInInstalledApp` — there is no per-user "active app" to manage and no ambiguity between installed apps.
		       - Do NOT pass `target-package-uid` in normal flows. The override exists for niche scenarios where you have already discovered a specific replacing schema (for example via `list-pages` filtered by name) and want to bypass hierarchy resolution; otherwise, omit it and let the backend pick.

		       Virtual package materialization (first-time replacing creation)
		       - When the target page has no replacing schema in the design package yet, `update-page` automatically creates one.
		       - Behind the scenes: clio builds a fresh DTO (new Guid uId, design-package, parent=original, extendParent=true) and SaveSchema materializes the virtual package in DB on first save.
		       - No extra input from the caller — just call `update-page schema-name=<platform page>` with a replacing-style body and the backend creates the replacement.

		       update-page write modes
		       - `mode: "replace"` (default): the body you pass replaces the schema body verbatim. Use only when composing the full schema body from scratch. All six marker pairs must be present.
		       - `mode: "append"`: clio loads the current schema body from the server, merges your incoming body fragment with it, and saves the merged result. Use this mode when ADDING a component/handler/config to an existing customized page — it is the safe choice when `ownBodySummary.viewConfigDiffOperations > 0`.
		       - Merge rules (append mode):
		         * `viewConfigDiff` — concat + dedupe by `name` (incoming wins)
		         * `handlers` — concat + dedupe by `request` string (incoming wins)
		         * `converters` — merge object by key (incoming wins)
		         * `viewModelConfigDiff` / `modelConfigDiff` — plain concat (no dedupe)
		       - Append does NOT support a body in the full `viewModelConfig`/`modelConfig` form (instead of the `*Diff` form); use replace mode for such bodies.
		       - Modifying an existing component: edit the operation that introduces it. Changing an own-body `insert` to a same-`name` `merge`/`move`/`remove` discards the insert, so unless a parent schema inserts that name the component is orphaned (disappears at runtime). Identify own-body inserts via `ownBodySummary.viewConfigDiffOps` (`operation: insert`) and edit their `values`; reserve `merge`/`move`/`remove` for parent-introduced elements. `update-page`/`sync-pages` warn (advisory, never block) when they detect this against the prior body.
		       - Append mode is permissive about the incoming body: pass only the sections you want to merge (for example, just `SCHEMA_VIEW_CONFIG_DIFF` + `SCHEMA_HANDLERS`). Missing sections are skipped; the current body's values stay intact for those sections. No need to pad with empty `[]` / `{}` markers.
		       - Never invent custom markers (for example `SCHEMA_WRAPPERS` is not a valid marker). Stick to: `SCHEMA_DEPS`, `SCHEMA_ARGS`, `SCHEMA_VIEW_CONFIG_DIFF`, `SCHEMA_VIEW_MODEL_CONFIG_DIFF`, `SCHEMA_MODEL_CONFIG_DIFF`, `SCHEMA_HANDLERS`, `SCHEMA_CONVERTERS`, `SCHEMA_VALIDATORS`. Static-form FormPage bodies (see "Static vs diff body forms" in `page-modification-field-contract`) instead carry `SCHEMA_VIEW_MODEL_CONFIG` and `SCHEMA_MODEL_CONFIG` (no `_DIFF`) in place of the two `_DIFF` markers — those are equally valid; preserve whichever pair the body you read from `get-page` already uses, and do not convert one form to the other.

		       CRITICAL — do NOT resend the full raw.body as the update-page body payload
		       - `raw.body` contains the schema's own existing viewConfigDiff operations (existing merges/inserts). Re-sending it makes the backend re-apply those merges against the current parent hierarchy, and one of them typically fails with
		         "The requested operation requires an element of type 'Object', but the target element has type 'Array'".
		       - Correct pattern: compose a MINIMAL body that contains only the NEW operations you are adding (for example, one `insert` for the new button and one handler entry), wrapped in the six required marker pairs.
		       - The backend treats the saved body as the complete schema body, but for an existing replacing schema the incremental-save approach (minimal new ops only) is the only reliable way to add a single component without breaking existing inherited merges.
		       - Sanity check before sending: inspect `page.ownBodySummary.viewConfigDiffOperations` from the `get-page` response. If it is greater than 1, `raw.body` already holds existing operations — DO NOT resend it. Compose a minimal body with only the new ops.
		       - Fast rule: if `ownBodySummary.bodyLength` > 1000 characters, send only the delta, never the whole body.

		       update-page optional-properties
		       - Pass `optional-properties` as a JSON array of `{key, value}` objects to merge into `schema.optionalProperties`.
		       - Example: `[{"key":"entitySchemaName","value":"UsrMyEntity"}]`
		       - The merge is keyed on `key` (case-insensitive). Existing entries with the same key are replaced; others are preserved.
		       - `optional-properties` is validated as a JSON array before the save attempt. Invalid JSON fails the call.

		       update-page verify flag
		       - When `verify: true`, `update-page` reads the page back after saving and returns `page` metadata in the response.
		       - Verify is best-effort: if the read-back fails, the update response still reports `success: true`.
		       - Use `verify: true` when you need page metadata (schema name, package, UId) in the same call as the save.

		       sync-pages optional-properties
		       - Each page entry in `sync-pages` also accepts `optional-properties` with the same JSON array semantics.
		       - Applies per-page; different pages in the same sync call may carry different optional-properties.

		       External-modification conflicts (checksum baseline)
		       - `get-page` stores a checksum baseline of the editable schema in `.clio-pages/{schema}/meta.json`. `update-page` and `sync-pages` automatically compare that baseline against the server BEFORE saving (same environment only).
		       - If the page was modified outside your session (the user edited it in the Creatio designer, deleted a component you added, another tool saved it), the write FAILS with `conflict: true` and `conflictDetails.reason`: `checksum-mismatch`, `schema-created-externally`, `schema-deleted-externally`, or `schema-uid-mismatch`. In `sync-pages` the conflict is per page (`conflict` / `conflict-details` on the page result) and the rest of the batch continues.
		       - RECOVERY — follow exactly this order:
		         1. Do NOT retry with the same body. Your local view of the page is stale.
		         2. Re-run `get-page` for the schema — this reloads body.js/bundle.json AND refreshes the baseline.
		         3. Inspect what changed externally, re-apply your intended change on top of the FRESH body (respect the user's external edits — do not restore components the user deleted).
		         4. Retry `update-page` / `sync-pages`.
		       - `force: true` (per page in `sync-pages`) skips the check and deliberately overwrites the external changes. Use it ONLY after you have informed the user about the external modifications and they explicitly confirmed overwriting them. Never set force pre-emptively.
		       - After a successful save the baseline refreshes automatically — consecutive updates in the same session do not false-conflict.
		       - No baseline (no prior `get-page`, legacy meta.json, different environment) → the check is skipped; flows behave exactly as before.
		       - A small race window between the check and the save remains (last write wins) — the check is a guard, not a transaction.

		       Body formatting
		       - clio does NOT normalize or re-indent page bodies — the string you pass is saved verbatim.
		       - When adding new content, match the indentation style already present in the page body (tabs, 2 spaces, 4 spaces, etc.).
		       - Do NOT reformat existing code returned by `get-page`. Preserve original whitespace and only modify targeted sections.
		       - Single-line or dense JSON/JS in newly authored content is unacceptable: it blocks human review and produces unreadable diffs.
		       - If the page body is empty or brand-new (no existing style to match), default to tab indentation.

		       Known limitations
		       - `update-page` fail-closed on design-package resolution: if `GetDesignPackageUId` fails for a write, the call returns an error instead of silently falling back to the original package.
		       - `get-page` uses a best-effort fallback to the original package if design-package resolution fails, because reads are non-destructive.
		       - Replacing schemas outside the design package (for example, manually created overrides in other packages) are not visible through `GetDesignPackageUId`. Use `list-pages` to find the correct schema name.
		       - `update-page` does NOT support handler JSON — handlers must be written as raw JavaScript inside `SCHEMA_HANDLERS` markers.
		       - The handler block is not JS-syntax-validated beyond Acorn parsing; semantic errors (wrong argument names, missing `await`) surface only when the page is loaded in the browser.
		       - ListPage DataGrid sorting: use `viewModelConfigDiff` via `Items.modelConfig.sortingConfig.attributeName` pointing to a sibling attribute (e.g. `ItemsSorting`) with sort options that use entity column names and `direction: asc/desc/none`. Do not insert `viewConfig.sorting` or `viewConfig.sortingChange` manually — the frontend preprocessor auto-injects them from `sortingConfig`.
		       """
	};

	/// <summary>
	/// Returns the canonical page-body save-lifecycle sub-guide of the page-modification family.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "page-modification-overview-guidance")]
	[Description("Returns the page-body save lifecycle sub-guide of the page-modification family: canonical get-page/update-page/sync-pages flow, replacing-schema concept, design-package resolution, write modes, external-modification conflicts, body formatting, and known limitations.")]
	public ResourceContents GetGuide() => Guide;
}
