# How to Use a File Cell (`crt.TableFileCell`) in a DataGrid Column

> Audience: code agent configuring a `crt.DataGrid` column to render file attachment values.
>
> `crt.TableFileCell` renders a file name as a downloadable link with an appropriate file-type icon and
> an optional upload-progress bar; clicking the link opens/downloads the file. It is embedded in a
> DataGrid column's `cellView` via `FileCellViewElementConfigCreator`.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: embedded inside a `crt.DataGrid` column `cellView` — not a standalone view element
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` (DataGrid `columns`) | A column with a `cellView: { type: "crt.TableFileCell", ... }`. |

The platform's `FileCellViewElementConfigCreator` constructs the `cellView` binding `fileName` to the
display attribute and computing `fileUrl` using the `crt.ToFileLink` pipe.

---

## 2. Step-by-step recipe

### 2.1 Add a file column to `crt.DataGrid`

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
        "code": "GridDS_FileName",
        "path": "FileName",
        "caption": "#ResourceString(GridDS_FileName)#",
        "dataValueType": 29,
        "cellView": {
          "type": "crt.TableFileCell",
          "fileName": "$GridItems.GridDS_FileName",
          "fileUrl": "$GridItems.GridDS_Id | crt.ToFileLink : 'FileSchema'"
        }
      }
    ]
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.TableFileCell` are in `ComponentRegistry.json` under `componentType: "crt.TableFileCell"`. This
guide covers only the assembly mechanics.

---

## 5. Copy-paste minimal example

```jsonc
// cellView inside a DataGrid column definition
{
  "type": "crt.TableFileCell",
  "fileName": "$GridItems.GridDS_FileName",
  "fileUrl": "$GridItems.GridDS_Id | crt.ToFileLink : 'FileList'"
}
```

The `crt.ToFileLink` pipe converts a file record ID into a download URL using the provided schema name.

---

## 7. Common pitfalls

1. **`fileUrl` not computed with `crt.ToFileLink`.** A plain URL string may work statically but breaks for dynamically loaded records; always use the `crt.ToFileLink` pipe with the correct schema name.
2. **`progress` set to a non-zero value when there is no upload in flight.** A non-zero `progress` that never reaches `100` shows a stuck loading indicator; bind `progress` only when tracking an actual upload.
3. **`fileName` empty.** Without a `fileName`, the file icon falls back to a generic icon and the link text is blank.

---

## 8. Quick checklist

- [ ] Column `cellView.type` set to `"crt.TableFileCell"`.
- [ ] `fileName` bound to the file name attribute.
- [ ] `fileUrl` computed using `crt.ToFileLink` pipe with the correct data schema name.
- [ ] `progress` omitted or bound to an actual upload-progress attribute (defaults to `undefined` — no progress bar shown).
