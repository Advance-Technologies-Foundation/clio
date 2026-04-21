using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for Freedom UI page modification through clio MCP.
/// </summary>
[McpServerResourceType]
public sealed class PageModificationGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/page-modification";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP page modification guide

		       Replacing-schema concept
		       - When a Freedom UI designer saves changes to a page, Creatio creates a replacing schema in a "design package".
		       - The replacing schema inherits from the original and contains only the diff applied by the designer.
		       - The design package is NOT the same as the package that owns the original page schema.
		       - `get-page` and `update-page` automatically resolve the replacing schema through the design package.
		       - The editable target is always `hierarchy[0]` — the most-derived schema in the hierarchy.
		       - If no replacing schema exists, `hierarchy[0]` is the original schema and the design package is the original package.

		       Canonical page modification flow
		       1. `list-pages` — discover the page schema name.
		       2. `get-page` — read `raw.body` (the editable replacing schema body) and `page` metadata.
		       3. Edit `raw.body` as needed.
		       4. `update-page` or `sync-pages` — save the modified body back.
		       5. Optionally use `verify: true` in `update-page` to read back the page metadata after saving.

		       get-page response structure
		       - `page` — metadata of the editable replacing schema: `schemaName`, `schemaUId`, `packageName`, `packageUId`, `parentSchemaName`.
		       - `raw.body` — full JavaScript body of the replacing schema (with markers). Read-only reference; see the CRITICAL warning below before reusing it as the write payload.
		       - `bundle` — read-only merged view across the full hierarchy. Do not send `bundle` or `bundle.viewConfig` as the body payload.
		       - `bundle.containers` — flat list of usable `parentName` values.

		       update-page write modes
		       - `mode: "replace"` (default): the body you pass replaces the schema body verbatim. Use only when composing the full schema body from scratch.
		       - `mode: "append"`: clio loads the current schema body from the server, merges your incoming body fragment with it, and saves the merged result. Use this mode when ADDING a component/handler/config to an existing customized page — it is the safe choice when `ownBodySummary.viewConfigDiffOperations > 0`.
		       - Merge rules (append mode):
		         * `viewConfigDiff` — concat + dedupe by `name` (incoming wins)
		         * `handlers` — concat + dedupe by `request` string (incoming wins)
		         * `viewModelConfigDiff` / `modelConfigDiff` — plain concat (no dedupe)
		       - In append mode the incoming body still needs the full marker envelope (all six marker pairs present, even if some sections are empty `[]` or `{}`).

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

		       Virtual package materialization (first-time replacing creation)
		       - When the target page has no replacing schema in the design package yet, `update-page` automatically creates one.
		       - Behind the scenes: clio builds a fresh DTO (new Guid uId, design-package, parent=original, extendParent=true) and SaveSchema materializes the virtual package in DB on first save.
		       - No extra input from the caller — just call `update-page schema-name=<platform page>` with a replacing-style body and the backend creates the replacement.

		       Finding a container for a new component (parentName)
		       - Never guess a container name. Use `bundle.containers` from `get-page` — a flat list of all containers discovered in `viewConfig`.
		       - Each entry exposes: `name` (value to use as `parentName`), `type` (e.g. `crt.FlexContainer`, `crt.Grid`), `childCount` (existing siblings), `path` (ancestor chain, useful for disambiguation when the same `name` appears in multiple branches).
		       - Pick a container whose `path` matches the visual region you want to modify and whose `childCount` > 0 for consistency (existing sibling confirms the container is usable).
		       - Fallback: walk `bundle.viewConfig` tree manually when `bundle.containers` is empty (possible for pages built entirely via diffs without a root viewConfig node).
		       - Common Freedom UI container types: `crt.FlexContainer` (filter rows, action bars), `crt.Grid` (column layouts), `crt.TabContainer`, `crt.Expansion`.

		       Adding a button with a click handler
		       Body structure for `update-page` (preserve all marker pairs — do not remove or reorder them):

		       ```
		       define("<PageName>", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {
		           return {
		               viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
		                   {
		                       "operation": "insert",
		                       "name": "UsrMyButton",
		                       "values": {
		                           "type": "crt.Button",
		                           "caption": "My button",
		                           "clicked": { "request": "usr.MyClickRequest" }
		                       },
		                       "parentName": "FilterGridContainer",
		                       "propertyName": "items",
		                       "index": 0
		                   }
		               ]/**SCHEMA_VIEW_CONFIG_DIFF*/,
		               viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
		               modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
		               handlers: /**SCHEMA_HANDLERS*/[
		                   {
		                       request: "usr.MyClickRequest",
		                       handler: async (request) => {
		                           alert("My button clicked");
		                           return request.next?.handle(request);
		                       }
		                   }
		               ]/**SCHEMA_HANDLERS*/,
		               converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
		               validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
		           };
		       });
		       ```

		       Rules for the handlers block
		       - Contents between `/**SCHEMA_HANDLERS*/` markers is raw JavaScript, NOT JSON. Use unquoted keys (`request`, `handler`) and arrow functions or `function` expressions.
		       - Every `viewConfigDiff` entry whose `values.clicked.request` is `usr.*` (or any custom namespace) MUST have a matching `handler` entry with the same `request` string.
		       - Always end custom handlers with `return request.next?.handle(request);` to propagate to the default pipeline. Omitting it breaks page lifecycle events.
		       - Built-in requests (`crt.*`) already have default handlers — don't duplicate them unless you intend to override.

		       Rules for viewConfigDiff
		       - `operation` must be one of: `insert`, `remove`, `merge`, `move`.
		       - `name` is the unique component id inside the hierarchy. Prefix custom components with `Usr` or project-specific prefix to avoid collisions.
		       - `parentName` must match an existing container name from `bundle.viewConfig`.
		       - `propertyName` is usually `items` for containers.
		       - `index` is the insertion position within `parentName.items[]`.

		       Canonical flow to add a Test button to Accounts_ListPage
		       1. `list-pages filter=Accounts_List` → resolve schema name.
		       2. `get-page schema-name=Accounts_ListPage` → response contains `bundle.containers` (flat list of valid parentName values) and `raw.body` (empty replacing template if no replacement exists yet).
		       3. Pick a container from `bundle.containers`: filter by `type == "crt.FlexContainer"` and non-zero `childCount`; use its `name` as `parentName`.
		       4. Compose body: start from `raw.body` (or the template above), add button entry to `viewConfigDiff` with the chosen `parentName`, add matching handler to `handlers`.
		       5. `update-page schema-name=Accounts_ListPage body=<composed body> verify:true`.
		       6. Response includes `page.schemaUId` — the newly-materialized replacing schema in the design package.

		       Known limitations
		       - `update-page` fail-closed on design-package resolution: if `GetDesignPackageUId` fails for a write, the call returns an error instead of silently falling back to the original package.
		       - `get-page` uses a best-effort fallback to the original package if design-package resolution fails, because reads are non-destructive.
		       - Replacing schemas outside the design package (for example, manually created overrides in other packages) are not visible through `GetDesignPackageUId`. Use `list-pages` to find the correct schema name.
		       - `update-page` does NOT support handler JSON — handlers must be written as raw JavaScript inside `SCHEMA_HANDLERS` markers.
		       - The handler block is not JS-syntax-validated beyond Acorn parsing; semantic errors (wrong argument names, missing `await`) surface only when the page is loaded in the browser.
		       """
	};

	/// <summary>
	/// Returns the canonical guidance article for Freedom UI page modification.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "page-modification-guidance")]
	[Description("Returns canonical MCP guidance for Freedom UI page modification, replacing-schema concepts, optional-properties, and verify round-trip.")]
	public ResourceContents GetGuide() => Guide;
}
