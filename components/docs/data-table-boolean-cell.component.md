# How to Use a Boolean Cell (`crt.TableBooleanCell`) in a DataGrid Column

> Audience: code agent configuring a `crt.DataGrid` column to render and edit boolean values.
>
> `crt.TableBooleanCell` is a checkbox-style cell renderer/editor for boolean columns in a `crt.DataGrid`.
> It is **not inserted directly** into `viewConfigDiff`; the platform creates it automatically via
> `BooleanCellViewElementConfigCreator` for any column whose `dataValueType` is `DataValueType.Boolean`.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: embedded inside a `crt.DataGrid` column `cellView` — not a standalone view element
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` (DataGrid `columns`) | A column definition with `dataValueType: 12` (Boolean). The platform wires `crt.TableBooleanCell` automatically. |

The cell is **platform-managed**: you declare the DataGrid column with `dataValueType: 12`; the
`BooleanCellViewElementConfigCreator` service constructs the `cellView` at runtime, binding `control`,
`readonly`, and `readiness` automatically. Direct `cellView` configuration is not needed in typical schemas.

---

## 2. Step-by-step recipe

### 2.1 Add a boolean column to `crt.DataGrid`

```jsonc
// viewConfigDiff — modify or insert a DataGrid and add a boolean column
{
  "operation": "insert",
  "name": "DataGrid_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.DataGrid",
    "items": "$GridItems",
    "primaryColumnName": "GridDS_Id",
    "columns": [
      {
        "id": "col-guid-here",
        "code": "GridDS_IsActive",
        "path": "IsActive",
        "caption": "#ResourceString(GridDS_IsActive)#",
        "dataValueType": 12
      }
    ],
    "features": {
      "editable": { "enable": true }
    }
  }
}
```

When `editable.enable` is `true`, the platform creates a checkbox that the user can toggle in-place.
When editing is disabled, the cell renders as a read-only checkbox icon.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.TableBooleanCell` are in `ComponentRegistry.json` under `componentType: "crt.TableBooleanCell"`.
This guide covers only the assembly mechanics.

---

## 5. Copy-paste minimal example

```jsonc
// Column definition inside crt.DataGrid.columns
{
  "id": "5a3d12f0-ab01-4567-89cd-ef0123456789",
  "code": "GridDS_Primary",
  "path": "Primary",
  "caption": "#ResourceString(GridDS_Primary)#",
  "dataValueType": 12
}
```

The platform binds `control: "$GridDS_Primary"`, `readonly: <edit-mode-expression>`, and
`readiness: "$BusinessRulesActivated"` automatically.

---

## 7. Common pitfalls

1. **Inserting `crt.TableBooleanCell` directly.** These cells are constructed programmatically; direct `insert` ops are not supported and will not work as expected.
2. **Wrong `dataValueType`.** Use `12` (the `DataValueType.Boolean` constant) for boolean columns; any other value selects a different cell creator.
3. **`editable.enable: false` with `readonly: false`.** The readonly state is computed from the editing feature flags; setting `readonly` explicitly in the column has no effect — the creator overrides it.
4. **Expecting toggle without `readiness`.** The cell defers toggle until the `readiness` binding (business rules activation) resolves; the checkbox appears pending until then.
5. **Missing `features.editable`** on the DataGrid while expecting interactive checkboxes. Without `editable.enable: true`, all boolean cells render as read-only icons.

---

## 8. Quick checklist

- [ ] Column `dataValueType` set to `12` in the `crt.DataGrid` `columns` array.
- [ ] `crt.DataGrid` has `features.editable.enable: true` if toggleable checkboxes are needed.
- [ ] Column has a unique `id` (UUID) and a valid `code` matching the datasource attribute.
- [ ] No direct `cellView: { type: "crt.TableBooleanCell", ... }` in the column — the platform handles this.
