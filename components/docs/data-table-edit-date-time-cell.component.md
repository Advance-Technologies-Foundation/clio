# How to Use a Data Table Edit Date-Time Cell (`crt.DataTableEditDateTimeCell`) in a Freedom UI Page

> Audience: code agent configuring date/time column types in a `crt.DataGrid` schema.
> `crt.DataTableEditDateTimeCell` is a **DataGrid cell renderer** for editable date, time, or date-time columns; it is not a standalone insertable element — it is referenced as the `columnType` of a `crt.DataGrid` column definition.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.DataGrid` column definition (`columnType` field only)
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A column entry inside a `crt.DataGrid` `columns` array, with `columnType: "crt.DataTableEditDateTimeCell"`. **Always present.** |

`crt.DataTableEditDateTimeCell` has `formControlConfig: { relatesTo: 'control' }` — it does **not** appear as its own `insert` op. The DataGrid framework instantiates it automatically for the matching column.

### 1.1 Naming convention

Column names follow the DataGrid convention, typically the data field name.

---

## 2. Step-by-step recipe

### 2.1 Add a date/time column to a `crt.DataGrid` (`viewConfigDiff` entry)

```jsonc
{
  "operation": "merge",
  "name": "MyDataGrid",
  "values": {
    "columns": [
      {
        "id": "StartDate",
        "code": "StartDate",
        "path": "StartDate",
        "caption": "#ResourceString(MyGrid_StartDate)#",
        "columnType": "crt.DataTableEditDateTimeCell",
        "dataValueType": 7,
        "width": 200,
        "sortable": true,
        "editable": true,
        "values": {
          "dateType": "DateTime",
          "useSeconds": false
        }
      }
    ]
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.DataTableEditDateTimeCell` are in `ComponentRegistry.json` under
`componentType: "crt.DataTableEditDateTimeCell"`. This guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// dateType — maps to DateTypes enum
type DateTypes = 'Date' | 'Time' | 'DateTime';

// control — injected automatically by DataGrid; do not set manually in the column values
```

---

## 7. Common pitfalls

1. **Trying to insert as a standalone element** — `crt.DataTableEditDateTimeCell` is a cell renderer, not a standalone component; always reference it as `columnType` inside a DataGrid column, never as a top-level `insert` op.
2. **Setting `dateType` incorrectly** — use `"Date"` for date-only, `"Time"` for time-only, `"DateTime"` for full date+time; mismatches cause incorrect pickers to open.
3. **Enabling `useSeconds` without a time component** — `useSeconds: true` only has effect when `dateType` is `"Time"` or `"DateTime"`; it is silently ignored for `"Date"`.
4. **Setting `readonly` on the column level vs. cell level** — prefer the DataGrid column `editable: false` to make the column read-only; the `readonly` cell property is reserved for programmatic per-row overrides.
5. **Forgetting `dataValueType`** — always align `dataValueType` in the column definition with the entity schema column type (`7` = Date/Time in Creatio).

---

## 8. Quick checklist

- [ ] Column referenced inside a `crt.DataGrid` columns array with `columnType: "crt.DataTableEditDateTimeCell"`.
- [ ] `dateType` set to `"Date"`, `"Time"`, or `"DateTime"` to match the data column type.
- [ ] `useSeconds` set to `true` only for time-bearing columns that need seconds precision.
- [ ] `editable` set on the column definition to control cell editability.
- [ ] `dataValueType` matches the entity schema column definition.
