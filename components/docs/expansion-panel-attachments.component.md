# How to Add Attachments (`crt.ExpansionPanel` preset) to a Freedom UI Page

> Audience: code agent assembling the **"Attachments"** designer element into a Creatio Freedom UI page schema.
>
> "Attachments" (caption key `Components.Attachment.Caption`) is **not a standalone component** — it is a preset of
> `crt.ExpansionPanel`: a collapsible panel pre-filled with a `crt.FileList` in its body and an upload / refresh /
> search toolbar. Dropping it in the designer runs `crt.AddAttachmentsCommand`, which inserts the panel + file list
> and then **connects the toolbar actions to the file list**. Reproducing it by hand means replicating BOTH the
> nested structure AND that connection.
>
> Cross-references (do not re-document them here):
> - Base panel mechanics: [`expansion-panel.component.md`](./expansion-panel.component.md).
> - Full `crt.FileList` property reference + its data source: its own guide
>   (`componentType: "crt.FileList"` in `ComponentRegistry.json`, `file-list.component.md`).

## Metadata

- **Designer element**: `Components.Attachment.Caption` — toolbar group `Components`, position `70`.
- **Create command**: `crt.AddAttachmentsCommand` (extends `crt.AddViewItemCommand`).
- **Wraps**: `crt.ExpansionPanel` → body (`items`) holds a `crt.FileList`; header (`tools`) holds upload / refresh / search.
- **Requires**: no extra package/feature.

---

## 1. What the designer drop produces

```
crt.ExpansionPanel (title, expanded:true, fullWidthHeader:false)
├─ items → crt.GridContainer (2 columns)
│           └─ crt.FileList   name:"FileList_<guid>"  masterRecordColumnValue:"$Id"  recordColumnName:"RecordId"  rowSpan:10
└─ tools → crt.GridContainer → crt.FlexContainer (row)
           ├─ crt.Button "upload"  icon:upload-button-icon  clicked: crt.UploadFileRequest
           ├─ crt.Button "refresh" icon:reload-icon         clicked: crt.LoadDataRequest { config:{ loadType:"reload" } }
           └─ crt.SearchFilter  (not icon-only)
```

`masterRecordColumnValue: "$Id"` binds the file list to the current record; `recordColumnName` is the lookup
column on the attachment entity that points back to the master (`RecordId` by default; real pages override it,
e.g. `IntentUId`).

## 2. Post-insert wiring — the part hand-authoring gets wrong

After insert, `AddAttachmentsCommand.connectActionsToFileList` finds the `crt.FileList` inside the new panel and
patches the three toolbar elements so they actually drive it:

| Toolbar element | Matched by | Field the command sets |
|---|---|---|
| Upload button | `clicked.request === "crt.UploadFileRequest"` | `clicked.params.viewElementName = <fileList.name>` |
| Refresh button | `clicked.request === "crt.LoadDataRequest"` | `clicked.params.dataSourceName = <fileList.name> + "DS"` |
| Search filter | `type === "crt.SearchFilter"` | `targetAttributes = [<fileList.name>]` (only if not already set) |

So every wiring value is **derived from the file list's `name`**. If you hand-author this, pick the file list
name first, then fill the three params/attributes from it.

## 3. Step-by-step recipe

```jsonc
[
  // panel — declares BOTH slot arrays empty
  { "operation": "insert", "name": "AttachmentsPanel", "parentName": "MainContainer", "propertyName": "items", "index": 0,
    "values": { "type": "crt.ExpansionPanel", "title": "#ResourceString(AttachmentsPanel_title)#", "expanded": true,
      "items": [], "tools": [],
      "layoutConfig": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 10 } } },

  // body: GridContainer in the items slot → FileList inside it
  { "operation": "insert", "name": "AttachmentsPanel_wrap", "parentName": "AttachmentsPanel", "propertyName": "items", "index": 0,
    "values": { "type": "crt.GridContainer", "columns": ["minmax(32px, 1fr)", "minmax(32px, 1fr)"], "items": [] } },
  { "operation": "insert", "name": "FileList_attachments", "parentName": "AttachmentsPanel_wrap", "propertyName": "items", "index": 0,
    "values": { "type": "crt.FileList", "masterRecordColumnValue": "$Id", "recordColumnName": "RecordId",
      "layoutConfig": { "column": 1, "row": 1, "colSpan": 2, "rowSpan": 10 } } },

  // tools: GridContainer in the tools slot → FlexContainer(row) → the controls (never directly in tools)
  { "operation": "insert", "name": "AttachmentsPanel_toolbar", "parentName": "AttachmentsPanel", "propertyName": "tools", "index": 0,
    "values": { "type": "crt.GridContainer", "columns": ["minmax(32px, 1fr)"], "items": [] } },
  { "operation": "insert", "name": "AttachmentsPanel_toolbarRow", "parentName": "AttachmentsPanel_toolbar", "propertyName": "items", "index": 0,
    "values": { "type": "crt.FlexContainer", "direction": "row", "alignItems": "center", "gap": "none", "items": [] } },
  { "operation": "insert", "name": "FileList_attachmentsUploadBtn", "parentName": "AttachmentsPanel_toolbarRow", "propertyName": "items", "index": 0,
    "values": { "type": "crt.Button", "icon": "upload-button-icon", "iconPosition": "only-icon", "color": "default",
      "clicked": { "request": "crt.UploadFileRequest", "params": { "viewElementName": "FileList_attachments" } } } },
  { "operation": "insert", "name": "FileList_attachmentsRefreshBtn", "parentName": "AttachmentsPanel_toolbarRow", "propertyName": "items", "index": 1,
    "values": { "type": "crt.Button", "icon": "reload-icon", "iconPosition": "only-icon", "color": "default",
      "clicked": { "request": "crt.LoadDataRequest", "params": { "config": { "loadType": "reload" }, "dataSourceName": "FileList_attachmentsDS" } } } },
  { "operation": "insert", "name": "FileList_attachmentsSearch", "parentName": "AttachmentsPanel_toolbarRow", "propertyName": "items", "index": 2,
    "values": { "type": "crt.SearchFilter", "targetAttributes": ["FileList_attachments"] } }
]
```

The `crt.FileList` also needs its own data source (`modelConfigDiff`) keyed `FileList_attachmentsDS` — see the
`crt.FileList` guide. The data-source key MUST be `<fileList.name> + "DS"` so the refresh button resolves it.

> **`crt.SearchFilter` is preprocessor-driven.** `targetAttributes` is the high-level wiring `AddAttachmentsCommand` sets; at save time the preprocessor expands it into a `_filterOptions.expose` block (with a `crt.SearchFilterAttributeConverter`). Saved schemas show that expanded form, but authoring `targetAttributes` is sufficient.

## 4. Common pitfalls

1. **Upload button not bound to the list.** Without `params.viewElementName = <fileList.name>` the upload action has no target and silently no-ops.
2. **Refresh `dataSourceName` mismatch.** It must be exactly `<fileList.name>DS`; any other value refreshes nothing.
3. **Search not scoped.** `targetAttributes` must contain the file list name, or the search box filters nothing.
4. **`recordColumnName` left at `RecordId` on a non-default entity.** Set it to the actual FK column on the attachment entity that references the master record.
5. **Slot composition.** The panel's `values` must declare `"items": []` **and** `"tools": []`; the file list goes into a `crt.GridContainer` in the `items` slot, and the toolbar controls into a `crt.GridContainer` (+ a `crt.FlexContainer` row) in the `tools` slot — never directly on the panel. Omitting a slot array throws `Item "<PanelName>" is not a container for other items` at runtime and the form does not render.

## 5. Quick checklist

- [ ] `crt.ExpansionPanel` inserted with a localized `title` and generous `rowSpan` (the list is tall).
- [ ] `crt.FileList` inside the panel's inner `crt.GridContainer`, with `masterRecordColumnValue: "$Id"` and the correct `recordColumnName`.
- [ ] File list data source added (`modelConfigDiff`) keyed `<fileList.name>DS`.
- [ ] Upload button `params.viewElementName` = file list name.
- [ ] Refresh button `params.dataSourceName` = file list name + `DS`.
- [ ] Search filter `targetAttributes` = `[<fileList.name>]`.
