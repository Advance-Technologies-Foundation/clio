# How to Add a DataTable Edit Web Cell (`crt.DataTableEditWebCell`) to a DataTable Column

> Audience: code agent configuring an editable web-URL column in a `crt.DataTable` in a Creatio Freedom UI
> page schema.
>
> `crt.DataTableEditWebCell` is the inline URL editor shown when a user activates a web-type cell in an
> editable `crt.DataTable` column. It validates the value as a URL and opens the link in a new tab. It is
> **not** inserted via `viewConfigDiff` — it is set as the `editingCellView` property on the column definition.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: column `editingCellView` slot inside `crt.DataTable`
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` — `crt.DataTable` column's `editingCellView` | An object `{ "type": "crt.DataTableEditWebCell", "control": "$Attr" }`. **Always present.** |

There is **no** standalone `insert` op for this cell. No `modelConfigDiff` or `viewModelConfigDiff` changes are
needed for the cell itself.

### 1.1 Naming convention

```
// no standalone name — inline config
// bound attribute follows DataTable column binding convention, e.g.:
$DetailDS_Website
```

---

## 2. Step-by-step recipe

### 2.1 Add `editingCellView` to the DataTable column

```jsonc
{
  "operation": "insert",
  "name": "MyDataTable",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.DataTable",
    "items": "$MyDetailDS",
    "columns": [
      {
        "id": "col-abc123",
        "code": "Website",
        "caption": "#ResourceString(MyDataTable_column_Website_caption)#",
        "dataValueType": 1,
        "cellView": {
          "type": "crt.TableTextCell",
          "value": "$MyDetailDS_Website"
        },
        "editingCellView": {
          "type": "crt.DataTableEditWebCell",
          "control": "$MyDetailDS_Website"
        }
      }
    ]
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.DataTableEditWebCell` are in `ComponentRegistry.json` under `componentType: "crt.DataTableEditWebCell"`.
This guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

No additional custom types. All inputs are primitives or `FormControl`.

---

## 5. Copy-paste minimal example

No direct PackageStore schema match found. Based on the standard `editingCellView` column convention:

```jsonc
// Web URL column
{
  "id": "col-website",
  "code": "Website",
  "caption": "Website",
  "dataValueType": 1,
  "cellView": {
    "type": "crt.TableTextCell",
    "value": "$AccountDS_Website"
  },
  "editingCellView": {
    "type": "crt.DataTableEditWebCell",
    "control": "$AccountDS_Website"
  }
}
```

---

## 6. Driving from page state

The `readonly` input is `propertyBindable`:

```jsonc
"editingCellView": {
  "type": "crt.DataTableEditWebCell",
  "control": "$DetailDS_Website",
  "readonly": "$MyTable_readonly"
}
```

---

## 7. Common pitfalls

1. **Using an `insert` op instead of `editingCellView`** — this cell is not a standalone view element.
2. **Binding `control` to a raw string** — `control` must receive a `FormControl`; bind via `$AttributeName`.
3. **Using this cell for non-URL text** — `crt.DataTableEditWebCell` applies URL validation; use
   `crt.DataTableEditTextCell` for plain text columns.
4. **Forgetting `cellView`** — `cellView` and `editingCellView` are independent; without `cellView` the
   display-mode cell renders blank.

---

## 8. Quick checklist

- [ ] `editingCellView` is set inside the DataTable column, not as a standalone `insert` op.
- [ ] `control` is bound to a `$AttributeName` referencing a `FormControl`.
- [ ] `cellView` is also set on the same column for the read-only display state.
- [ ] `readonly` is bound or set to a literal value as needed.
- [ ] Column `dataValueType` matches a web/text type (typically `1` for `Text`).
