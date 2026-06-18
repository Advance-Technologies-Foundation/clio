# How to Use a Date/Time Cell (`crt.TableDateTimeCell`) in a DataGrid Column

> Audience: code agent configuring a `crt.DataGrid` column to render date/time values.
>
> `crt.TableDateTimeCell` renders a date, time, or datetime value formatted according to the column's
> `dataValueType`; it uses the `DateTimeFormatPipe` to apply locale-aware formatting. It is
> **not inserted directly** — the platform creates it automatically via `DateTimeCellViewElementConfigCreator`
> for columns with date/datetime data value types.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: embedded inside a `crt.DataGrid` column `cellView` — not a standalone view element
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` (DataGrid `columns`) | A column with `dataValueType: 7` (DateTime), `8` (Date), or `9` (Time). The platform wires `crt.TableDateTimeCell` automatically. |

The cell is **platform-managed**: you declare the DataGrid column with the appropriate
`dataValueType`; the `DateTimeCellViewElementConfigCreator` service constructs the `cellView` binding
`value` to the column's datasource attribute.

---

## 2. Step-by-step recipe

### 2.1 Add a date/time column to `crt.DataGrid`

```jsonc
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
        "code": "GridDS_CreatedOn",
        "path": "CreatedOn",
        "caption": "#ResourceString(GridDS_CreatedOn)#",
        "dataValueType": 7
      }
    ]
  }
}
```

`dataValueType` values:
- `7` — DateTime
- `8` — Date
- `9` — Time

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.TableDateTimeCell` are in `ComponentRegistry.json` under `componentType: "crt.TableDateTimeCell"`.
This guide covers only the assembly mechanics.

---

## 5. Copy-paste minimal example

```jsonc
// Column definition inside crt.DataGrid.columns (Date column)
{
  "id": "8da98b54-1433-0020-cd5b-1bf019a13f42",
  "code": "GridDS_CreatedOn",
  "path": "CreatedOn",
  "caption": "#ResourceString(GridDS_CreatedOn)#",
  "dataValueType": 7
}
```

The platform binds `value: "$GridItems.GridDS_CreatedOn"` and formats it using `DateTimeFormatPipe`
based on `dataValueType`.

---

## 7. Common pitfalls

1. **Inserting `crt.TableDateTimeCell` directly.** This cell is constructed programmatically; direct `insert` ops will not work.
2. **Wrong `dataValueType`.** Use `7` for DateTime, `8` for Date, `9` for Time; an incorrect type selects a different cell renderer.
3. **Expecting formatted output with a plain string `value`.** The format is derived from `column.dataValueType` passed into `DateTimeFormatPipe`; if `column` is not set, the formatter cannot apply locale rules.

---

## 8. Quick checklist

- [ ] Column `dataValueType` set to `7`, `8`, or `9` in the `crt.DataGrid` `columns` array.
- [ ] Column has a unique `id` (UUID) and a valid `code` matching the datasource attribute.
- [ ] No direct `cellView: { type: "crt.TableDateTimeCell", ... }` — the platform handles this automatically.
