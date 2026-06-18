# How to Use a Data Table Edit Email Cell (`crt.DataTableEditEmailCell`) in a Freedom UI Page

> Audience: code agent configuring email column types in a `crt.DataGrid` schema.
> `crt.DataTableEditEmailCell` is a **DataGrid cell renderer** for editable email-address columns; it is not a standalone insertable element — it is referenced as the `columnType` of a `crt.DataGrid` column definition.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.DataGrid` column definition (`columnType` field only)
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A column entry inside a `crt.DataGrid` `columns` array, with `columnType: "crt.DataTableEditEmailCell"`. **Always present.** |

`crt.DataTableEditEmailCell` has `formControlConfig: { relatesTo: 'control' }` — it does **not** appear as its own `insert` op. The DataGrid framework instantiates it automatically for the matching column.

### 1.1 Naming convention

Column names follow the DataGrid convention, typically the data field name.

---

## 2. Step-by-step recipe

### 2.1 Add an email column to a `crt.DataGrid` (`viewConfigDiff` entry)

```jsonc
{
  "operation": "merge",
  "name": "MyDataGrid",
  "values": {
    "columns": [
      {
        "id": "Email",
        "code": "Email",
        "path": "Email",
        "caption": "#ResourceString(MyGrid_Email)#",
        "columnType": "crt.DataTableEditEmailCell",
        "dataValueType": 1,
        "width": 220,
        "sortable": true,
        "editable": true,
        "values": {
          "isFormatValidated": true
        }
      }
    ]
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.DataTableEditEmailCell` are in `ComponentRegistry.json` under
`componentType: "crt.DataTableEditEmailCell"`. This guide covers only the assembly mechanics.

---

## 7. Common pitfalls

1. **Trying to insert as a standalone element** — `crt.DataTableEditEmailCell` is a cell renderer, not a standalone component; always reference it as `columnType` inside a DataGrid column, never as a top-level `insert` op.
2. **Setting `isFormatValidated: false` on required fields** — disabling format validation allows any text to be saved; enable it for email-type entity columns.
3. **Setting `readonly` on the cell level instead of the column `editable` flag** — use the DataGrid column `editable: false` for static read-only columns; the `readonly` cell property is for programmatic per-row overrides.
4. **Forgetting `dataValueType`** — `dataValueType: 1` (ShortText) is the typical mapping for email columns in Creatio entity schemas.

---

## 8. Quick checklist

- [ ] Column referenced inside a `crt.DataGrid` columns array with `columnType: "crt.DataTableEditEmailCell"`.
- [ ] `isFormatValidated` set to `true` for standard email columns that require RFC 5322 format checking.
- [ ] `editable` set on the column definition to control cell editability.
- [ ] `dataValueType` matches the entity schema column definition (typically `1` for ShortText).
