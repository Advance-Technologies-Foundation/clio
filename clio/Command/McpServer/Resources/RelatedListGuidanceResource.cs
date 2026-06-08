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

		       Before you author or edit a detail you MUST call `get-component-info` for `crt.DataGrid` (it ships
		       long-form `documentation`) and for `crt.ExpansionPanel`, and read both in full. Also read
		       `page-modification` (body markers, append vs replace, static-vs-diff body forms, container
		       selection) and `esq-filters` (the filter-leaf contract). Those are the source of truth for the
		       component shapes and the filter grammar; this guide owns the master-detail WIRING that connects
		       them — the part AI most often gets wrong.

		       There is NO single "detail" component
		       - No `crt.Detail` / `crt.ExpandableList` / `crt.ExpandedList` type exists. The designer's
		         "Expanded list" detail is a COMPOSITE you assemble yourself:
		         - `crt.ExpansionPanel` — the collapsible frame: `title`, the header `tools` area (the "+ New"
		           add button), and `items` (the body).
		         - `crt.DataGrid` (or `crt.List`) — the records list, placed inside the panel's `items`.
		       - Dropping a bare `crt.DataGrid` into a tab/container produces a list that does NOT look like a
		         native detail (no collapsible header, no title, no add action). Wrap it in `crt.ExpansionPanel`.
		       - Always confirm live component names (`crt.MultiList`, `crt.ListWidget`, `crt.FilterableList`,
		         etc.) with `get-component-info` against the target environment before authoring.

		       Insert the composite — initialize every container's slot (or the page will not render)
		       - When you INSERT a container node in `viewConfigDiff` (the new `crt.TabContainer`, the wrapping
		         `crt.GridContainer`/`crt.FlexContainer`, and the `crt.ExpansionPanel`), you MUST initialize its
		         content-slot array in `values`: `"items": []` on EVERY container, PLUS `"tools": []` on the
		         `crt.ExpansionPanel` (it declares `contentSlots: ['items', 'tools']`).
		       - Omitting `items` is the #1 detail footgun. The view-engine does NOT treat a slot-less panel as
		         a container, so the page fails at RUNTIME with `Error: Item "<PanelName>" is not a container for
		         other items` and the whole record card stops rendering. `update-page` still returns
		         `success: true` — the break only surfaces in the browser, so always reload and verify the page
		         renders (and the detail's tab appears) after saving.
		       - Build the composite as separate `viewConfigDiff` inserts chained by `parentName`:
		         Tab -> GridContainer -> ExpansionPanel -> GridContainer -> DataGrid; each parent keeps its own
		         `items: []`, and the grid binds `items` to the `$<CollectionAttr>` collection attribute.

		       Filter the list by page data (the master-detail pattern) — the critical part
		       A detail is scoped to the current record by an entity data source + a collection attribute whose
		       data source carries a filter that matches the child foreign-key column to the master record id,
		       plus an init handler that injects the open record's id into that filter at runtime.
		       Four edits make this work:

		       1. Page-scoped `crt.EntityDataSource` for the CHILD entity under `modelConfig.dataSources`
		          (e.g. data source `UsrContactDS` over entity `UsrContact`).
		       2. A COLLECTION attribute (`"isCollection": true`) under `viewModelConfig.attributes` whose
		          `modelConfig.path` points at that data source. The grid `items` binds to this attribute
		          (`$UsrContactGrid`). List the child columns under the collection attribute's own
		          `viewModelConfig.attributes`.
		       3. The filter. DO NOT inline a `filter` object on the collection attribute. The runtime shape is a
		          SEPARATE filter attribute referenced by name from the collection attribute's
		          `modelConfig.filterAttributes`. That filter attribute's `value` is a filter GROUP
		          (`filterType: 6`) containing a single CompareFilter leaf (`filterType: 1`,
		          `comparisonType: 3` = Equal) whose `leftExpression` is the child FOREIGN-KEY column and whose
		          `rightExpression` is a PARAMETER (`expressionType: 2`). SEED that parameter's `value` with the
		          empty Guid `00000000-0000-0000-0000-000000000000` (a VALID Guid placeholder) — do NOT put `$Id`
		          here. The framework does NOT resolve a `$`-parameter inside a static filter value; it serializes
		          the literal string into the ESQ query, the server rejects it with
		          `FormatException: Guid should contain 32 digits with 4 dashes` (HTTP 500), and the record card
		          fails to render. The empty-Guid seed simply returns 0 rows on first load; the real id is injected
		          at runtime in step 4.
		       4. A `crt.HandleViewModelInitRequest` handler that scopes the list to the OPEN record. After
		          `await next?.handle(request)`, read the open-record id from `$context["Id"]` and, when it is set,
		          `$context.set("<FilterAttr>", { ...the same filter group with the real Guid in
		          rightExpression.parameter.value... })`. Because the collection attribute lists this filter under
		          `filterAttributes` with `loadOnChange: true`, setting it reloads the grid scoped to the record.
		          This handler — not a static `$Id` — is the runtime-verified way to filter a hand-authored detail
		          by the page record. (See `page-schema-handlers` for the handler contract.)

		       Runtime-verified payload (a Contact detail on an Account/Client FormPage, filtered to the open
		       record via the child FK column `UsrClient`). Shown as static-form `viewModelConfig`; in a diff-form
		       replacing schema, carry the same two attributes through a `viewModelConfigDiff`
		       `{ "operation": "merge", "path": [], "values": { "attributes": { ... } } }` entry (see
		       `page-modification` for the static-vs-diff body decision). The filter is SEEDED with the empty Guid
		       and the real id is set by the init handler shown right after:

		       ```jsonc
		       // under viewModelConfig.attributes — the collection attribute the grid binds to:
		       "UsrContactGrid": {
		         "isCollection": true,
		         "modelConfig": {
		           "path": "UsrContactDS",
		           "filterAttributes": [ { "name": "UsrContactGridFilter", "loadOnChange": true } ]
		         },
		         "viewModelConfig": {
		           "attributes": {
		             "UsrContactDS_Id":     { "modelConfig": { "path": "UsrContactDS.Id" } },
		             "UsrContactDS_UsrName": { "modelConfig": { "path": "UsrContactDS.UsrName" } },
		             "UsrContactDS_UsrJobTitle": { "modelConfig": { "path": "UsrContactDS.UsrJobTitle" } }
		           }
		         }
		       },
		       // the SEPARATE filter attribute named by filterAttributes[].name above:
		       "UsrContactGridFilter": {
		         "value": {
		           "items": {
		             "masterRecordFilter": {
		               "filterType": 1,
		               "comparisonType": 3,
		               "isEnabled": true,
		               "trimDateTimeParameterToDate": false,
		               "leftExpression": { "expressionType": 0, "columnPath": "UsrClient" },
		               "rightExpression": { "expressionType": 2, "parameter": { "dataValueType": 0, "value": "00000000-0000-0000-0000-000000000000" } }
		             }
		           },
		           "logicalOperation": 0,
		           "isEnabled": true,
		           "filterType": 6
		         }
		       }
		       ```

		       Then inject the real open-record id at runtime with a `crt.HandleViewModelInitRequest` handler (in the
		       schema `handlers` array). This is the runtime-verified master-detail scoping — the static seed above
		       only keeps the first load from 500-ing; this handler is what actually filters by the open record:

		       ```js
		       handlers: /**SCHEMA_HANDLERS*/[
		         {
		           request: "crt.HandleViewModelInitRequest",
		           handler: async (request, next) => {
		             await next?.handle(request);
		             const id = await request.$context["Id"];
		             if (id) {
		               await request.$context.set("UsrContactGridFilter", {
		                 items: {
		                   masterRecordFilter: {
		                     filterType: 1, comparisonType: 3, isEnabled: true,
		                     trimDateTimeParameterToDate: false,
		                     leftExpression: { expressionType: 0, columnPath: "UsrClient" },
		                     rightExpression: { expressionType: 2, parameter: { dataValueType: 0, value: id } }
		                   }
		                 },
		                 logicalOperation: 0, isEnabled: true, filterType: 6
		               });
		             }
		           }
		         }
		       ]/**SCHEMA_HANDLERS*/
		       ```

		       Wiring rules
		       - `leftExpression.columnPath` is the CHILD entity's foreign-key column that points back at the
		         master (here `UsrClient` on `UsrContact`), NOT a path on the master entity and NOT a `...Id`
		         suffix form. Use the bare reference column name.
		       - `rightExpression` is a parameter whose `value` is a REAL Guid. Seed the static filter with the
		         empty Guid `00000000-0000-0000-0000-000000000000` and set the live record id from
		         `$context["Id"]` in the `crt.HandleViewModelInitRequest` handler (steps 3-4). A `$Id` string left
		         in the static `value` is sent to the server verbatim and 500s — it is NOT auto-resolved.
		       - `filterAttributes[].loadOnChange: true` reloads the list when the filter attribute changes — this
		         is what makes the init handler's `$context.set(...)` re-query the grid scoped to the record. Keep
		         it on; without it the list never re-scopes after the handler sets the real id.
		       - The grid `items` binding and the panel/grid `viewConfigDiff` inserts are normal page edits — see
		         `page-modification` for the `crt.ExpansionPanel` + `crt.DataGrid` insert shape and
		         `parentName`/`propertyName`/`index` placement, and `get-component-info` for `columns`,
		         `features`, and toolbar slots.

		       Editable vs read-only
		       - The inner `crt.DataGrid` is read-only by default (`features.editable.enable: false`). For inline
		         add/edit set `features.editable.enable: true` and `features.editable.itemsCreation: true`; new
		         rows inherit the master foreign key from the collection filter.

		       Reuse, don't duplicate
		       - Do NOT create a new child schema when an existing child entity + relationship already models the
		         detail. Reuse the existing entity and just wire the data source, collection attribute, and
		         filter. Confirm the relationship with `get-app-info` / `get-entity-schema-properties` first
		         (see `app-modeling` and `existing-app-maintenance`).

		       Common mistakes (these are why a detail shows ALL records or none — or the page will not render)
		       - Inserting the `crt.ExpansionPanel` (or any wrapping container) without `"items": []` in its
		         `values`. The engine rejects it at runtime with `is not a container for other items` and the
		         record card does not render at all — even though `update-page` reported `success: true`.
		       - Putting an inline `filter` directly on the collection attribute instead of a separate filter
		         attribute referenced through `modelConfig.filterAttributes`. The inline form is silently ignored
		         and the list shows every child record.
		       - Filtering on the master entity's primary key path instead of the child's foreign-key column in
		         `leftExpression.columnPath`.
		       - Putting `$Id` (or any `$`-parameter) as the static `rightExpression.parameter.value`. It is NOT
		         resolved — the server receives the literal string "$Id" and fails with
		         `FormatException: Guid should contain 32 digits with 4 dashes` (HTTP 500), so the page does not
		         open. Seed an empty Guid and set the real id in the `crt.HandleViewModelInitRequest` handler.
		       - Omitting the init handler (or `loadOnChange`), so the list never scopes to the open record — it
		         stays empty (the empty-Guid seed matches nothing) or shows every child record.
		       - Using a `...Id` path form for the FK column — see `esq-filters` column-path normalization.
		       """
	};

	/// <summary>
	/// Returns the canonical guidance article for adding and filtering a Freedom UI related/child list (detail).
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "related-list-guidance")]
	[Description("Returns canonical MCP guidance for adding a Freedom UI related/child list (detail) and filtering it by the current page record: the ExpansionPanel + DataGrid composite, the page-scoped EntityDataSource, the isCollection attribute, and the separate master-detail filter attribute wiring.")]
	public ResourceContents GetGuide() => Guide;
}
