using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for adding a related/child list (a "detail") to a Freedom UI
/// page and filtering it by the current page record (the master-detail "filter by page data" pattern).
/// </summary>
[McpServerResourceType]
public sealed class RelatedListGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/related-list";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP related list guide

		       Use this guide whenever you add a related/child list — a "detail" — to a Freedom UI record page
		       (FormPage), or whenever a list on a page must show only the records that belong to the current
		       record. This is the "filter the list by page data" requirement: a child list scoped to the master
		       record open on the page.

		       Before you author or edit a detail, fetch the structure from `get-component-info`: the full
		       "Expanded list" recipe with `composite="Expanded list"`. Read it in full. Also read `page-modification`
		       and its sub-guides (`page-modification-overview` for body markers / append vs replace,
		       `page-modification-field-contract` for static-vs-diff body forms, `page-modification-containers` for
		       container selection). Those are the source of truth for the component/composite shapes; this guide owns
		       the master-detail WIRING that connects them — the part AI most often gets wrong.

		       The headline rule — scope a detail with `modelConfig.dependencies`, NOT a handler
		       The platform filters a child list by the open record DECLARATIVELY. You declare the child→master
		       foreign-key relationship in `modelConfig.dependencies`, and the runtime builds the ESQ filter for
		       you AND waits for the master record to load before it does (so there is no timing race to solve).
		       This is exactly what the Freedom UI page designer emits for every out-of-the-box detail — every OOB
		       FormPage with a detail ships `handlers: []`. Do NOT hand-write a `crt.HandleViewModelInitRequest`
		       handler, a seeded empty-Guid filter, or a `filterAttributes` entry to scope by the record — those
		       are not how the platform does it and they break (see Common mistakes).

		       There is NO single "detail" component — the "Expanded list" composite (fetched above) is the
		       structure; do not look for a single "detail" component type or hand-assemble one from raw types.
		       - Slot-init footgun (the #1 detail break): every container node you INSERT in `viewConfigDiff` MUST
		         initialize its content-slot array in `values` (e.g. `"items": []`); the recipe and
		         `page-modification` show the exact slots per container. A slot-less container is not treated as a
		         container, so the page fails at RUNTIME with `Error: Item "<name>" is not a container for other
		         items` and the whole record card stops rendering. `update-page` still returns `success: true` —
		         the break only surfaces in the browser, so always reload and verify the page renders (and the
		         detail's tab appears) after saving.

		       Filter the list by page data (the master-detail pattern) — three declarative edits
		       A detail is scoped to the current record by a child entity data source, a collection attribute the
		       grid binds to, and a `dependencies` entry that links the child foreign-key column to the master
		       record. No handler, no seeded filter, no `filterAttributes`:

		       1. A child `crt.EntityDataSource` under `modelConfig.dataSources`, `scope: "viewElement"`, with the
		          child `entitySchemaName` and the columns you display under `config.attributes` (e.g. data source
		          `UsrContactDS` over entity `UsrContact`). The foreign-key column does NOT need to be listed in
		          `config.attributes` — the dependency references it directly.
		       2. A COLLECTION attribute (`"isCollection": true`) under `viewModelConfig.attributes` whose
		          `modelConfig.path` points at that data source. The grid `items` binds to this attribute
		          (`$UsrContactGrid`). List the child columns under the collection attribute's own
		          `viewModelConfig.attributes`. Do NOT add a `filterAttributes` entry here for record scoping.
		       3. A `dependencies` entry under `modelConfig.dependencies` keyed by the child data-source name. Its
		          value is an array of `{ "attributePath": "<ChildForeignKeyColumn>", "relationPath": "PDS.Id" }`.
		          `attributePath` is the CHILD entity's foreign-key column that points back at the master (the bare
		          reference column name, NOT a `...Id` suffix form). `relationPath` is the MASTER data source's id
		          attribute — `"PDS.Id"` when the page's `primaryDataSourceName` is `PDS` (use
		          `"<primaryDataSourceName>.Id"` otherwise). The runtime resolves the open record's id from this
		          path, waits for the master model to load, and applies the filter automatically.

		       Canonical payload (a Contact detail on an Account/Client FormPage, scoped to the open record via the
		       child FK column `UsrClient`). Shown as the static full-body form. Note `handlers: []` — there is no
		       init handler and no seeded filter; the `dependencies` entry is the whole scoping mechanism:

		       ```jsonc
		       // viewModelConfig.attributes — the collection attribute the grid binds to ($UsrContactGrid):
		       "UsrContactGrid": {
		         "isCollection": true,
		         "modelConfig": {
		           "path": "UsrContactDS"
		         },
		         "viewModelConfig": {
		           "attributes": {
		             "UsrContactDS_Id":         { "modelConfig": { "path": "UsrContactDS.Id" } },
		             "UsrContactDS_UsrName":    { "modelConfig": { "path": "UsrContactDS.UsrName" } },
		             "UsrContactDS_UsrJobTitle": { "modelConfig": { "path": "UsrContactDS.UsrJobTitle" } }
		           }
		         }
		       }

		       // modelConfig.dataSources — the child entity data source:
		       "UsrContactDS": {
		         "type": "crt.EntityDataSource",
		         "scope": "viewElement",
		         "config": {
		           "entitySchemaName": "UsrContact",
		           "attributes": {
		             "UsrName":     { "path": "UsrName" },
		             "UsrJobTitle": { "path": "UsrJobTitle" }
		           }
		         }
		       }

		       // modelConfig.dependencies — links the child FK column to the open master record (NO handler):
		       "dependencies": {
		         "UsrContactDS": [
		           { "attributePath": "UsrClient", "relationPath": "PDS.Id" }
		         ]
		       }
		       ```

		       In a diff-form (replacing/diff body), carry the same three pieces through diff entries instead of a
		       static body (see `page-modification` for the static-vs-diff body decision):
		       - `viewModelConfigDiff`: `{ "operation": "merge", "path": ["attributes"], "values": { "UsrContactGrid": { ... } } }`
		       - `modelConfigDiff`: one `{ "operation": "merge", "path": ["dataSources"], "values": { "UsrContactDS": { ... } } }`
		         and one `{ "operation": "merge", "path": ["dependencies"], "values": { "UsrContactDS": [ { "attributePath": "UsrClient", "relationPath": "PDS.Id" } ] } }`

		       Wiring rules
		       - `attributePath` is the CHILD entity's foreign-key column that points back at the master (here
		         `UsrClient` on `UsrContact`), NOT a path on the master entity and NOT a `...Id` suffix form. Use
		         the bare reference column name.
		       - `relationPath` is the master data source's id attribute, normally `"PDS.Id"`. It must start with
		         the page `primaryDataSourceName` (the data source whose `scope` is `page`). A detail can also be
		         scoped to a non-id master column (e.g. `"PDS.Account"`) when the relationship runs through that
		         column.
		       - Leave `handlers: []`. The dependency is evaluated on first load and recomputed whenever the master
		         id changes, so switching the open record re-scopes the child list with no handler.
		       - The grid `items` binding and the panel/grid `viewConfigDiff` inserts are normal page edits — fetch
		         the structure with `get-component-info composite="Expanded list"` (the canonical recipe), and see
		         `page-modification-components` for `parentName`/`propertyName`/`index` placement and `get-component-info` for
		         `columns`, `features`, and toolbar slots.

		       Adding records to the detail — inline grid add is the DEFAULT; a header "Add" button needs a resolvable page
		       - DEFAULT, always safe: enable INLINE add on the inner `crt.DataGrid` (read-only by default). Fetch
		         the exact editable flags from `get-component-info crt.DataGrid` (the grid `features` that enable
		         editing and inline row creation). The grid then renders an add row; the new record inherits the
		         master foreign key from the `dependencies` relationship, so it is scoped to the open record with
		         NO separate page and NO navigation. This works for ANY child entity — INCLUDING a standalone
		         detail entity that has no section. Prefer this whenever the requirement is "add a related item" /
		         "an Add button"; the inline add row IS the add affordance the user asked for.
		       - REQUIRED wiring for inline add to SAVE: the child FK column (the `attributePath` reference column,
		         here `UsrContact.UsrClient`) MUST be one of the grid's columns / collection attributes. The
		         `dependencies` relationship populates the FK on the new row only when that column is present in the
		         grid; omit it and the inline add row's parent FK stays empty, so the save fails with the runtime error
		         "<FK caption> field must be filled in" (a detail FK is normally required). Add the FK column to the
		         grid — it may be hidden in the UI, but it must be in the collection so inline create inherits the
		         open master (confirm via OData that the child's `...Id` equals the master Id after save).
		       - FOOTGUN — do NOT, by default, satisfy "Add button" with a header `tools` button wired to
		         `crt.CreateRecordRequest`. That request opens the child entity's navigation/edit page, which the
		         runtime resolves from the entity's REGISTERED page. A standalone detail entity (created with
		         `create-entity-schema` + a `create-page` FormPage but NO section) has no registered navigation/edit
		         page, so clicking the button throws the runtime toast "There is no page for new or existing record.
		         System administrator must check the button settings in the Freedom UI." (console: `_openEntityPage`
		         -> `_showEntityNavigationError`). The page and the grid still load — only the click fails — and
		         `update-page` reports `success: true`, so the break surfaces only in the browser. Reload and click
		         the button to verify; never trust the save result alone.
		       - If a separate header button is explicitly required (inline add is not wanted), make the target page
		         resolvable in one of two ways, AND seed the master FK so the new record stays linked:
		         - pass an explicit `entityPageName` in the request `params` naming an existing FormPage schema
		           (e.g. `"entityPageName": "UsrContactItem_FormPage"`) so the runtime opens that page directly
		           instead of looking one up; or
		         - give the child entity a real section (`create-app-section`) so its edit page is registered for
		           navigation.
		         Either way set `params.defaultValues` to the master FK, e.g.
		         `[{ "attributeName": "<ChildForeignKeyColumn>", "value": "$Id" }]` (the same column used as the dependency
		         `attributePath`), so the created record points back at the open record.

		       Reuse, don't duplicate
		       - Do NOT create a new child schema when an existing child entity + relationship already models the
		         detail. Reuse the existing entity and just wire the data source, collection attribute, and
		         dependency. Confirm the relationship with `get-app-info` / `get-entity-schema-properties` first
		         (see `app-modeling` and `existing-app-maintenance`).

		       When `filterAttributes` IS appropriate (NOT for record scoping)
		       - `filterAttributes` + `loadOnChange` is the channel for an interactive SEARCH/quick-filter on the
		         list (e.g. a `crt.SearchFilter` the user types into) — a UI filter the user changes, not the
		         master-record scope. The designer emits these alongside `dependencies`, never instead of it. Use
		         `dependencies` for "records that belong to the open record"; use `filterAttributes` only for a
		         user-driven filter on top.

		       Common mistakes (these are why a detail shows ALL records or none — or the page will not render)
		       - Inserting the `crt.ExpansionPanel` (or any wrapping container) without `"items": []` in its
		         `values`. The engine rejects it at runtime with `is not a container for other items` and the
		         record card does not render at all — even though `update-page` reported `success: true`.
		       - Using a `crt.HandleViewModelInitRequest` handler to scope the list. Inside that handler, after
		         `await next?.handle(request)`, the page attributes are NOT loaded yet, so `request.$context["Id"]`
		         is undefined; the `if (id)` guard fails and the master filter is never applied — the list stays
		         empty, or shows every child record when the first collection load wins the race.
		         Use `modelConfig.dependencies` instead; the runtime injects the id and waits for the master load.
		       - Seeding a static filter with the empty Guid `00000000-0000-0000-0000-000000000000` and a separate
		         `filterAttributes` entry to scope by the record. That is the hand-rolled handler anti-pattern; the
		         empty Guid simply returns 0 rows and nothing replaces it. Declare a `dependencies` entry instead.
		       - Putting `attributePath` as the MASTER entity's primary key, or `relationPath` as the child FK.
		         It is the reverse: `attributePath` = child FK column, `relationPath` = master id path (`PDS.Id`).
		       - Pointing `relationPath` at something other than the page `primaryDataSourceName` — the runtime
		         cannot resolve the open record's id and the list is not scoped.
		       - Satisfying an "Add button" with a header `tools` button wired to `crt.CreateRecordRequest` for a
		         standalone detail entity that has no registered navigation/edit page. The click throws "There is
		         no page for new or existing record" even though `update-page` returned `success: true`. Prefer
		         inline grid add (see `get-component-info crt.DataGrid` for the editable flags) — the safe
		         default — or pass an explicit `entityPageName` (an existing FormPage) / register a section page.
		         See "Adding records to the detail".
		       - Using a `...Id` path form for the FK column in `attributePath` — see `esq-filters` column-path
		         normalization; use the bare reference column name.
		       """
	};

	/// <summary>
	/// Returns the canonical guidance article for adding and filtering a Freedom UI related/child list (detail).
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "related-list-guidance")]
	[Description("Returns canonical MCP guidance for adding a Freedom UI related/child list and filtering it by the current page record (master-detail \"filter by page data\"): the declarative, dependencies-based scoping — no handler. Fetch the 'Expanded list' composite structure via get-component-info.")]
	public ResourceContents GetGuide() => Guide;
}
