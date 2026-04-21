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
		       - `raw.body` — full JavaScript body of the replacing schema (with markers). Use this as the editable source.
		       - `bundle` — read-only merged view across the full hierarchy. Do not send `bundle` or `bundle.viewConfig` as the body payload.

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
		       - Never guess a container name. Walk the `bundle.viewConfig` tree from `get-page` to find valid `parentName` values.
		       - Each node in `bundle.viewConfig` has `type`, `items[]` (children), and either `name` or implicit position.
		       - Common Freedom UI containers: `FilterGridContainer` (list page filter row), `GridContainer` (column-aligned layout), `CardContainer` (form page), `ActionButtonsContainer`, `GridDetailAddRecordButtonsContainer`.
		       - Pick a container whose children list already holds similar siblings (e.g. an existing button next to where you want the new one).

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
		       2. `get-page schema-name=Accounts_ListPage` → response contains `bundle.viewConfig` (to find container names) and `raw.body` (empty replacing template if no replacement exists yet).
		       3. Locate container: walk `bundle.viewConfig` for `FilterGridContainer` or similar.
		       4. Compose body: start from `raw.body` (or the template above), add button entry to `viewConfigDiff`, add matching handler to `handlers`.
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
