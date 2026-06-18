# How to Add a Text Table Cell (`crt.TableTextCell`) to a Freedom UI Page

> Audience: code agent inserting `crt.TableTextCell` into a Creatio Freedom UI page schema.
> A `crt.TableTextCell` is a read-only display cell used inside a `crt.DataGrid` column to render text values. It is the default cell type for text and lookup columns. It is wired as the `cellView` property of a column definition, not inserted as a standalone view element.

## Metadata
- **Category**: display
- **Container**: no
- **Parent types**: `crt.DataGrid` column definition (as `cellView` value)
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit
| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A column in a `crt.DataGrid` with `"cellView": { "type": "crt.TableTextCell", "value": "..." }`. |

`crt.TableTextCell` is **not** inserted as an independent element with `parentName`/`propertyName: "items"`. It lives inside the `cellView` slot of a column definition in `crt.DataGrid`. It is used for plain text columns as well as lookup display values (with a pipe applied to the bound attribute).

### 1.1 Naming convention
```
// cellView object has no `name` field.
// Bind the text attribute path via "value".
```

---

## 2. Step-by-step recipe

### 2.1 Wire `crt.TableTextCell` as `cellView` inside a `crt.DataGrid` column

```jsonc
{
  "id": "e5f6a7b8-0000-0000-0000-000000000001",
  "code": "MyDetail_Name",
  "caption": "#ResourceString(MyDetail_Name)#",
  "dataValueType": 28,
  "cellView": {
    "type": "crt.TableTextCell",
    "value": "$MyDetail.MyDetail_Name"
  },
  "editingCellView": {
    "type": "crt.DataTableEditTextCell",
    "control": "$MyDetail.MyDetail_Name"
  }
}
```

For lookup columns, apply a display-value pipe on the `value` binding:
```jsonc
"cellView": {
  "type": "crt.TableTextCell",
  "value": "$MyDetail.MyDetail_Status | crt.ToDataValueTypeDisplayValue"
}
```

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.TableTextCell` are in `ComponentRegistry.json` under `componentType: "crt.TableTextCell"`. This guide covers
only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

Cell `inputs` available at runtime (from the registry):
```ts
interface TableTextCellInputs {
  column: BaseColumnDefinition;  // injected by crt.DataGrid
  record: DataItem;              // injected by crt.DataGrid
  value: TValue;                 // bound via "value": "$..." in cellView
}
```
`column` and `record` are injected by `crt.DataGrid`; do not set them manually.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — DataGrid with text column
{
  "operation": "insert",
  "name": "SkillsGrid",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.DataGrid",
    "items": "$InputParametersDetail",
    "primaryColumnName": "InputParametersDS_Id",
    "_designOptions": {
      "columns": {
        "cellViews": {
          "InputParametersDS_DataValueTypeUId": {
            "type": "crt.TableTextCell",
            "value": "$InputParametersDetail.InputParametersDS_DataValueTypeUId | crt.ToDataValueTypeDisplayValue"
          }
        }
      }
    },
    "columns": [
      {
        "id": "e5f6a7b8-0000-0000-0000-000000000001",
        "code": "InputParametersDS_Code",
        "caption": "#ResourceString(InputParametersDS_Code)#",
        "dataValueType": 28
      }
    ],
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 12, "rowSpan": 6 }
  }
}
```

---

## 7. Common pitfalls

- **Trying to `insert` as a standalone view element.** `crt.TableTextCell` is only valid inside a `crt.DataGrid` column's `cellView`.
- **Forgetting the display-value pipe for lookup columns.** Without `| crt.ToDataValueTypeDisplayValue` (or equivalent), a lookup column renders the raw UUID instead of the human-readable label.
- **Wrong `dataValueType`.** Text columns use `28` (TEXT). Mismatching causes incorrect filter UI in the grid.
- **Missing `editingCellView` when inline editing is enabled.** If `features.editable.enable` is `true` on the grid, each column needs its own `editingCellView`; without it the cell is not editable.
- **Relying on `icon` for MMP customization.** The leaf component exposes `icon` as an Angular `@Input` but it is **not** a `@CrtInput`; it does not appear in the registry and cannot be driven from the schema.

---

## 8. Quick checklist

- [ ] `crt.TableTextCell` placed in `cellView` of a `crt.DataGrid` column, not as a top-level view element.
- [ ] `value` binding is `"$<collectionAttr>.<columnCode>"`.
- [ ] Lookup columns include an appropriate display-value pipe on `value`.
- [ ] `id` on the column definition is a unique GUID.
- [ ] `editingCellView` added if inline editing is enabled on the parent grid.
