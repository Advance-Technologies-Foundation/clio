# How to Add a File List (`crt.FileList`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.FileList` into a mobile page schema.
> Renders a file attachment list on a mobile record page, allowing users to view and manage attached files.

## Metadata
- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.TabPanel` tab body, `crt.GridContainer`, any layout container
- **Typical children**: none

---
## 1. Mental model
`crt.FileList` displays files linked to the current record. Bind `items` to the PDS collection
attribute for the file entity. Set `entityName` to the file entity schema name (e.g.
`"ActivityFile"`), `recordColumnName` to the foreign-key column pointing to the parent record,
and `masterRecordColumnValue` to the binding for the parent record ID (typically `"$Id"`).

---
## 2. Clio operation
```jsonc
{
  "operation": "insert",
  "name": "AttachmentList",
  "values": {
    "type": "crt.FileList",
    "items": "$PDS_Files",
    "entityName": "ActivityFile",
    "recordColumnName": "ActivityId",
    "masterRecordColumnValue": "$Id"
  },
  "parentName": "AttachmentsTab",
  "propertyName": "items",
  "index": 0
}
```

---
## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.FileList` are in
`ComponentRegistry.json` under `componentType: "crt.FileList"`.

Key inputs:
| Property | Type | Description |
|---|---|---|
| `items` | string (binding) | Data attribute binding for the file collection (e.g. `$PDS_Files`). |
| `entityName` | string | File entity schema name (e.g. `"ActivityFile"`, `"ContactFile"`). |
| `primaryColumnName` | string | Primary column name of the file entity (defaults to `"Id"`). |
| `recordColumnName` | string | Foreign-key column on the file entity pointing to the parent record. |
| `masterRecordColumnValue` | string (binding) | Binding for the parent record ID value (e.g. `"$Id"`). |

Additional runtime properties:

| Property | Type | Description |
|---|---|---|
| `scrollable` | boolean | Enable vertical scrolling on the file list when it overflows. Default: `true`. |

---
## 4. Copy-paste minimal example
```jsonc
{
  "operation": "insert",
  "name": "AttachmentList",
  "values": {
    "type": "crt.FileList",
    "items": "$PDS_Files",
    "entityName": "ActivityFile",
    "recordColumnName": "ActivityId",
    "masterRecordColumnValue": "$Id"
  },
  "parentName": "AttachmentsTab",
  "propertyName": "items",
  "index": 0
}
```

---
## 5. Common pitfalls
- **`entityName` must match an existing file entity schema.** An incorrect name prevents the
  file list from loading and shows no error in design-time.
- **`recordColumnName` must be the column on the file entity**, not on the parent entity.
  Swapping the two columns is a common source of empty file lists at runtime.
- **`items` must be a binding expression** (`$PDS_*`). Without a valid binding the component
  renders its attachment placeholder icon indefinitely.
