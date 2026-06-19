# How to Add Communication Options (`crt.CommunicationOptions`) to a Freedom UI Page

> Audience: code agent inserting a `crt.CommunicationOptions` into a Creatio Freedom UI page schema.
> A `crt.CommunicationOptions` is a grid-based editor for a **contact's or account's** communication
> detail list (phones, emails, social accounts). It renders one row per communication entry from a
> bound collection attribute and supports inline add/remove/edit.

## Metadata

- **Category**: fields
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.TabContainer`, root page container
- **Typical children**: none

> **Scope — backed by `ContactCommunication` / `AccountCommunication` only.**
> The component edits the standard `ContactCommunication` (a Contact's phones/emails/web) or
> `AccountCommunication` (an Account's). The page entity does **not** have to be `Contact`/`Account`: it
> binds to a Contact/Account **reference** through `masterRecordColumnValue` — `"$Id"` when the page entity
> itself is `Contact`/`Account`, or a Contact/Account lookup attribute on any other entity (shipped
> `CommOptionsAutotest_FormPage` does this on a custom entity). What is **not** supported is pointing it at a
> custom communication-like table — it expects the standard `CommunicationType` / `Number` / master-FK shape
> and fails at runtime otherwise (§5, pitfall 4). For a custom record's own "type/value" detail use a
> `crt.DataGrid` over your custom entity instead.

---

## 1. Mental model — the 3 places you must edit

Like `crt.DataGrid`, this is a **collection-bound** element. The `items` input expects a live
`BaseViewModelCollection`, so all three diff sections must be filled in lockstep:

| # | Section | What you add |
|---|---|---|
| 1 | `modelConfigDiff` | A `merge` op that registers a `crt.EntityDataSource` over `ContactCommunication` / `AccountCommunication` under `dataSources`. **Always present.** |
| 2 | `viewModelConfigDiff` | A `merge` op that registers a **collection attribute** (`isCollection: true`, `modelConfig.path` → the datasource, one nested attribute per column). **Always present.** |
| 3 | `viewConfigDiff` | An `insert` op with `type: "crt.CommunicationOptions"`, `items`, and the column-name inputs. **Always present.** |

> ⚠️ **Do not use the `viewModelConfig.generator: "EntityDataSource"` shorthand.** It renders in the
> Page Designer (static preview) but produces a non-collection value at runtime, so the `items` setter
> crashes with `TypeError: t.map is not a function` and the panel shows "No data" with no add control.
> Use the explicit `modelConfigDiff` datasource + `isCollection: true` form below — the same shape the
> designer's drag-and-drop emits.

### 1.1 Naming convention

```
CommunicationOptions_<id>          // view element name AND the collection attribute name
CommunicationOptions_<id>DS        // datasource key in modelConfigDiff
$CommunicationOptions_<id>         // value of "items" in viewConfig (prefix with $)
CommunicationOptions_<id>DS_Id     // primary row attribute (= "<DS>_<ColumnCode>")
CommunicationOptions_<id>DS_Number // every row attribute = "<DS>_<ColumnCode>"
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
      "CommunicationOptions_z4g5fjxDS": {
        "type": "crt.EntityDataSource",
        "scope": "viewElement",
        "config": {
          "entitySchemaName": "ContactCommunication",
          "attributes": {
            "Number":                         { "path": "Number" },
            "CommunicationType":              { "path": "CommunicationType" },
            "CommunicationTypeName":          { "path": "CommunicationType.Name", "type": "ForwardReference" },
            "CommunicationTypeDisplayFormat": { "path": "CommunicationType.DisplayFormat", "type": "ForwardReference" },
            "Primary":                        { "path": "Primary" },
            "NonActual":                      { "path": "NonActual" },
            "CreatedOn":                      { "path": "CreatedOn" }
          }
        }
      }
    }
  }
}
```

| Field | Notes |
|---|---|
| `type` | Always `"crt.EntityDataSource"`. |
| `config.entitySchemaName` | `"ContactCommunication"` for a Contact page, `"AccountCommunication"` for an Account page. |
| `config.attributes` | The columns to load, keyed by attribute name. `CommunicationTypeName` / `CommunicationTypeDisplayFormat` are **forward references** off the `CommunicationType` lookup: their `path` is the dotted entity path (`CommunicationType.Name` / `CommunicationType.DisplayFormat`) and they must carry `"type": "ForwardReference"`. Keep both — `DisplayFormat` drives the per-row editor template, `Name` is the human label. |
| `scope` | `"viewElement"` so the datasource disposes with the element. |

### 2.2 Bind the collection attribute (`viewModelConfigDiff` entry)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "CommunicationOptions_z4g5fjx": {
        "isCollection": true,
        "modelConfig": {
          "path": "CommunicationOptions_z4g5fjxDS",
          "sortingConfig": { "default": [{ "columnName": "CreatedOn", "direction": "asc" }] }
        },
        "viewModelConfig": {
          "attributes": {
            "CommunicationOptions_z4g5fjxDS_Id":                            { "modelConfig": { "path": "CommunicationOptions_z4g5fjxDS.Id" } },
            "CommunicationOptions_z4g5fjxDS_Number":                        { "modelConfig": { "path": "CommunicationOptions_z4g5fjxDS.Number" } },
            "CommunicationOptions_z4g5fjxDS_CommunicationType":             { "modelConfig": { "path": "CommunicationOptions_z4g5fjxDS.CommunicationType" } },
            "CommunicationOptions_z4g5fjxDS_CommunicationTypeName":         { "modelConfig": { "path": "CommunicationOptions_z4g5fjxDS.CommunicationTypeName" } },
            "CommunicationOptions_z4g5fjxDS_CommunicationTypeDisplayFormat":{ "modelConfig": { "path": "CommunicationOptions_z4g5fjxDS.CommunicationTypeDisplayFormat" } },
            "CommunicationOptions_z4g5fjxDS_Primary":                       { "modelConfig": { "path": "CommunicationOptions_z4g5fjxDS.Primary" } },
            "CommunicationOptions_z4g5fjxDS_NonActual":                     { "modelConfig": { "path": "CommunicationOptions_z4g5fjxDS.NonActual" } },
            "CommunicationOptions_z4g5fjxDS_CreatedOn":                     { "modelConfig": { "path": "CommunicationOptions_z4g5fjxDS.CreatedOn" } }
          }
        }
      }
    }
  }
}
```

Rules:
- `isCollection: true` is **mandatory** — it is what materializes `items` as a `BaseViewModelCollection`. Omitting it is the direct cause of the runtime `t.map` crash (§ 5, pitfall 1).
- `modelConfig.path` points at the **datasource key** (`<DS>`); the per-row attributes point at `"<DS>.<ColumnCode>"`.
- Forward-ref row attributes (`<DS>_CommunicationTypeName`, `<DS>_CommunicationTypeDisplayFormat`) point `modelConfig.path` at the **flattened datasource attribute name** (`<DS>.CommunicationTypeName`), **not** the dotted entity path (`<DS>.CommunicationType.Name`). The bare `CommunicationType` attribute stays a plain lookup column (`<DS>.CommunicationType`).
- `<DS>_Id` is loaded implicitly — `crt.EntityDataSource` always fetches the primary key, so `Id` needs no entry in the datasource `config.attributes` (§ 2.1); declare it only here, because `primaryColumnName` (§ 2.3) resolves against this view-model attribute.

### 2.3 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "CommunicationOptions_z4g5fjx",
  "parentName": "SideAreaProfileFieldFlexContainer",   // any container in the page tree
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.CommunicationOptions",
    "items": "$CommunicationOptions_z4g5fjx",
    "masterRecordColumnName": "Contact",                // "Account" on an Account page
    "masterRecordColumnValue": "$Id",
    "primaryColumnName": "CommunicationOptions_z4g5fjxDS_Id",
    "typeColumnName": "CommunicationOptions_z4g5fjxDS_CommunicationType",
    "numberColumnName": "CommunicationOptions_z4g5fjxDS_Number",
    "displayFormatColumnName": "CommunicationOptions_z4g5fjxDS_CommunicationTypeDisplayFormat",
    "readonly": false,
    "columnsCount": 1,
    "labelPosition": "above",
    "showNoDataPlaceholder": false,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

Drop `layoutConfig` when the parent is a `crt.FlexContainer`.

> **The column-name inputs (`typeColumnName` / `numberColumnName` / `primaryColumnName` /
> `displayFormatColumnName`) are optional.** When the collection's row attributes follow the
> `<collection>DS_<ColumnCode>` convention (§2.2), the component derives them — the designer drop and
> shipped pages (e.g. `CommOptionsAutotest_FormPage`) omit these inputs entirely. The element inputs that
> are actually needed are `items`, `masterRecordColumnName`, and `masterRecordColumnValue`.
>
> If you do set them, they are **view-model attribute names, not bare entity columns** — the web component
> reads each row as `row[typeColumnName]` / `row.getControl(numberColumnName)` /
> `row.attributes[displayFormatColumnName]`, so they must equal the `<DS>_<ColumnCode>` keys from §2.2
> (e.g. `"CommunicationOptions_z4g5fjxDS_Number"`, not `"Number"`). (Mobile pages use the bare entity column name.)

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.CommunicationOptions` are in `ComponentRegistry.json` under `componentType: "crt.CommunicationOptions"`.
This guide covers only the assembly mechanics.

---

## 4. Copy-paste minimal example (Contact page)

```jsonc
// 1) modelConfigDiff entry
{
  "operation": "merge",
  "path": [],
  "values": {
    "dataSources": {
      "CommunicationOptions_mainDS": {
        "type": "crt.EntityDataSource",
        "scope": "viewElement",
        "config": {
          "entitySchemaName": "ContactCommunication",
          "attributes": {
            "Number":                         { "path": "Number" },
            "CommunicationType":              { "path": "CommunicationType" },
            "CommunicationTypeName":          { "path": "CommunicationType.Name", "type": "ForwardReference" },
            "CommunicationTypeDisplayFormat": { "path": "CommunicationType.DisplayFormat", "type": "ForwardReference" },
            "Primary":                        { "path": "Primary" },
            "NonActual":                      { "path": "NonActual" },
            "CreatedOn":                      { "path": "CreatedOn" }
          }
        }
      }
    }
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
      "CommunicationOptions_main": {
        "isCollection": true,
        "modelConfig": {
          "path": "CommunicationOptions_mainDS",
          "sortingConfig": { "default": [{ "columnName": "CreatedOn", "direction": "asc" }] }
        },
        "viewModelConfig": {
          "attributes": {
            "CommunicationOptions_mainDS_Id":                            { "modelConfig": { "path": "CommunicationOptions_mainDS.Id" } },
            "CommunicationOptions_mainDS_Number":                        { "modelConfig": { "path": "CommunicationOptions_mainDS.Number" } },
            "CommunicationOptions_mainDS_CommunicationType":             { "modelConfig": { "path": "CommunicationOptions_mainDS.CommunicationType" } },
            "CommunicationOptions_mainDS_CommunicationTypeName":         { "modelConfig": { "path": "CommunicationOptions_mainDS.CommunicationTypeName" } },
            "CommunicationOptions_mainDS_CommunicationTypeDisplayFormat":{ "modelConfig": { "path": "CommunicationOptions_mainDS.CommunicationTypeDisplayFormat" } },
            "CommunicationOptions_mainDS_Primary":                       { "modelConfig": { "path": "CommunicationOptions_mainDS.Primary" } },
            "CommunicationOptions_mainDS_NonActual":                     { "modelConfig": { "path": "CommunicationOptions_mainDS.NonActual" } },
            "CommunicationOptions_mainDS_CreatedOn":                     { "modelConfig": { "path": "CommunicationOptions_mainDS.CreatedOn" } }
          }
        }
      }
    }
  }
}
```

```jsonc
// 3) viewConfigDiff entry
{
  "operation": "insert",
  "name": "CommunicationOptions_main",
  "parentName": "SideAreaProfileFieldFlexContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.CommunicationOptions",
    "items": "$CommunicationOptions_main",
    "masterRecordColumnName": "Contact",
    "masterRecordColumnValue": "$Id",
    "primaryColumnName": "CommunicationOptions_mainDS_Id",
    "typeColumnName": "CommunicationOptions_mainDS_CommunicationType",
    "numberColumnName": "CommunicationOptions_mainDS_Number",
    "displayFormatColumnName": "CommunicationOptions_mainDS_CommunicationTypeDisplayFormat",
    "readonly": false,
    "columnsCount": 1,
    "labelPosition": "above",
    "showNoDataPlaceholder": false
  }
}
```

---

## 5. Common pitfalls

1. **`viewModelConfig.generator` shorthand instead of a real datasource (the `t.map` crash).** Declaring
   the attribute as `{ "value": null, "viewModelConfig": { "generator": "EntityDataSource", ... } }`
   *looks* right in the Page Designer (which renders a static preview with no runtime binding) but at
   runtime the `items` setter receives a non-collection value, `setCollectionValidator` calls `.map` on
   it, and the console throws `TypeError: t.map is not a function`. The panel shows "No data" with no add
   control. **Fix:** use the explicit `modelConfigDiff` `crt.EntityDataSource` + a `viewModelConfigDiff`
   attribute with `isCollection: true` (§ 2.1–2.2).
2. **Missing `isCollection: true`.** Same crash as above — without it `items` is never materialized as a
   `BaseViewModelCollection`.
3. **Column-name inputs set to bare entity columns.** `typeColumnName` / `numberColumnName` /
   `primaryColumnName` / `displayFormatColumnName` must be the `<DS>_<ColumnCode>` view-model attribute
   names (§ 2.3), not `"Number"` / `"CommunicationType"`. Bare names resolve to `undefined` per row and the
   validators/templates break.
4. **Wrong / custom entity.** Use `ContactCommunication` (Contact page) or `AccountCommunication` (Account
   page). A custom `Usr…CommunicationOption`-style table has no `CommunicationType` / `Number` / master
   `Contact` columns wired the way this component expects and will not render — use a `crt.DataGrid` over
   the custom entity instead (see § Scope above).
5. **`masterRecordColumnName` mismatch.** It must be the foreign-key column on the communication entity
   that points back to the page record: `"Contact"` for `ContactCommunication`, `"Account"` for
   `AccountCommunication`.
6. **`masterRecordColumnValue` not bound to the page's Contact/Account reference.** It must carry the
   Contact/Account **id** the communication rows belong to: `"$Id"` when the page entity itself is
   `Contact`/`Account`, otherwise a Contact/Account **lookup attribute** on the page (e.g.
   `"$PDS_<ContactColumn>"` — shipped `CommOptionsAutotest_FormPage` binds it to a Contact lookup column).
   A display value or a non-Contact id breaks the foreign-key filter.
7. **`columnsCount` of 0.** Coerced to `1` at runtime when zero/falsy; always pass a positive integer.
8. **`showNoDataPlaceholder: true` on narrow layouts.** The placeholder image is tall; use `false` in
   compact sidebar layouts.

---

## 6. Quick checklist

- [ ] `merge` op in `modelConfigDiff` (`path: []`) registers a `crt.EntityDataSource` over `ContactCommunication` / `AccountCommunication` with `scope: "viewElement"` and a `config.attributes` map.
- [ ] `merge` op in `viewModelConfigDiff` (`path: []`) declares the collection attribute with `isCollection: true`, `modelConfig.path` → `<DS>`, and one nested attribute per column.
- [ ] `insert` op in `viewConfigDiff` with `type: "crt.CommunicationOptions"`, `items: "$CommunicationOptions_<id>"`, unique `name`, valid `parentName`, `propertyName: "items"`.
- [ ] `typeColumnName` / `numberColumnName` / `primaryColumnName` / `displayFormatColumnName` set to the `<DS>_<ColumnCode>` attribute names, **not** bare entity columns.
- [ ] `masterRecordColumnName` is the FK column (`"Contact"` / `"Account"`); `masterRecordColumnValue` is `"$Id"`.
- [ ] `columnsCount` is a positive integer; `readonly` reflects the form's edit mode.
- [ ] **Not** used over a custom entity — Contact/Account communication data only.
