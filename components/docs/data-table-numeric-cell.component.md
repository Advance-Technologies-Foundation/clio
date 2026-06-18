# How to Add a Numeric Table Cell (`crt.TableNumericCell`) to a Freedom UI Page

> Audience: code agent inserting `crt.TableNumericCell` into a Creatio Freedom UI page schema.
> A `crt.TableNumericCell` is a read-only display cell used inside a `crt.DataGrid` column to render numeric values. It is not a standalone view element; it is wired as the `cellView` property of a column definition.

## Metadata
- **Category**: display
- **Container**: no
- **Parent types**: `crt.DataGrid` column definition (as `cellView` value)
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit
| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A `merge` op on the existing `crt.DataGrid` view element, updating its `columns` array to include a column with `"cellView": { "type": "crt.TableNumericCell", "value": "..." }`. |

`crt.TableNumericCell` is **not** inserted as an independent element with `parentName`/`propertyName: "items"`. It lives inside the `cellView` slot of a column in `crt.DataGrid`.

### 1.1 Naming convention
```
// The column's cellView object does not have a `name` field.
// Reference the column's data via the bound attribute path in `value`.
```

---

## 2. Step-by-step recipe

### 2.1 Wire `crt.TableNumericCell` as `cellView` inside a `crt.DataGrid` column

Add or edit a column definition in the `crt.DataGrid`'s `columns` array:

```jsonc
{
  "id": "a1b2c3d4-0000-0000-0000-000000000001",
  "code": "MyDetail_Amount",
  "caption": "#ResourceString(MyDetail_Amount)#",
  "dataValueType": 7,
  "cellView": {
    "type": "crt.TableNumericCell",
    "value": "$MyDetail.MyDetail_Amount"
  },
  "editingCellView": {
    "type": "crt.DataTableEditNumericCell",
    "control": "$MyDetail.MyDetail_Amount"
  }
}
```

The `value` binding uses the collection attribute path (`$<collectionAttribute>.<columnCode>`). Pair `cellView` with `editingCellView: { type: "crt.DataTableEditNumericCell" }` when inline editing is needed.

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.TableNumericCell` are in `ComponentRegistry.json` under `componentType: "crt.TableNumericCell"`. This guide covers
only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

Cell `inputs` available at runtime (from the registry):
```ts
interface TableNumericCellInputs {
  column: BaseColumnDefinition;  // injected by crt.DataGrid
  record: DataItem;              // injected by crt.DataGrid
  value: TValue;                 // bound via "value": "$..." in cellView
}
```
These inputs are injected automatically by `crt.DataGrid`; you do not set `column` or `record` manually.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — insert or merge the DataGrid that hosts this cell
{
  "operation": "insert",
  "name": "MyGrid",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.DataGrid",
    "items": "$MyDetail",
    "primaryColumnName": "MyDetail_Id",
    "columns": [
      {
        "id": "a1b2c3d4-0000-0000-0000-000000000001",
        "code": "MyDetail_Amount",
        "caption": "#ResourceString(MyDetail_Amount)#",
        "dataValueType": 7,
        "cellView": {
          "type": "crt.TableNumericCell",
          "value": "$MyDetail.MyDetail_Amount"
        }
      }
    ],
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 12, "rowSpan": 6 }
  }
}
```

---

## 7. Common pitfalls

- **Trying to `insert` as a standalone view element.** `crt.TableNumericCell` is only valid inside `cellView`/`editingCellView` of a `crt.DataGrid` column. It has no `parentName`/`propertyName` path of its own.
- **Using the wrong `value` binding path.** The path must reference the grid's collection attribute: `"$<collectionAttribute>.<columnCode>"`. A bare `"$column"` is never valid.
- **Omitting `editingCellView` when the grid has inline editing.** Without it the cell is read-only even when the grid `features.editable.enable` is `true`.
- **Setting `column` or `record` explicitly.** These inputs are injected by `crt.DataGrid`; manually specifying them overwrites the grid's injection and breaks data binding.
- **Wrong `dataValueType`.** For numeric columns use `Terrasoft.DataValueType.INTEGER` (4) or `FLOAT` variants (7, 8, etc.) to ensure proper server-side filtering.

---

## 8. Quick checklist

- [ ] `crt.TableNumericCell` is placed in `cellView` of a `crt.DataGrid` column, not as a top-level view element.
- [ ] `value` binding is `"$<collectionAttr>.<columnCode>"`.
- [ ] `id` on the column definition is a unique GUID.
- [ ] `dataValueType` is a numeric type constant.
- [ ] `editingCellView` provided if the grid has inline editing enabled.
