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

		       Adding records to the detail — two mechanisms; page-based add/edit is the primary one
		       This guide owns only the MECHANICS of each add mechanism. WHICH one to use is a UX/product decision —
		       take it from the approved plan (Business Plan) and the `creatio-ui-guidelines` skill (`page-layout-and-controls.md`).
		       Default for a detail: page-based add/edit (a mini add page + a full edit page). Use inline editing only
		       for simple line-item lists (a few short columns) or when the user explicitly asks. When the plan specifies
		       a pattern, implement THAT one — do not downgrade a page-based detail to inline to save effort. The panel /
		       grid / toolbar STRUCTURE comes from `get-component-info composite="Expanded list"`; this guide adds only the
		       wiring below.

		       Mechanism A (default) — page-based add: a header "Add" button opens a page
		       - The "Expanded list" composite's header add button (`crt.CreateRecordRequest`) opens the child entity's
		         REGISTERED add page. That page MUST be resolvable, or the click throws the runtime toast
		         "There is no page for new or existing record. System administrator must check the button settings in the
		         Freedom UI." (console: `_openEntityPage` -> `_showEntityNavigationError`). The page and grid still load and
		         `update-page` reports `success: true` — the failure surfaces only on click, so reload and click to verify;
		         never trust the save result alone.
		       - Make the page resolvable in one of these ways:
		         - Register the entity's pages with `create-related-page-addon` (see `get-guidance name=related-page-binding`):
		           bind a default/edit page (`is-default`) and, if it differs, a separate add page (`is-add`) — e.g. a full
		           `create-page` FormPage for edit + a `BaseMiniPageTemplate` mini page for add. `crt.CreateRecordRequest`
		           then resolves the registered add page automatically. This is the standard section-less-entity path.
		           (Note: `create-related-page-addon` rebuilds static content and bumps the page checksum, so a subsequent
		           `update-page` may report a false "external modification" conflict — re-fetch, or force after confirming
		           the change is your own.)
		         - or pass an explicit `entityPageName` in the request `params` (e.g. `"entityPageName": "UsrContactItem_FormPage"`)
		           to open a specific FormPage without registration; or
		         - give the child entity a real section (`create-app-section`) so its edit page is registered for navigation.
		       - In every case extend the add button's `params` with `defaultValues` so the new record stays linked to the
		         open master: `params.defaultValues: [{ "attributeName": "<ChildForeignKeyColumn>", "value": "$Id" }]` (the
		         same column used as the dependency `attributePath`).
		       - Authoring the pages themselves: a `create-page` page starts with an EMPTY body and is the DIFF body form, so
		         you must add the `PDS` data source via `modelConfigDiff` (it is NOT auto-provisioned as with `create-app`);
		         mini-page fields go in `MainContainer`. See `create-page` / `page-modification` before writing the bodies.

		       Mechanism B — inline grid add: edit rows in the grid (for simple line-item lists)
		       - Enable inline editing/creation on the inner `crt.DataGrid` via its `features` (fetch the exact flags from
		         `get-component-info crt.DataGrid`). The grid renders an add row; the new record inherits the master foreign
		         key from the `dependencies` relationship, so it is scoped to the open record with NO separate page and NO
		         navigation. This works for ANY child entity — including a section-less detail entity. Choosing inline means
		         intentionally omitting the composite's header add button (a valid opt-out):
		         the inline add row IS the add affordance.
		       - REQUIRED wiring for inline add to SAVE: the child FK column (the `attributePath` reference column, here
		         `UsrContact.UsrClient`) MUST be one of the grid's columns / collection attributes. The `dependencies`
		         relationship populates the FK on the new row only when that column is present in the grid; omit it and the
		         inline add row's parent FK stays empty, so the save fails with the runtime error "<FK caption> field must be
		         filled in" (a detail FK is normally required). Add the FK column to the grid — it may be hidden in the UI, but
		         it must be in the collection so inline create inherits the open master (confirm via OData that the child's
		         `...Id` equals the master Id after save).

		       Reuse, don't duplicate
		       - Do NOT create a new child schema when an existing child entity + relationship already models the
		         detail. Reuse the existing entity and just wire the data source, collection attribute, and
		         dependency. Confirm the relationship with `get-app-info` / `get-entity-schema-properties` first
		         (see `app-modeling` and `existing-app-maintenance`).

		       When `filterAttributes` IS appropriate (NOT for record scoping)
		       - `filterAttributes` + `loadOnChange` is the channel for BOTH (a) an interactive SEARCH/quick-filter
		         the user changes (e.g. a `crt.SearchFilter` the user types into) AND (b) a STATIC business filter
		         (see next section) — a fixed condition applied to the list. It is NEVER the record-scope mechanism:
		         the designer emits `filterAttributes` ALONGSIDE `dependencies`, never instead of it. Use
		         `dependencies` for "records that belong to the open record"; use `filterAttributes` for a static
		         business filter or a user-driven filter ON TOP of that scope.

		       Adding a STATIC business filter (Emails vs Activities, status/type-restricted lists)
		       Some lists need a fixed condition — e.g. an "Emails" detail = `Activity` where `Type = Email`, an
		       "Activities" detail = `Activity` where `Type != Email`, a "Current vacancies" list =
		       `InternalRequest` where `Status IN (...)`, or a plain grid of `Contact` where `Account = <fixed>`.
		       The static condition is a PREDEFINED FILTER carried by the COLLECTION ATTRIBUTE (step 2 of the recipe
		       above) — NOT by the data source. It works BOTH standalone (a list with a fixed condition and no
		       master scope) AND on top of a `dependencies` record scope; when a `dependencies` entry is also
		       present the two filters apply together (AND-combined) at runtime. This is EXACTLY the shape the
		       Freedom Designer emits when you add a filter in the list's filter panel (verified against Designer
		       output). Two coupled edits on the SAME collection attribute:

		       1. On the collection attribute's `modelConfig`, add a `filterAttributes` entry naming the filter
		          attribute: `"filterAttributes": [ { "name": "<CollectionAttr>_PredefinedFilter", "loadOnChange": true } ]`.
		       2. Add a SIBLING view-model attribute of that EXACT name holding the serialized ESQ filter group as
		          its `value`: `"<CollectionAttr>_PredefinedFilter": { "value": <filter group> }`.

		       The runtime reads that attribute's value and adds it to the grid's ESQ query as a filter parameter
		       (verified: `_setupFiltersAttributes` sets `viewModel[<name>]` from the attribute value, and the
		       data-source load reads every `filterAttributes[].name` value and adds it as a query `Filter`). Naming
		       rule: use `<CollectionAttr>_PredefinedFilter` (the step-2 collection attribute name + the
		       `_PredefinedFilter` suffix) so the Freedom Designer ROUND-TRIPS it — shows it in the detail filter
		       panel and re-emits it on save. An arbitrary name still filters at runtime but the Designer will not
		       recognise it. The filter group is the standard serialized ESQ group — build it with
		       `esq-filters-frontend`; its `rootSchemaName` MUST equal the child data source `entitySchemaName`, and
		       keep the full group envelope (`filterType: 6`, `logicalOperation`, `isEnabled`, `rootSchemaName`).
		       Validate the group with `execute-esq` over the child entity before saving.

		       Diff-form: one `viewModelConfigDiff` merge into `["attributes"]` carrying BOTH the collection
		       attribute (with `filterAttributes` inside its `modelConfig`) AND the sibling `_PredefinedFilter`
		       attribute. The exact `_PredefinedFilter` value shape and a worked example live in the `crt.DataGrid`
		       component doc — fetch it via `get-component-info crt.DataGrid` (§ "predefined / static filters"); do
		       NOT re-derive it here. Minimal wiring skeleton — an Emails detail (`Activity` where `Type = Email`) on
		       top of the `Account`→`PDS.Id` scope:

		       ```jsonc
		       "EmailGrid": {
		         "isCollection": true,
		         "modelConfig": {
		           "path": "EmailDS",
		           "filterAttributes": [ { "name": "EmailGrid_PredefinedFilter", "loadOnChange": true } ]
		         }
		         // ...viewModelConfig.attributes as in the recipe above
		       },
		       "EmailGrid_PredefinedFilter": { "value": { /* ESQ filter group, rootSchemaName == "Activity";
		         build the Type=Email lookup leaf with esq-filters-frontend */ } }
		       // modelConfig.dependencies still scopes to the open master (unchanged):
		       //   { "EmailDS": [ { "attributePath": "Account", "relationPath": "PDS.Id" } ] }
		       ```

		       Resolve the `Email` `ActivityType` Id before saving (the lookup leaf takes a value object, not a bare
		       GUID — see `esq-filters-frontend`) and validate the group with `execute-esq`. For the sibling
		       "Activities" detail add the mirror `Type != Email` group to ITS OWN `_PredefinedFilter` attribute so
		       the two details do not overlap.

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
		       - Wiring a header "Add" button (`crt.CreateRecordRequest`) for a child entity whose add/edit page is not
		         resolvable (no registered page, no `entityPageName`, no section). The click throws
		         "There is no page for new or existing record" even though `update-page` returned `success: true`. Fix by
		         registering the page (`related-page-binding`), passing `entityPageName`, or adding a section — see
		         "Adding records to the detail, Mechanism A". Use inline add (Mechanism B) only when the plan calls for a
		         simple line-item list, and then ensure the FK column is present in the grid collection.
		       - Using a `...Id` path form for the FK column in `attributePath` — see `esq-filters-frontend` column-path
		         normalization; use the bare reference column name.
		       - Putting a static filter in the `crt.EntityDataSource` `config.filters` (on the viewElement data
		         source under `modelConfig.dataSources`). That key is NEVER applied: `filters` is not a
		         recognized `crt.EntityDataSource` config option, and grid filtering comes
		         EXCLUSIVELY from `modelConfig.dependencies` (record scope) and the collection attribute's
		         `filterAttributes` (predefined/search filter). A `config.filters` block persists in the saved body
		         and `update-page` returns `success: true`, but it is silently ignored — the detail shows UNFILTERED
		         data and the Designer's filter panel shows nothing. Put the static condition in a
		         `<CollectionAttr>_PredefinedFilter` attribute referenced from `filterAttributes` instead (see
		         "Adding a STATIC business filter on top of the record scope" above).
		       """
	};

	/// <summary>
	/// Returns the canonical guidance article for adding and filtering a Freedom UI related/child list (detail).
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "related-list-guidance")]
	[Description("Returns canonical MCP guidance for adding a Freedom UI related/child list (detail) and filtering it: master-detail scoping by the current page record (declarative dependencies — no handler) AND a STATIC business filter on the list (the <CollectionAttr>_PredefinedFilter attribute via filterAttributes — never config.filters on the datasource). Fetch the 'Expanded list' composite structure via get-component-info.")]
	public ResourceContents GetGuide() => Guide;
}
