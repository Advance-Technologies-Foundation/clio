# How to Add an Expanded List (`crt.ExpansionPanel` preset) to a Freedom UI Page

> Audience: code agent assembling the **"Expanded list"** designer element into a Creatio Freedom UI page schema.
>
> "Expanded list" (caption key `Components.ExpandedList.Caption`) is **not a standalone component** — it is a
> preset of `crt.ExpansionPanel`: a collapsible panel pre-filled with a `crt.DataGrid` in its body and a fixed
> header toolbar (add / refresh / settings menu / search). Dropping it in the designer runs the
> `crt.CreateExpandedDataGridItemCommand`, which inserts the panel + grid and then runs DataGrid initialization.
> Reproducing it by hand means replicating BOTH the nested structure AND that initialization.
>
> Cross-references (do not re-document them here):
> - Base panel mechanics (slots, naming, layout): [`expansion-panel.component.md`](./expansion-panel.component.md).
> - Full `crt.DataGrid` property reference + data-source/attribute wiring: its own guide
>   (`componentType: "crt.DataGrid"` in `ComponentRegistry.json`, `data-grid.component.md`).

## Required components

A complete Expanded list **always** consists of all of the following. Emit every one (full structure in §3.1);
skip a toolbar action only on explicit user opt-out (§3.4).

| # | Component | Role | Slot / parent |
|---|-----------|------|---------------|
| 1 | `crt.ExpansionPanel`      | host panel                         | page `MainContainer.items` |
| 2 | `crt.GridContainer`       | body wrapper                       | panel `items` |
| 3 | `crt.DataGrid`            | the list                           | body `GridContainer.items` |
| 4 | `crt.GridContainer`       | toolbar wrapper                    | panel `tools` |
| 5 | `crt.FlexContainer` (row) | action row                         | toolbar `GridContainer.items` |
| 6 | `crt.Button`              | **add**                            | action row |
| 7 | `crt.Button`              | **reload**                         | action row |
| 8 | `crt.Button` (`clickMode:"menu"`) | **import/export** menu      | action row |
| 9 | `crt.MenuItem`            | export                             | settings button `menuItems` |
| 10 | `crt.MenuItem`           | import                             | settings button `menuItems` |
| 11 | `crt.SearchFilter`       | **search**                         | action row |

Plus the data wiring (§3.2): a `crt.EntityDataSource` (`modelConfigDiff`) and the `isCollection` attribute with
its search `filterAttributes` (`viewModelConfigDiff`). Without these the grid shows nothing and search does nothing.

## Metadata

- **Designer element**: `Components.ExpandedList.Caption` — toolbar group `Components`, position `30`, id `ExpandedList`.
- **Create command**: `crt.CreateExpandedDataGridItemCommand` (extends `crt.CreateDataGridItemCommand`).
- **Wraps**: `crt.ExpansionPanel` → body (`items`) holds a `crt.DataGrid`; header (`tools`) holds the action buttons.
- **Requires**: no extra feature.

---

## 1. What the designer drop produces

The toolbar config wraps every preset in the same two-level shell (see `getExpansionPanelToolbarConfig`). The
nested element is **never** a direct child of the panel — it sits inside a `crt.GridContainer`:

```
crt.ExpansionPanel (title, expanded:true, togglePosition:"before", titleWidth:20, fullWidthHeader:true)
├─ items  → crt.GridContainer (2 columns)
│            └─ crt.DataGrid            name: "GridDetail_<guid>"   layoutConfig.rowSpan: 6
└─ tools  → crt.GridContainer (1 column)
             └─ crt.FlexContainer (row, alignItems:center)
                ├─ crt.Button   "add"      icon:add-button-icon     clicked: crt.CreateRecordRequest
                ├─ crt.Button   "refresh"  icon:reload-icon         clicked: crt.LoadDataRequest { config:{ loadType:"reload" } }
                ├─ crt.Button   "settings" icon:actions-button-icon clickMode:"menu"
                │   ├─ crt.MenuItem "export" clicked: crt.ExportDataGridToExcelRequest
                │   └─ crt.MenuItem "import" clicked: crt.ImportDataRequest
                └─ crt.SearchFilter  iconOnly:true
```

The DataGrid view-element name is prefixed `GridDetail` (the `EXPANDED_LIST_DATA_GRID_PREFIX` const).

> **All four toolbar actions — add, refresh, import/export menu, search — are part of the standard "Expanded list".**
> A correct reproduction emits every one of them (see §3.3); omit an action only on explicit user opt-out (§3.4).
> Hand-authoring that produces only the grid, or the grid with a partial toolbar, does **not** match the designer output.

## 2. Post-insert wiring — the part hand-authoring gets wrong

After the static insert, `crt.CreateExpandedDataGridItemCommand` (via `CreateDataGridItemCommand.innerExecute`) runs:

1. **`_setInitialFeatures`** → `features.rows.selection = { enable: true, multiple: true }` on the DataGrid (skipped only when the `DisableDataGridMultipleRowsSelection` feature is on).
2. **`_setInitialFilterOptions`** → since a freshly dropped grid has no `items`/`activeRow` yet, it binds them to attributes named after the view element: `items: "$GridDetail_records"` and `activeRow: "$GridDetail_records_ActiveRow"` (the view element name, no `_items`/`_activeRow` suffix), and adds a `_filterOptions` block. This runs on the drop and does **not** require a data source.
3. **`_setInitialSelectionOptions`** → registers the selection attribute.
4. **`_setDefaultDataSource`** → **DOES NOT run on a toolbar drop.** It fires only if the command receives `defaultDataSourceConfig.entitySchemaName`, and the toolbar preset passes none. **So the DataGrid lands with its `items`/`activeRow` bindings set (step 2) but NO data source feeding them and NO columns.**

> **The single most important consequence:** an "Expanded list" dropped from the toolbar is an *empty* grid skeleton inside a panel. To make it show data you must add the DataGrid's own three diffs yourself — exactly as in the standalone `crt.DataGrid` guide.

## 3. Step-by-step recipe

### 3.1 Insert the full structure — panel + grid + toolbar (one `viewConfigDiff`)

> ⚠️ **This is ONE `viewConfigDiff` array — emit every insert below together.** The header toolbar (the `tools`
> slot) is part of the component, not an optional add-on. A panel with the grid but no toolbar, or with only some
> actions, is **incomplete** and does not render like the standard Expanded list. The only exception is an explicit
> user opt-out (§3.4). After emitting, confirm all 11 inserts are present (3 grid + 8 toolbar).
>
> ⚠️ **Every container must declare its child collection(s) as empty arrays in `values`** — the panel needs
> `"items": []` **and** `"tools": []`, each `crt.GridContainer` / `crt.FlexContainer` needs `"items": []`, and the
> settings button needs `"menuItems": []`. These are load-bearing, not boilerplate: a child insert into a parent
> that does not pre-declare that array throws `Item "<parent>" is not a container for other items`. **Do not strip
> empty arrays.**

```jsonc
[
  // ── host panel ──
  // fullWidthHeader:true + a NUMERIC titleWidth split the header into title% / tools% (tools lane = 100 - titleWidth %).
  // With fullWidthHeader:false the tools lane gets no width and the toolbar buttons stack vertically instead of a row.
  { "operation": "insert", "name": "ExpandedListPanel", "parentName": "MainContainer", "propertyName": "items", "index": 0,
    "values": { "type": "crt.ExpansionPanel", "title": "#ResourceString(ExpandedListPanel_title)#", "expanded": true,
      "togglePosition": "before", "titleWidth": 20, "fullWidthHeader": true, "fitContent": true, "items": [], "tools": [],
      "layoutConfig": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 8 } } },

  // ── body: GridContainer → DataGrid ──
  { "operation": "insert", "name": "ExpandedListPanel_grid_wrap", "parentName": "ExpandedListPanel", "propertyName": "items", "index": 0,
    "values": { "type": "crt.GridContainer", "columns": ["minmax(32px, 1fr)", "minmax(32px, 1fr)"], "rows": "minmax(max-content, 32px)",
      "gap": { "columnGap": "large", "rowGap": 0 }, "styles": { "overflow-x": "hidden" }, "items": [] } },
  { "operation": "insert", "name": "GridDetail_records", "parentName": "ExpandedListPanel_grid_wrap", "propertyName": "items", "index": 0,
    "values": { "type": "crt.DataGrid", "items": "$GridDetail_records", "activeRow": "$GridDetail_records_ActiveRow",
      "primaryColumnName": "GridDetail_recordsDS_Id", "columns": [ /* see crt.DataGrid guide §2.3 */ ],
      "features": { "rows": { "selection": { "enable": true, "multiple": true } } },
      "layoutConfig": { "column": 1, "row": 1, "colSpan": 2, "rowSpan": 6 } } },

  // ── toolbar: GridContainer → FlexContainer(row) → 4 actions (ALL REQUIRED) ──
  // The `tools` slot must contain EXACTLY ONE child — this GridContainer. The header renders each direct `tools`
  // item in its own block <div>, so inserting the buttons straight into `tools` stacks them vertically. All four
  // actions go inside the single FlexContainer(row) below, never directly into `tools`.
  { "operation": "insert", "name": "ExpandedListToolsContainer", "parentName": "ExpandedListPanel", "propertyName": "tools", "index": 0,
    "values": { "type": "crt.GridContainer", "rows": "minmax(max-content, 24px)", "columns": ["minmax(32px, 1fr)"],
      "gap": { "columnGap": "large", "rowGap": "none" }, "color": "transparent", "items": [] } },
  { "operation": "insert", "name": "ExpandedListToolsRow", "parentName": "ExpandedListToolsContainer", "propertyName": "items", "index": 0,
    "values": { "type": "crt.FlexContainer", "direction": "row", "alignItems": "center", "gap": "none", "items": [],
      "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 } } },

  // 1. add → creates a record in the grid entity
  { "operation": "insert", "name": "ExpandedListAddButton", "parentName": "ExpandedListToolsRow", "propertyName": "items", "index": 0,
    "values": { "type": "crt.Button", "icon": "add-button-icon", "iconPosition": "only-icon", "color": "default",
      "clicked": { "request": "crt.CreateRecordRequest", "params": { "entityName": "<grid entity schema name>" } } } },

  // 2. refresh → reloads the grid data source
  { "operation": "insert", "name": "ExpandedListRefreshButton", "parentName": "ExpandedListToolsRow", "propertyName": "items", "index": 1,
    "values": { "type": "crt.Button", "icon": "reload-icon", "iconPosition": "only-icon", "color": "default",
      "clicked": { "request": "crt.LoadDataRequest", "params": { "config": { "loadType": "reload" }, "dataSourceName": "GridDetail_recordsDS" } } } },

  // 3. settings → menu button holding export + import
  { "operation": "insert", "name": "ExpandedListSettingsButton", "parentName": "ExpandedListToolsRow", "propertyName": "items", "index": 2,
    "values": { "type": "crt.Button", "icon": "actions-button-icon", "iconPosition": "only-icon", "color": "default", "clickMode": "menu", "menuItems": [] } },
  { "operation": "insert", "name": "ExpandedListExportMenuItem", "parentName": "ExpandedListSettingsButton", "propertyName": "menuItems", "index": 0,
    "values": { "type": "crt.MenuItem", "icon": "export-button-icon", "caption": "#ResourceString(ExpandedListExportMenuItem_caption)#",
      "clicked": { "request": "crt.ExportDataGridToExcelRequest", "params": { "viewName": "GridDetail_records" } } } },
  { "operation": "insert", "name": "ExpandedListImportMenuItem", "parentName": "ExpandedListSettingsButton", "propertyName": "menuItems", "index": 1,
    "values": { "type": "crt.MenuItem", "icon": "import-button-icon", "caption": "#ResourceString(ExpandedListImportMenuItem_caption)#",
      "clicked": { "request": "crt.ImportDataRequest", "params": { "entitySchemaName": "<grid entity schema name>" } } } },

  // 4. search → SearchFilter bound to the grid via the exposed attribute (see §3.2 filterAttributes)
  //   Naming pattern: exposed attribute = <SearchFilterName>_<DataGridName>;
  //   from = <SearchFilterName>_SearchValue and <SearchFilterName>_FilteredColumnsGroups.
  //   Rename ALL of these together (here + §3.2 filterAttributes) if the SearchFilter or grid is renamed.
  { "operation": "insert", "name": "ExpandedListSearchFilter", "parentName": "ExpandedListToolsRow", "propertyName": "items", "index": 3,
    "values": { "type": "crt.SearchFilter", "iconOnly": true, "placeholder": "#ResourceString(ExpandedListSearchFilter_placeholder)#",
      "_filterOptions": {
        "expose": [ { "attribute": "ExpandedListSearchFilter_GridDetail_records",
          "converters": [ { "converter": "crt.SearchFilterAttributeConverter", "args": ["GridDetail_records"] } ] } ],
        "from": [ "ExpandedListSearchFilter_SearchValue", "ExpandedListSearchFilter_FilteredColumnsGroups" ] } } }
]
```

### 3.2 Add the grid data source + collection attribute (`modelConfigDiff` + `viewModelConfigDiff`)

These are the standard DataGrid diffs — **not optional** for a working list; the toolbar drop omits them, so you must add them. The inline JSON below is the authoritative form for the Expanded list (see `data-grid.component.md` for the full `crt.DataGrid` property reference). Two points to keep in mind:

- The grid's data source uses **`scope: "viewElement"`** — the recommended default for embedded grids (`data-grid.component.md` §2.1); the Expanded list carries no exception.
- The collection attribute carries a **`filterAttributes`** entry naming the search filter's exposed attribute (`<SearchFilterName>_<DataGridName>`, `loadOnChange: true`). This is the Expanded-list-specific wiring that connects the `crt.SearchFilter` (§3.3) to the grid; without it search does nothing.

```jsonc
// modelConfigDiff
[ { "operation": "merge", "path": [], "values": { "dataSources": {
  "GridDetail_recordsDS": {
    "type": "crt.EntityDataSource",
    "scope": "viewElement",
    "config": { "entitySchemaName": "<grid entity schema name>", "attributes": { "Name": { "path": "Name" } } } } } } } ]

// viewModelConfigDiff
[ { "operation": "merge", "path": [], "values": { "attributes": {
  "GridDetail_records": {
    "isCollection": true,
    "modelConfig": {
      "path": "GridDetail_recordsDS",
      "filterAttributes": [
        { "name": "ExpandedListSearchFilter_GridDetail_records", "loadOnChange": true } ] },
    "viewModelConfig": { "attributes": {
      "GridDetail_recordsDS_Name": { "modelConfig": { "path": "GridDetail_recordsDS.Name" } },
      "GridDetail_recordsDS_Id":   { "modelConfig": { "path": "GridDetail_recordsDS.Id" } } } } } } } } ]
```

Add one `viewModelConfig` attribute + one data-source `attributes` entry per grid column.

### 3.3 Toolbar wiring rules

The toolbar inserts are in the §3.1 array (nested `tools → crt.GridContainer → crt.FlexContainer(row) → the four actions`; never place buttons directly in `tools`). Each action's request must be wired to the grid, or it silently no-ops:

- **add** → `crt.CreateRecordRequest`, `params.entityName` = grid entity schema (NOT `dataSourceName`).
- **refresh** → `crt.LoadDataRequest`, `params.dataSourceName` = the grid's `<name>DS` key.
- **export** → `crt.ExportDataGridToExcelRequest`, `params.viewName` = the **DataGrid view-element name** (`GridDetail_records`), not the `<name>DS` key.
- **import** → `crt.ImportDataRequest`, `params.entitySchemaName` = grid entity schema.
- **search** → exposed attribute is `<SearchFilterName>_<DataGridName>`; the converter arg and `from` prefixes reuse those exact names, and the same attribute name must appear in the collection attribute's `filterAttributes` (§3.2).

### 3.4 Opt-out

The four toolbar actions (add, refresh, import/export menu, search) are part of the standard "Expanded list" and must be emitted by default. Skip an individual action **only when the user explicitly says it is not needed** (e.g. "no import/export", "no search"); dropping its `insert`(s) is enough — the grid and the remaining toolbar still work.

## 4. Common pitfalls

1. **No data source after the drop.** The preset gives an empty grid; `_setDefaultDataSource` never runs from the toolbar. Add the `crt.EntityDataSource` + collection attribute + `columns` yourself (§3.2).
2. **Use the command's exact attribute names.** The bindings are `items: "$<DataGridName>"` and `activeRow: "$<DataGridName>_ActiveRow"` — the view element name, with no `_items`/`_activeRow` suffix. The command sets them on drop; the bound attribute just stays empty until you add the data source (§3.2).
3. **Toolbar actions wired to the wrong target.** The refresh button's `params.dataSourceName` must reuse the DataGrid's `<name>DS` key; the add button's `crt.CreateRecordRequest` instead takes `params.entityName` (the grid's entity schema), not `dataSourceName`. A mismatch makes the action silently no-op.
4. **Nested element is one level deeper than the panel.** Children go into the inner `crt.GridContainer`, not directly into `crt.ExpansionPanel.items`.
5. **Incomplete toolbar.** Emitting only the grid (or grid + add/refresh) does not match the designer — the standard preset always includes the **search filter** and the **import/export menu** too. Include all four actions unless the user opts out (§3.4).
6. **Toolbar buttons placed directly in the `tools` slot.** The header wraps **each** direct `tools` item in its own block `<div>`, so multiple buttons inserted straight into `tools` stack **vertically**. The `tools` slot must hold **exactly one** child — a `crt.GridContainer` whose `crt.FlexContainer(row)` holds all four actions (§3.1). Same applies to the body: wrap the `crt.DataGrid` in a `crt.GridContainer`, don't put it straight into the panel `items`.
7. **`export` `viewName` set to the data-source key.** `crt.ExportDataGridToExcelRequest.params.viewName` is the **DataGrid view-element name** (`GridDetail_records`), not `<name>DS`. `crt.ImportDataRequest` uses `params.entitySchemaName`.
8. **Empty child arrays stripped from a container.** Omitting `"items": []` / `"tools": []` on the panel (or `"items": []` on a `GridContainer`/`FlexContainer`, or `"menuItems": []` on the settings button) throws `Item "<parent>" is not a container for other items` at runtime — the parent must pre-declare the collection it receives children into.
9. **Toolbar buttons render vertically.** The `tools` lane only gets a width when the panel has `fullWidthHeader: true` **and** a numeric `titleWidth` (the header splits into `titleWidth%` / `100 - titleWidth%`). With `fullWidthHeader: false` the lane collapses to the grid's `minmax(32px, …)` and the action row wraps to one button per line. Set `fullWidthHeader: true` + a numeric `titleWidth` (e.g. `20`).

## 5. Quick checklist

- [ ] `crt.ExpansionPanel` inserted with a localized `title`, enough `rowSpan`, and **both** `"items": []` and `"tools": []` declared.
- [ ] Panel has `fullWidthHeader: true` **and** a numeric `titleWidth` (otherwise the toolbar buttons stack vertically).
- [ ] Every container declares its child array (`items: []` on GridContainer/FlexContainer, `menuItems: []` on the settings button) — never stripped.
- [ ] DataGrid inserted inside the panel's inner `crt.GridContainer` (not directly in `items`).
- [ ] `modelConfigDiff` data source + `viewModelConfigDiff` collection attribute added (§3.2) — the drop omits them.
- [ ] `features.rows.selection` set (the command would set it; preserve it when hand-authoring).
- [ ] `items` / `activeRow` bound as `$<DataGridName>` / `$<DataGridName>_ActiveRow` (the view element name, no `_items` suffix) — the drop sets both.
- [ ] Refresh button `params.dataSourceName` = grid's `<name>DS`; add button `crt.CreateRecordRequest` uses `params.entityName`.
- [ ] Toolbar nested as `tools → GridContainer → FlexContainer(row)`, not buttons directly in `tools`.
- [ ] **add** button present (`crt.CreateRecordRequest`).
- [ ] **refresh** button present (`crt.LoadDataRequest`, reload).
- [ ] **import/export menu** present: `clickMode:"menu"` button with two `crt.MenuItem`s (`crt.ExportDataGridToExcelRequest` with `viewName` = grid name; `crt.ImportDataRequest` with `entitySchemaName`).
- [ ] **search filter** present (`crt.SearchFilter`, `iconOnly:true`, exposed attribute `<SearchFilterName>_<DataGridName>`).
- [ ] An action is omitted only because the user explicitly opted out (§3.4).
