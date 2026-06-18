# How to Add a MultiSelect (`crt.MultiSelect`) to a Freedom UI Page

> Audience: code agent inserting a `crt.MultiSelect` into a Creatio Freedom UI page schema.
>
> `crt.MultiSelect` is a chip-list picker bound to a *back-reference* (many-to-many) detail
> rather than a single lookup column. The canonical wiring is declarative: a single
> view-element insert with four fields (`selectSchemaName`, `selectColumnName`,
> `recordRelationColumnName`, `recordId`) is consumed by `MultiSelectPreprocessor`, which
> generates the datasources, view-model attributes, and request handlers for selecting,
> adding, and removing junction rows.

For the underlying form-control conventions (labels, tooltips), see crt.Input guide. This document focuses on multi-select-specific configuration.

## Metadata

- **Category**: fields
- **Container**: no
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`
- **Typical children**: none (selected values are preprocessor-managed from the junction table)

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.MultiSelect"` and the four select-configuration fields described below. |

`MultiSelectPreprocessor` (extends `BaseSelectPreprocessor`) reads the four fields and:

- Creates an embedded `crt.EntityDataSource` for the junction table (named after the element, e.g. `MultiSelect_<id>DS`).
- Declares the `items` collection attribute (selected junction rows) and the `listItems` collection attribute (available lookup values) with the right `modelConfig.path` plumbing.
- Wires default request handlers for `showList`, `paginationChange`, `selectedItemsChange`, and `deleteSelectedItems` that create/delete junction rows.

You do **not** need to declare datasources, attributes, or handlers manually.

### 1.1 Naming convention

```
MultiSelect_<id>            // view element name
```

The preprocessor derives all attribute / datasource names from the element name (see `buildMultiSelectAttributeNames`).

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "MultiSelect_xkp4r",
  "parentName": "SideAreaProfileContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.MultiSelect",
    "label": "#ResourceString(MultiSelect_xkp4r_label)#",
    "labelPosition": "auto",
    "placeholder": "",
    "tooltip": "",
    "visible": true,

    "recordId": "$Id",
    "recordRelationColumnName": "UsrContact",
    "selectSchemaName": "UsrContactTagTable",
    "selectColumnName": "UsrTag",

    "layoutConfig": { "column": 1, "row": 2, "colSpan": 1, "rowSpan": 1 }
  }
}
```

That's the full schema. The preprocessor generates the rest at page-load time.

### 2.2 What the four select fields mean

The MultiSelect is a UI over a junction table. To wire it you describe that junction:

| Field | What it points to | Example |
|---|---|---|
| `selectSchemaName` | The junction table's entity schema name. | `"UsrContactTagTable"` |
| `recordRelationColumnName` | Column in the junction that holds the FK to the **master record** (the page's entity). | `"UsrContact"` |
| `selectColumnName` | Column in the junction that holds the FK to the **lookup** (the items the user picks). | `"UsrTag"` |
| `recordId` | Page attribute carrying the current master record id (typically `"$Id"`). | `"$Id"` |

The preprocessor reads `selectColumnName`'s column metadata to discover the lookup entity (it follows the FK), so there is no need to specify the lookup schema separately. If the lookup schema differs from the column's referenced type, set the optional `listSchemaName` to override.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.MultiSelect` are in `ComponentRegistry.json` under `componentType: "crt.MultiSelect"`. This guide covers
only the assembly mechanics — what to put where in `viewConfigDiff` /
`viewModelConfigDiff` / `modelConfigDiff`.

---

## 4. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — single insert; no datasources or attributes needed
{
  "operation": "insert",
  "name": "TagsPicker",
  "parentName": "SideAreaProfileContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.MultiSelect",
    "label": "#ResourceString(TagsPicker_label)#",
    "labelPosition": "auto",
    "visible": true,
    "recordId": "$Id",
    "recordRelationColumnName": "UsrContact",
    "selectSchemaName": "UsrContactTagTable",
    "selectColumnName": "UsrTag",
    "layoutConfig": { "column": 1, "row": 2, "colSpan": 1, "rowSpan": 1 }
  }
}
```

---

## 5. Common pitfalls

1. **Confusing `crt.MultiSelect` with `crt.ComboBox` + `useMultiChoice`** — `ComboBox.useMultiChoice` works on a **single** column. `MultiSelect` works on a **back-reference** (junction table). They serve different cardinalities.
2. **Picking the wrong column for `recordRelationColumnName`** — it must be the junction column that points back to the master record, not the lookup. Mixing them produces an empty chip list (no rows match).
3. **Forgetting `recordId: "$Id"`** — the master record id is how the preprocessor filters the junction collection. Without it, the chip shows all junction rows for all records.
4. **Manually declaring `items` / `listItems` attributes or a datasource (`crt.EntityDataSource`) in `modelConfigDiff`** — the preprocessor will collide with your declarations. Trust the preprocessor; only override via `listSchemaName` or `disableManualSave` for advanced cases. (Note: there is no `crt.LookupDataSource` type — emitting it fails at runtime; use `crt.EntityDataSource` for any custom datasource needs.)
5. **`wrap: false` on a long chip list** — works, but the horizontal scrollbar is easy to miss. Prefer `wrap: true` unless the layout cell is narrow.
6. **`required: true` without `disableManualSave: false`** — required validation is also preprocessor-driven; setting `disableManualSave: true` skips it. Leave `disableManualSave` unset unless you intend custom save logic.

---

## 6. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.MultiSelect"`, unique `name`, `propertyName: "items"`.
- [ ] All four select-config fields set: `selectSchemaName`, `selectColumnName`, `recordRelationColumnName`, `recordId`.
- [ ] `selectSchemaName` is the **junction** entity (e.g. `<Master><Lookup>Table`), not the lookup itself.
- [ ] `recordRelationColumnName` is the junction column whose FK points back to the master record's entity.
- [ ] `selectColumnName` is the junction column whose FK points at the lookup entity.
- [ ] `recordId: "$Id"` (or the equivalent master-record id attribute).
- [ ] `label` provided.
- [ ] `wrap` set intentionally.
- [ ] `layoutConfig` provided.
- [ ] No manual `modelConfigDiff` datasources, no manual `viewModelConfigDiff` attributes, no manual `items` / `listItems` / `showList` / `selectedItemsChange` / `deleteSelectedItems` wiring — the preprocessor generates them.
