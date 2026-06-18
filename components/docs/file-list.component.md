# How to Add a File List (`crt.FileList`) to a Freedom UI Page

> Audience: code agent inserting a `crt.FileList` into a Creatio Freedom UI page schema.
>
> `crt.FileList` renders attached files for a parent record. It extends `crt.DataGrid` —
> the same diff-shape, but with file-list-specific properties (`viewType`, `fileGroups`,
> `droppable`, `masterRecordColumnValue`). Bind it to a `File` (or similar attachment)
> entity scoped by a master record column.

For the full DataGrid contract (datasource, collection attribute, column descriptors),
see crt.DataGrid guide.
This document highlights only the file-list-specific differences.

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`, `crt.TabContainer`
- **Typical children**: none (file rows are bound via `items: "$<collection>"`)

---

## 1. Mental model — the 3 places you must edit (same as DataGrid)

| # | Section | What you add |
|---|---|---|
| 1 | `modelConfigDiff` | A `crt.EntityDataSource` over a file entity (e.g. `ContactFile`, `AccountFile`), filtered by the master record. |
| 2 | `viewModelConfigDiff` | A collection attribute (`isCollection: true`) bound to the file datasource. |
| 3 | `viewConfigDiff` | An `insert` op with `type: "crt.FileList"`, `items: "$<collection>"`, `masterRecordColumnValue`, etc. |

### 1.1 Naming convention

```
FileList_<id>            // view element name
FileList_<id>DS          // datasource over the file entity
$FileList_<id>           // items binding ($-prefix)
FileList_<id>DS_Id       // primary column
```

---

## 2. Step-by-step recipe

### 2.1 Add the file datasource (`modelConfigDiff` entry)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "dataSources": {
      "FileList_xkp4rDS": {
        "type": "crt.EntityDataSource",
        "scope": "viewElement",
        "config": {
          "entitySchemaName": "ContactFile",
          "attributes": {
            "Id":    { "path": "Id" },
            "Name":  { "path": "Name" },
            "Size":  { "path": "Size" },
            "Type":  { "path": "Type" },
            "CreatedOn": { "path": "CreatedOn" }
          }
        }
      }
    }
  }
}
```

### 2.2 Declare the collection attribute (`viewModelConfigDiff` entry)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "FileList_xkp4r": {
        "isCollection": true,
        "modelConfig": { "path": "FileList_xkp4rDS" },
        "viewModelConfig": {
          "attributes": {
            "FileList_xkp4rDS_Id":   { "modelConfig": { "path": "FileList_xkp4rDS.Id"   } },
            "FileList_xkp4rDS_Name": { "modelConfig": { "path": "FileList_xkp4rDS.Name" } },
            "FileList_xkp4rDS_Size": { "modelConfig": { "path": "FileList_xkp4rDS.Size" } },
            "FileList_xkp4rDS_Type": { "modelConfig": { "path": "FileList_xkp4rDS.Type" } },
            "FileList_xkp4rDS_CreatedOn": { "modelConfig": { "path": "FileList_xkp4rDS.CreatedOn" } }
          }
        }
      }
    }
  }
}
```

### 2.3 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "FileList_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.FileList",
    "viewType": "list",
    "items": "$FileList_xkp4r",
    "primaryColumnName": "FileList_xkp4rDS_Id",
    "masterRecordColumnValue": "$Id",
    "recordColumnName": "Contact",
    "droppable": true,
    "tag": "ContactFiles",
    "uploadClicked": { "request": "crt.UploadFileRequest" },
    "fileDropped":   { "request": "crt.UploadFileRequest" },
    "columns": [
      {
        "id": "11111111-1111-1111-1111-111111111111",
        "code": "FileList_xkp4rDS_Name",
        "caption": "#ResourceString(FileList_xkp4rDS_Name)#",
        "dataValueType": 28
      },
      {
        "id": "22222222-2222-2222-2222-222222222222",
        "code": "FileList_xkp4rDS_Size",
        "caption": "#ResourceString(FileList_xkp4rDS_Size)#",
        "dataValueType": 4
      }
    ],
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 4 }
  }
}
```

### 2.4 Add handlers for `uploadClicked` and `fileDropped`

```jsonc
{
  "request": "crt.UploadFileRequest",
  "handler": async (request, next) => {
    // open file picker, upload, attach to ContactFile rows
    return next?.handle(request);
  }
}
```

### 2.5 Optional: hierarchical (tree) view

Show attachments as a tree (each file nested under its **parent file**) instead of a flat list.
Schema-driven, **off by default**, `list` view only. Requires a **self-referencing lookup** on the
file entity (e.g. `UsrParentSysFile` on `SysFile`).

1. `modelConfigDiff` — add `hierarchyConfig` **inside the datasource `config`** (placing it as a
   sibling of `config` silently disables the tree):
   ```jsonc
   "loadParameters": {
     "options": {
       "hierarchyConfig": { "type": "ClientSide", "hierarchicalColumnName": "UsrParentSysFile" }
     }
   }
   ```
2. `viewConfigDiff` — enable the feature: `"features": { "hierarchical": { "enable": true } }`.

The `SetupDataGridHierarchicalHandler` derives the rest (`hierarchicalAttributeName`,
has-children/children-count, toggle). Files with an empty parent render at the root; upload, icons,
and `fileGroups` keep working.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.FileList` are in `ComponentRegistry.json` under `componentType: "crt.FileList"`. This guide
covers only the assembly mechanics — what to put where in `viewConfigDiff` /
`viewModelConfigDiff` / `modelConfigDiff`.

---

## 4. Copy-paste minimal example

See § 2 above — the example is already minimal.

---

## 5. Common pitfalls

(In addition to the generic `crt.DataGrid` pitfalls — see § 7 of crt.DataGrid guide.)

1. **Missing `masterRecordColumnValue`** — the drop-zone placeholder hides (`showDropZonePlaceholder` returns `false`), and uploaded files cannot be attached because there is no parent id. Always set `masterRecordColumnValue: "$Id"` on edit pages.
2. **Wrong `recordColumnName`** — the file entity must have a lookup column back to the master entity (e.g. `Contact` on `ContactFile`). Passing a non-existent column breaks the upload handler.
3. **Setting `features.itemsCreation: true`** — overridden by the component (`itemsCreation: false`), users always rely on `uploadClicked`. Don't fight the override.
4. **`droppable: true` without a `fileDropped` handler** — the drop visualization works but the dropped files vanish on release.
5. **Mixing `viewType: "gallery"` with row-level `columns`** — gallery view ignores most column metadata except the icon mapping. Switch to `viewType: "list"` for column-rich displays.
6. **Same `tag` on multiple FileLists** — used internally for grouping; reusing the tag may cause cross-talk on bulk operations. Use unique tags per page.

---

## 6. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.FileList"`, unique `name`, `propertyName: "items"`.
- [ ] `items: "$<attr>"` references a collection bound to a file-entity datasource (see DataGrid for the diff shape).
- [ ] `masterRecordColumnValue: "$Id"` set, `recordColumnName` set to the master-record FK column on the file entity.
- [ ] `viewType` chosen (`"list"` for grid, `"gallery"` for thumbnails).
- [ ] `uploadClicked.request` wired to a handler that opens the file picker and persists files.
- [ ] If `droppable: true`, also wire `fileDropped.request`.
- [ ] `columns` defined when `viewType: "list"`.
- [ ] `layoutConfig` provided with generous `rowSpan` (file lists need vertical space).
