# How to Add a DataGrid (`crt.DataGrid`) to a Freedom UI Page

> Audience: code agent inserting a `crt.DataGrid` into a Creatio Freedom UI page schema
>
> A Freedom UI page schema is a single JS file with these sections:
> `viewConfigDiff`, `viewModelConfigDiff`, `modelConfigDiff`, `handlers`. Adding a DataGrid requires
> coordinated changes in the first **three**.

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`, `crt.TabContainer`
- **Typical children**: `crt.MenuItem`, `crt.MenuLabel`, `crt.MenuDivider` (in the `bulkActions` slot)

---

## 1. Mental model — the 3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `modelConfigDiff` | A `merge` op that registers a `crt.EntityDataSource` under `dataSources`. |
| 2 | `viewModelConfigDiff` | A `merge` op that registers a collection attribute (`isCollection: true`, `modelConfig.path`, per-column attribute paths) under `attributes`. |
| 3 | `viewConfigDiff` | An `insert` op with `type: "crt.DataGrid"`, `items`, `columns`, `primaryColumnName`, `layoutConfig`. |

### 1.1 How diff sections work

All three sections are **arrays of diff operations**, not plain objects. The runtime merges every operation into a base configuration. Cheat-sheet for `merge`:

- `path` is **always `Array<string>`**, never a bare string. Use `[]` for the section root, `["dataSources"]` to descend one level, etc.
- When `path: []`, your `values` must wrap the root key — `values: { dataSources: { … } }` or `values: { attributes: { … } }`.
- When `path: ["dataSources"]`, the inner object goes directly into `values: { … }` and is merged into the `dataSources` map.

(See `JsonDiffOperation` in `libs/studio-enterprise/util/low-code/src/lib/models/json-diff-operation.ts`.)

### 1.2 Naming convention

Follow this convention so the Page Designer recognises the grid later:

```
DataGrid_<id>         // view element name
DataGrid_<id>DS       // datasource key in modelConfigDiff
$DataGrid_<id>        // value of "items" in viewConfig (prefix with $)
DataGrid_<id>DS_Id    // primary column attribute
DataGrid_<id>DS_Name  // every displayed column = "<DS>_<ColumnCode>"
```

`<id>` is any short unique slug (e.g. `z4g5fjx`). It has no runtime meaning.

---

## 2. Step-by-step recipe

### 2.1 Add the data source (`modelConfigDiff` entry)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "dataSources": {
      "DataGrid_z4g5fjxDS": {
        "type": "crt.EntityDataSource",
        "scope": "viewElement",
        "config": {
          "entitySchemaName": "Contact",
          "attributes": {
            "Name": { "path": "Name" }
          }
        }
      }
    }
  }
}
```

| Field | Type | Default | Notes |
|---|---|---|---|
| `type` | string | — | `"crt.EntityDataSource"` for standard grids. |
| `config.entitySchemaName` | string | — | **Required.** Target entity schema. |
| `config.attributes` | object | `{}` | Map of entity columns to load: `{ "<EntityCol>": { "path": "<EntityCol>" } }`. Add an entry for every column you display (lookups use dot-paths, e.g. `"Owner": { "path": "Owner" }`). The Page Designer always emits this block; omitting it may leave cells blank. |
| `scope` | string | `"page"` | **Almost always `"viewElement"`** so the source disposes when the grid is removed. Use `"page"` only when several elements on the same page must share one underlying data source (rare). Defaulting to `"page"` causes memory leaks and stale-cache bugs on grids embedded into tabs/cards. |

### 2.2 Bind the collection attribute (`viewModelConfigDiff` entry)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "DataGrid_z4g5fjx": {
        "isCollection": true,
        "modelConfig": { "path": "DataGrid_z4g5fjxDS" },
        "viewModelConfig": {
          "attributes": {
            "DataGrid_z4g5fjxDS_Id":   { "modelConfig": { "path": "DataGrid_z4g5fjxDS.Id"   } },
            "DataGrid_z4g5fjxDS_Name": { "modelConfig": { "path": "DataGrid_z4g5fjxDS.Name" } }
            // one entry per displayed column
          }
        }
      }
    }
  }
}
```

Rules:
- `isCollection: true` is mandatory; without it nothing renders.
- Per-column attribute paths use `"<DS>.<ColumnCode>"`. For a **lookup** column (read-only **or** editable) the path ends at the **bare reference column** — `"<DS>.Owner"`, `"<DS>.Status"` — **not** `.Name`. The cell then receives the full `LookupValue` and renders its `displayValue`; set `dataValueType: 10` and — to mirror what every OOTB list page emits — a `referenceSchemaName` on the column descriptor. Appending `.Name` (e.g. `"<DS>.Owner.Name"`) makes the cell read a bare string instead of a `LookupValue`, and unless that `.Name` sub-path is itself an exposed data-source attribute the binding fails to resolve and the column drops out as **"Column removed"** (pitfall #5). This is the OOTB pattern for both list and detail grids; for the editable specifics see § 5.1.

### 2.3 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "DataGrid_z4g5fjx",
  "parentName": "MainContainer",         // any container in the page tree
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.DataGrid",
    "layoutConfig": { "column": 1, "row": 2, "colSpan": 1, "rowSpan": 1 },
    "items": "$DataGrid_z4g5fjx",        // $-prefix references the collection attribute
    "primaryColumnName": "DataGrid_z4g5fjxDS_Id",
    "columns": [
      {
        "id": "a0916745-3823-6491-7a9f-f3076bd826d0",
        "code": "DataGrid_z4g5fjxDS_Name",
        "caption": "#ResourceString(DataGrid_z4g5fjxDS_Name)#",
        "dataValueType": 28
      }
    ]
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.DataGrid` are in `ComponentRegistry.json` under `componentType: "crt.DataGrid"`. This guide covers
only the assembly mechanics — what to put where in `viewConfigDiff` /
`viewModelConfigDiff` / `modelConfigDiff`. Column descriptors and the `features` object
are not in the registry — see § 4 and § 5 below.

---

## 4. Column descriptor

| Field | Type | Default | Notes |
|---|---|---|---|
| `id` | string (GUID) | — | Unique per column. Any GUID. |
| `code` | string | — | Must equal the **attribute name** in the bound collection (`<DS>_<col>`). |
| `path` | string | — | The entity column this cell reads, relative to the data source (e.g. `"Subject"`, `"Status"`). For a **lookup** column this is the **bare reference column** (`"Status"`), never `"Status.Name"`. |
| `caption` | string | `""` | Use `"#ResourceString(...)#"` for localized labels. |
| `dataValueType` | number | — | Numeric `DataValueType` — must match the entity column's real type. See § 4.1. |
| `referenceSchemaName` | string | — | **Emit on lookup columns (`dataValueType: 10`)** — every OOTB list page does, and the Page Designer's column editor reads it. Schema name of the lookup's reference entity (e.g. `"Contact"`, `"CaseStatus"`). It is a *design-time* field: the runtime read-only cell infers the reference schema from the bound entity-schema attribute, so omitting it does **not** by itself cause "Column removed" — that comes from the bound path not resolving (see pitfall #5). Not used on non-lookup columns. |
| `visible` | boolean | `true` | Show column. |

### 4.1 `DataValueType` values

Values come from `ɵDataValueType` in `libs/devkit/base/src/lib/public/enums/data-value-type.enum.ts`. The numbers below are authoritative — do not improvise.

| Value | Name | Use for |
|---|---|---|
| `0` | Guid | `Id` and other GUID columns |
| `1` | Text | Generic string, unspecified length |
| `4` | Integer | Whole numbers |
| `5` | Float | Real numbers |
| `6` | Money | Currency |
| `7` | DateTime | Date + time |
| `8` | Date | Date only |
| `9` | Time | Time only |
| `10` | Lookup | Foreign-key columns — bind the **bare reference column** (`"DataGrid_xDS.Owner"`), **not** `.Name`, and emit `referenceSchemaName` to mirror OOTB descriptors (the runtime can also infer it from the bound attribute; see pitfall #5). |
| `11` | Enum | Enumeration |
| `12` | Boolean | Yes/No |
| `18` | Color | Color hex string |
| `19` | LOCALIZABLE_STRING | Localizable short text |
| `25` | FILE | File reference |
| `27` | SHORT_TEXT | Short fixed-length text |
| `28` | MEDIUM_TEXT | Medium-length text — **default for `Contact.Name`-style columns** |
| `29` | MAXSIZE_TEXT | Maximum-length text |
| `30` | LONG_TEXT | Long text |
| `42` | PHONE_TEXT | Phone number |
| `43` | RICH_TEXT | Rich text / HTML |
| `44` | WEB_TEXT | Web link |
| `45` | EMAIL_TEXT | Email address |

> **Pitfall.** A wrong `dataValueType` silently breaks cell rendering — e.g. a `Lookup`-typed cell expects a `{ value, displayValue }` payload, while plain text cells expect a string. When in doubt, inspect the entity schema and copy the actual column type rather than guessing.

---

## 5. `features` object

```jsonc
"features": {
  "cells":  { "selection": { "enable": true } },        // cell-level selection
  "rows":   { "selection": { "enable": true, "multiple": true } },
  "columns": {
    "dragAndDrop": { "enable": true },
    "resizing":    { "enable": true },
    "sorting":     { "enable": true },
    "editing":     { "enable": true },
    "toolbar":     { "enable": true },                  // column-chooser toolbar
    "adding":      { "enable": true }
  },
  "editable":     { "enable": true },                    // inline edit globally on
  "hierarchical": { "enable": true },                    // tree-view mode
  "header":       { "enable": true },                    // show header row
  "operations":   { "enable": true }                     // row operations menu (⋯)
}
```

All inner `enable` flags default to `false`.

### 5.1 Editable lookup columns (`dataValueType: 10` + inline edit)

When the grid is editable (`features.editable.enable: true`) the runtime auto-builds the inline editor for
each column from its `dataValueType` — you normally do **not** hand-write an `editingCellView`. For a
**lookup** column (`dataValueType: 10`) the grid generates a `crt.DataTableEditLookupCell` combo-box and loads
its dropdown options from the lookup entity behind the column. For that to work the bound attribute must
resolve to the **lookup reference column**, not to a display string:

- **EntityDataSource (`modelConfigDiff`).** Expose the **reference column itself** under `config.attributes` —
  the bare reference that holds the foreign key, e.g. `"Type": { "path": "Type" }`, **not** `"Type.Name"`.
- **Collection attribute (`viewModelConfigDiff`).** Bind the column's nested attribute to that reference
  column: `"<DS>_Type": { "modelConfig": { "path": "<DS>.Type" } }` — the path ends at the reference column,
  **not** at `.Name`.
- **Column descriptor (`viewConfigDiff`).** Keep `dataValueType: 10` and leave `editingCellView` unset for the
  standard case. The editing-cell preprocessor adds `{ "type": "crt.DataTableEditLookupCell", … }` and wires
  the dropdown's vocabulary, type-ahead search, and value persistence from the reference column's
  `referenceSchemaName`.
- **Adding rows & lookup records.** `features.editable.enable: true` lets users edit existing rows (pick a
  lookup value — this is what clears the "No data" state). Set `features.editable.itemsCreation: true` to show
  the inline **+ Add new record** button that creates new **grid rows** (detail records). The separate
  **"+ New …" action inside the lookup dropdown** (create a brand-new lookup record on the fly) is governed by
  `features.editable.lookupItemsCreation`, **not** by `itemsCreation` — the editing-cell preprocessor gates
  that add-record list action on `lookupItemsCreation` alone.

The cell value at runtime is a `LookupValue` object (`{ value, displayValue, … }`). Both the read-only display
cell (`crt.DataTableLookupCell`, which renders `value.displayValue`) and the inline editor read it — that is
why the **reference column** is the correct binding: it yields the full `LookupValue`, whereas a `.Name` path
yields a bare string.

#### Why an editable lookup column shows "No data" + `Cannot read properties of undefined (reading 'name')`

If the lookup column is bound to the display sub-path (`"path": "<DS>.Type.Name"`) instead of the bare
reference column, the editor's target field becomes `Name`, a plain-text attribute with **no
`referenceSchemaName`**. The combo-box then has no lookup entity to query, so the dropdown shows **"No data"**
even when the lookup table is full; and the bound value is a string instead of a `LookupValue`, so the cell
dereferences a property (`name` / `displayValue`) on a non-object and throws
`TypeError: Cannot read properties of undefined (reading 'name')`. **Fix:** bind the column to the reference
column (`"<DS>.Type"`), keep `dataValueType: 10`, expose `Type` in the EntityDataSource `config.attributes`, and
enable `features.editable`.

> Need a non-standard editor — a fixed static option list, a custom `showList` handler, or extra `listActions`?
> Set `editingCellView` explicitly for those cases only; see `crt.DataTableEditLookupCell` in
> `get-component-info` and its `data-table-edit-lookup-cell.component.md` recipe.

> A `.Name` path is fine **only** as a *separate auxiliary attribute* feeding a custom `cellView` caption (e.g. a `crt.Link` showing the lookup's display name) — never as the lookup column's own binding, which stays the bare reference column.

---

## 6. Copy-paste minimal example

```jsonc
// 1) viewConfigDiff entry
{
  "operation": "insert",
  "name": "DataGrid_contacts",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.DataGrid",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 2, "rowSpan": 4 },
    "items": "$DataGrid_contacts",
    "primaryColumnName": "DataGrid_contactsDS_Id",
    "features": { "rows": { "selection": { "enable": true, "multiple": true } } },
    "columns": [
      { "id": "11111111-1111-1111-1111-111111111111",
        "code": "DataGrid_contactsDS_Name",
        "caption": "#ResourceString(DataGrid_contactsDS_Name)#",
        "dataValueType": 28 },             // 28 = MEDIUM_TEXT (Contact.Name)
      { "id": "22222222-2222-2222-2222-222222222222",
        "code": "DataGrid_contactsDS_Email",
        "caption": "#ResourceString(DataGrid_contactsDS_Email)#",
        "dataValueType": 45 },             // 45 = EMAIL_TEXT
      { "id": "33333333-3333-3333-3333-333333333333",
        "code": "DataGrid_contactsDS_Account",
        "caption": "#ResourceString(DataGrid_contactsDS_Account)#",
        "dataValueType": 10,               // 10 = Lookup → bind the bare reference column "Account", not "Account.Name"
        "referenceSchemaName": "Account" } // matches OOTB; the cell receives the full LookupValue
    ]
  }
}
```

```jsonc
// 2) viewModelConfigDiff entry
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "DataGrid_contacts": {
        "isCollection": true,
        "modelConfig": { "path": "DataGrid_contactsDS" },
        "viewModelConfig": {
          "attributes": {
            "DataGrid_contactsDS_Id":      { "modelConfig": { "path": "DataGrid_contactsDS.Id" } },
            "DataGrid_contactsDS_Name":    { "modelConfig": { "path": "DataGrid_contactsDS.Name" } },
            "DataGrid_contactsDS_Email":   { "modelConfig": { "path": "DataGrid_contactsDS.Email" } },
            "DataGrid_contactsDS_Account": { "modelConfig": { "path": "DataGrid_contactsDS.Account" } } // bare reference column
          }
        }
      }
    }
  }
}
```

```jsonc
// 3) modelConfigDiff entry
{
  "operation": "merge",
  "path": [],
  "values": {
    "dataSources": {
      "DataGrid_contactsDS": {
        "type": "crt.EntityDataSource",
        "scope": "viewElement",
        "config": {
          "entitySchemaName": "Contact",
          "attributes": {
            "Name":    { "path": "Name" },
            "Email":   { "path": "Email" },
            "Account": { "path": "Account" }   // expose the reference column so the lookup cell resolves
          }
        }
      }
    }
  }
}
```

---

## 7. Common pitfalls

1. **`items` value must start with `$`** — `"$DataGrid_x"`. Without `$` the grid receives a literal string and renders empty.
2. **`isCollection: true` is mandatory** on the binding attribute. Missing flag = no rows.
3. **`primaryColumnName` must exist** in the column attribute list, even if not rendered. Selection/operations silently break without it.
4. **Every column `code` must have a matching attribute** under the collection's `viewModelConfig.attributes`.
5. **Lookup columns (read-only _and_ editable) must bind to the bare reference column** (`"path": "DataGrid_xDS.Owner"`) at every level — `config.attributes`, the collection attribute path, and the column descriptor — and set `dataValueType: 10`. Binding to `.Name` (`"DataGrid_xDS.Owner.Name"`) makes the cell read a bare string instead of a `LookupValue`, and — unless that `.Name` sub-path is also exposed as a data-source attribute — the binding fails to resolve and the column drops out as the **"Column removed"** placeholder (the caption falls back to that text whenever the bound path matches no data-source attribute). Also emit a `referenceSchemaName` on the column descriptor (`"Contact"`, `"CaseStatus"`, …) to match OOTB output and feed the design-time column editor; it is **not** what triggers "Column removed" — the runtime read-only cell infers the reference schema from the bound entity-schema attribute. Mirror what the standard list page emits (validated in OOTB `Cases_ListPage` / `Cases_FormPage`). Editable specifics: § 5.1.
6. **`path` is an `Array<string>`**, not a bare string. `"path": "dataSources"` is wrong — use `"path": []` with `"values": { "dataSources": { … } }`, or `"path": ["dataSources"]` with the inner object as `values`.
7. **Use the real entity column type for `dataValueType`** — `Contact.Name` is `MEDIUM_TEXT (28)`, not `Text (1)`; `Contact.Email` is `EMAIL_TEXT (45)`. Cross-check the actual entity schema instead of defaulting to `1`.
8. **`config.attributes` should list every displayed column** in the EntityDataSource. The Page Designer always emits it; omitting it can leave columns empty depending on platform version.
9. **`placeholder` is a `contentSlot`, not a top-level `values` field.** To override the "no data" empty state, insert a child element with `parentName: "<DataGrid>"`, `propertyName: "placeholder"`. Setting `"placeholder"` inside `values` is silently ignored.
10. **`scope: "page"` on a transient grid** (e.g. one rendered inside a tab, dialog, mini-page, or any view fragment that mounts/unmounts) leaks the underlying data source for the lifetime of the page and serves stale rows after the grid is destroyed and re-created. Default to `scope: "viewElement"` unless multiple elements explicitly need to share the same DS instance across the whole page.
11. **Pairing the grid with `crt.SearchFilter`.** The filter targets the **collection attribute name** (the key under `viewModelConfigDiff[].values.attributes`, e.g. `"DataGrid_contacts"`), never the DS key (`"DataGrid_contactsDS"`) and never a fabricated `_List`/`_Items` suffix. See `search-filter.component.md` § 1.2 for the full pipeline diagram.
12. **Editable lookup column shows "No data" and the console throws `Cannot read properties of undefined (reading 'name')`.** The lookup column is bound to the display path (`"<DS>.Type.Name"`) — or its reference column is missing from the EntityDataSource `config.attributes` — so the inline combo-box has no lookup entity to load (dropdown is empty) and the cell value is a string instead of a `LookupValue` (the property-on-undefined crash). Bind the column to the **reference column** (`"<DS>.Type"`), keep `dataValueType: 10`, expose `Type` in `config.attributes`, and enable `features.editable`. See § 5.1.

---

## 8. Quick checklist

- [ ] New `merge` op in `modelConfigDiff` with `path: []`, valid `entitySchemaName`, `scope`, and `config.attributes` listing each displayed column.
- [ ] New `merge` op in `viewModelConfigDiff` with `path: []`, collection attribute (`isCollection: true`), correct `modelConfig.path`, and one nested attribute per column.
- [ ] `insert` op in `viewConfigDiff` with `type: "crt.DataGrid"`, `items: "$…"`, `primaryColumnName`, `columns`, and `layoutConfig`.
- [ ] All column `code` values exist as nested attributes; each `dataValueType` matches the real entity column type (see § 4.1).
- [ ] `parentName` references an existing container, `propertyName: "items"`, and a unique `index`.
- [ ] Lookup columns (`dataValueType: 10`, read-only **and** editable) bind every level to the **bare reference column** (`"<DS>.Type"`, not `.Name`), expose that reference column in the EntityDataSource `config.attributes`, and emit `referenceSchemaName` on the column descriptor to mirror OOTB descriptors (the runtime infers it from the bound attribute, so it is not what causes "Column removed" — see pitfall #5 and § 5.1).
