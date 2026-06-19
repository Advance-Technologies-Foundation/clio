# How to Add an Approval List (`crt.ApprovalList`) to a Freedom UI Page

> Audience: code agent inserting `crt.ApprovalList` into a Creatio Freedom UI page schema.
> Renders a data grid listing approval records for the current entity, with row selection, sorting,
> and WebSocket-based auto-refresh; extends `crt.DataGrid` with approval-specific reload behavior.

## Metadata
- **Category**: display
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: none

---

## 1. Mental model — the 3 places you must edit
| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.ApprovalList"` and column definitions. **Always present.** |
| 2 | `modelConfigDiff` | A datasource entry for the approval list (`crt.EntityDataSource`). |
| 3 | `viewModelConfigDiff` | Attributes binding `items` to the datasource collection. |

`crt.ApprovalList` inherits all `crt.DataGrid` inputs/outputs. The leaf adds only `recordId` (input)
and `reloadData` (output). WebSocket messages for the bound `recordId` automatically trigger `reloadData`.

### 1.1 Naming convention
```
ApprovalList_<id>          // view element name
ApprovalList_<id>DS        // datasource name in modelConfigDiff
$ApprovalList_<id>         // items attribute in viewModelConfigDiff
ApprovalList_<id>DS_<col>  // column code (datasource name + "_" + column name)
```

---

## 2. Step-by-step recipe

### 2.1 Declare datasource in `modelConfigDiff`

```jsonc
{
  "operation": "insert",
  "name": "ApprovalList_abc123DS",
  "values": {
    "type": "crt.EntityDataSource",
    "config": {
      "entitySchemaName": "OrderVisa",
      "attributes": {
        "VisaOwner": { "path": "VisaOwner", "type": 10 },
        "Objective": { "path": "Objective", "type": 30 },
        "SetDate": { "path": "SetDate", "type": 7 },
        "Status": { "path": "Status", "type": 10 }
      }
    },
    "masterRecordColumnName": "RecordId",
    "filterAttributes": [
      {
        "name": "ApprovalListSearch_ApprovalList_abc123",
        "loadOnChange": true
      }
    ]
  },
  "parentName": "PDS",
  "propertyName": "datasources",
  "index": 0
}
```

Alternatively register the datasource with two `merge` ops: declare it under
`dataSources` with `"scope": "viewElement"`, and filter it to the current record with a `dependencies` entry —
the approval entity's foreign-key column to the parent, related to `PDS.Id`:

```jsonc
// merge at path [] — links the datasource to the parent record
{ "operation": "merge", "path": [], "values": {
  "dependencies": { "ApprovalList_abc123DS": [{ "attributePath": "Order", "relationPath": "PDS.Id" }] }
}}
// merge at path ["dataSources"] — the datasource itself
{ "operation": "merge", "path": ["dataSources"], "values": {
  "ApprovalList_abc123DS": {
    "type": "crt.EntityDataSource",
    "scope": "viewElement",
    "config": { "entitySchemaName": "OrderVisa", "attributes": {
      "VisaOwner": { "path": "VisaOwner" }, "Objective": { "path": "Objective" },
      "CreatedOn": { "path": "CreatedOn" }, "SetDate": { "path": "SetDate" },
      "SetBy": { "path": "SetBy" }, "Status": { "path": "Status" }
    }}
  }
}}
```

> **Which approval entity?** An object with a dedicated visa entity (e.g. `Order` → `OrderVisa`) binds to
> that entity, and the dependency FK is its column to the master (`attributePath: "Order"`). A custom or
> arbitrary object with **no** dedicated visa entity uses the universal **`SysApproval`** entity, whose FK to
> the master record is **`EntityId`** — so `approvalEntityName: "SysApproval"`, the datasource is over
> `SysApproval`, and the dependency is `{ "attributePath": "EntityId", "relationPath": "<master>DS.Id" }`
> (shipped example: `UsrApprovalTestApp_FormPage` — `SysApproval` + `EntityId → PDS.Id`). The `entityName`
> input is the master record's own entity in both cases.

### 2.2 Declare attribute in `viewModelConfigDiff`

The top-level `modelConfig.path` binds the collection to the datasource — **without it the grid stays empty**.
`sortingConfig` sets the default row order; `filterAttributes` links the search filter (§2.4) to this collection.
Re-declare every column shown in `viewConfigDiff.columns` (plus `Id`) as a `<DS>_<column>` sub-attribute.

```jsonc
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "ApprovalList_abc123": {
      "isCollection": true,
      "modelConfig": {
        "path": "ApprovalList_abc123DS",
        "sortingConfig": { "default": [{ "columnName": "CreatedOn", "direction": "desc" }] },
        "filterAttributes": [{ "name": "ApprovalListSearch_ApprovalList_abc123", "loadOnChange": true }]
      },
      "viewModelConfig": {
        "attributes": {
          "ApprovalList_abc123DS_Id": { "modelConfig": { "path": "ApprovalList_abc123DS.Id" } },
          "ApprovalList_abc123DS_VisaOwner": { "modelConfig": { "path": "ApprovalList_abc123DS.VisaOwner" } }
        }
      }
    }
  }
}
```

### 2.3 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "ApprovalList",
  "values": {
    "type": "crt.ApprovalList",
    "masterRecordColumnValue": "$Id",
    "recordColumnName": "RecordId",
    "layoutConfig": { "colSpan": 2, "column": 1, "row": 1, "rowSpan": 10 },
    "features": { "editable": false },
    "entityName": "Order",
    "approvalEntityName": "OrderVisa",
    "items": "$ApprovalList_abc123",
    "primaryColumnName": "ApprovalList_abc123DS_Id",
    "columns": [
      {
        "id": "c006c5d0-ad3d-8605-cd50-fdcd22fa09e1",
        "code": "ApprovalList_abc123DS_VisaOwner",
        "caption": "#ResourceString(ApprovalList_abc123DS_VisaOwner)#",
        "dataValueType": 10,
        "width": 175
      }
    ],
    "visible": true
  },
  "parentName": "ApprovalListGridContainer",
  "propertyName": "items",
  "index": 0
}
```

### 2.4 Companion elements (container, reload, search)

The grid does not stand alone — `parentName` above points at a `crt.GridContainer` you must also insert
(e.g. `ApprovalListGridContainer`). Shipped pages wrap it together with two header controls:

- **Refresh button** — a `crt.Button` whose `clicked` fires `crt.LoadDataRequest` with the datasource name. This is
  the explicit reload path (the `reloadData` output covers automatic WebSocket-driven refresh; the button covers
  manual refresh):
  ```jsonc
  "clicked": { "request": "crt.LoadDataRequest", "params": {
    "config": { "loadType": "reload", "useLastLoadParameters": true },
    "dataSourceName": "ApprovalList_abc123DS"
  }}
  ```
- **Search filter** — a `crt.SearchFilter` that exposes the attribute named in the datasource/collection
  `filterAttributes` (`ApprovalListSearch_ApprovalList_abc123`) via the `crt.SearchFilterAttributeConverter`.

Drop these if the page needs neither manual reload nor search; the grid itself still works once §2.1–2.3 are wired.

---

## 2.4 Optional: hierarchical (tree) view

Show approvals as a tree (each approval nested under its **parent approval**) instead of a flat list.
Schema-driven, **off by default**. Requires a **self-referencing lookup** on the approval entity
(e.g. `UsrApprovalParent` on `SysApproval`).

1. `modelConfigDiff` — add `hierarchyConfig` **inside the datasource `config`** (a sibling of
   `config` silently disables the tree), and scope rows to the page record by `EntityId`:
   ```jsonc
   "config": {
     "entitySchemaName": "SysApproval",
     "attributes": { "Id": { "path": "Id" }, "EntityId": { "path": "EntityId" } },
     "loadParameters": {
       "options": {
         "hierarchyConfig": { "type": "ClientSide", "hierarchicalColumnName": "UsrApprovalParent" }
       }
     }
   },
   "masterRecordColumnName": "EntityId"
   ```
2. `viewConfigDiff` — enable the feature: `"features": { "hierarchical": { "enable": true } }`.

The `SetupDataGridHierarchicalHandler` derives the rest (`hierarchicalAttributeName`,
has-children/children-count, toggle); expand/collapse state is kept client-side. Approve/reject,
sorting, and pagination keep working.

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ApprovalList` are in `ComponentRegistry.json` under `componentType: "crt.ApprovalList"`. This
guide covers only the assembly mechanics; full column/feature options are inherited from `crt.DataGrid`.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — matches real PackageStore usage (Orders_FormPage)
{
  "operation": "insert",
  "name": "ApprovalList",
  "values": {
    "type": "crt.ApprovalList",
    "masterRecordColumnValue": "$Id",
    "recordColumnName": "RecordId",
    "layoutConfig": { "colSpan": 2, "column": 1, "row": 1, "rowSpan": 10 },
    "features": { "editable": false },
    "entityName": "Order",
    "approvalEntityName": "OrderVisa",
    "items": "$ApprovalList_eo00aks",
    "primaryColumnName": "ApprovalList_eo00aksDS_Id",
    "columns": [
      {
        "id": "c006c5d0-ad3d-8605-cd50-fdcd22fa09e1",
        "code": "ApprovalList_eo00aksDS_VisaOwner",
        "caption": "#ResourceString(ApprovalList_eo00aksDS_VisaOwner)#",
        "dataValueType": 10,
        "width": 175
      },
      {
        "id": "322150b1-a141-61ad-8113-a0a4df970d4d",
        "code": "ApprovalList_eo00aksDS_CreatedOn",
        "caption": "#ResourceString(ApprovalList_eo00aksDS_CreatedOn)#",
        "dataValueType": 7,
        "width": 175
      }
    ],
    "visible": true
  },
  "parentName": "ApprovalListGridContainer",
  "propertyName": "items",
  "index": 0
}
```

---

## 7. Common pitfalls

1. **Missing `modelConfigDiff` datasource** — `items` must be bound to a datasource collection; a plain array does not support WebSocket-triggered reloads.
2. **`recordId` not set** — without `recordId`, the WebSocket subscription for change notifications is never created; the grid does not auto-refresh when approval data changes.
3. **Wrong `masterRecordColumnValue` / `recordColumnName` pair** — `masterRecordColumnValue` is the page attribute holding the parent record ID; `recordColumnName` is the foreign-key column in the approval entity that links to that parent.
4. **`features.editable` not set to `false`** — the default `DataGrid` features allow inline editing; for approval lists set `"editable": false` to prevent accidental row editing.
5. **`reloadData` not wired** — the component emits `reloadData` on WebSocket message; wire it to a `crt.LoadDataRequest` handler so the grid refreshes automatically.
6. **Column `code` must match `datasourceName_columnName` format** — the platform uses `code` to map grid cells to datasource attributes. A mismatch silently renders blank cells.
7. **`primaryColumnName` must reference the Id column** — the grid uses this to identify selected rows. Always set it to the `<datasourceName>_Id` column.
8. **`items` attribute missing the top-level `modelConfig.path`** — declaring only the per-column `viewModelConfig.attributes` leaves the collection unbound; the grid renders empty. The attribute must carry `modelConfig: { path: "<datasourceName>" }` (see §2.2).
9. **Datasource not filtered to the parent record** — without either `masterRecordColumnName` on the datasource or a `dependencies` entry (`attributePath` → approval FK column, `relationPath` → `PDS.Id`), the grid lists approvals for *all* records, not the current one.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.ApprovalList"`, unique `name`, valid `parentName`.
- [ ] Datasource declared in `modelConfigDiff` with `masterRecordColumnName` pointing to the approval entity's foreign-key column.
- [ ] `items` attribute declared in `viewModelConfigDiff` as `isCollection: true` **with a top-level `modelConfig.path`** pointing to the datasource (not only per-column attributes).
- [ ] Datasource filtered to the current record (via `masterRecordColumnName` or a `dependencies` relation to `PDS.Id`).
- [ ] Parent `crt.GridContainer` (e.g. `ApprovalListGridContainer`) exists; optional refresh button / search filter added if needed (§2.4).
- [ ] `masterRecordColumnValue` bound to the page's primary record ID attribute.
- [ ] `primaryColumnName` set to the `<datasourceName>_Id` column.
- [ ] `columns` array has entries with valid `code`, `caption`, and `dataValueType`.
- [ ] `reloadData` output wired to a `crt.LoadDataRequest` handler.
- [ ] `recordId` bound so WebSocket auto-refresh works.
