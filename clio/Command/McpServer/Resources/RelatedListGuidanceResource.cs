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
		       data source carries a filter that matches the child foreign-key column to the master record id.
		       Three model edits make this work:

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
		          `rightExpression` is the page-record id bound as the `$Id` parameter.

		       Verified runtime payload (a Contact detail on an Account/Client FormPage, filtered to the open
		       record via the child FK column `UsrClient`). This is static-form `viewModelConfig` as emitted by
		       `create-app` FormPages — in a diff-form replacing schema, carry the same two attributes through a
		       `viewModelConfigDiff` `{ "operation": "merge", "path": [], "values": { "attributes": { ... } } }`
		       entry (see `page-modification` for the static-vs-diff body decision):

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
		               "rightExpression": { "expressionType": 2, "parameter": { "dataValueType": 0, "value": "$Id" } }
		             }
		           },
		           "logicalOperation": 0,
		           "isEnabled": true,
		           "filterType": 6
		         }
		       }
		       ```

		       Wiring rules
		       - `leftExpression.columnPath` is the CHILD entity's foreign-key column that points back at the
		         master (here `UsrClient` on `UsrContact`), NOT a path on the master entity and NOT a `...Id`
		         suffix form. Use the bare reference column name.
		       - `rightExpression` binds the master record through the page `$Id` parameter
		         (`{ "expressionType": 2, "parameter": { "dataValueType": 0, "value": "$Id" } }`). `$Id` is the
		         open record's primary-key attribute on the FormPage.
		       - `filterAttributes[].loadOnChange: true` reloads the list when the bound record id changes (for
		         example right after the master record is first saved); use it for master-detail scoping.
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
		       - Hardcoding a literal id in `rightExpression` instead of binding the `$Id` page parameter.
		       - Omitting `loadOnChange`, so the list never scopes after the record id resolves.
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
