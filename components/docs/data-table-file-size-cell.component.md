# How to Use a File Size Cell (`crt.TableFileSizeCell`) in a DataGrid Column

> Audience: code agent configuring a `crt.DataGrid` column to render file size values in human-readable form.
>
> `crt.TableFileSizeCell` renders a numeric byte count as a formatted string (e.g. "1.2 MB") using the
> `MemoryFormatPipe`; it has no additional configuration beyond the base cell properties. It is embedded in
> a DataGrid column's `cellView` via `FileSizeCellViewElementConfigCreator`.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: embedded inside a `crt.DataGrid` column `cellView` — not a standalone view element
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` (DataGrid `columns`) | A column with `cellView: { type: "crt.TableFileSizeCell", ... }`. |

The platform's `FileSizeCellViewElementConfigCreator` constructs the `cellView` binding `value` and
`column` automatically.

---

## 2. Step-by-step recipe

### 2.1 Add a file size column to `crt.DataGrid`

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
        "code": "GridDS_FileSize",
        "path": "FileSize",
        "caption": "#ResourceString(GridDS_FileSize)#",
        "dataValueType": 4,
        "cellView": {
          "type": "crt.TableFileSizeCell",
          "value": "$GridItems.GridDS_FileSize"
        }
      }
    ]
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.TableFileSizeCell` are in `ComponentRegistry.json` under `componentType: "crt.TableFileSizeCell"`.
This guide covers only the assembly mechanics.

---

## 5. Copy-paste minimal example

```jsonc
// cellView inside a DataGrid column definition
{
  "type": "crt.TableFileSizeCell",
  "value": "$GridItems.GridDS_FileSize",
  "column": { "code": "GridDS_FileSize", "dataValueType": 4 }
}
```

The `MemoryFormatPipe` formats the numeric byte value into a human-readable string (KB, MB, GB).

---

## 7. Common pitfalls

1. **`value` not a numeric byte count.** The `MemoryFormatPipe` expects a raw number in bytes; passing a pre-formatted string produces garbled output.
2. **Using for non-file-size columns.** This cell is specific to file-size byte counts; for general integers use `crt.TableTextCell` or `crt.TableNumericCell` instead.

---

## 8. Quick checklist

- [ ] Column `cellView.type` set to `"crt.TableFileSizeCell"`.
- [ ] `value` bound to a numeric (integer/float) attribute holding bytes.
- [ ] Column `dataValueType` is an integer or float type.
