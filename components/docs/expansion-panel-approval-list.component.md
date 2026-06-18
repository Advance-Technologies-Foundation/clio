# How to Add an Approval List (`crt.ExpansionPanel` preset) to a Freedom UI Page

> Audience: code agent assembling the **"Approval list"** designer element into a Creatio Freedom UI page schema.
>
> "Approval list" (caption key `Components.ApprovalList.Caption`) is **not a standalone component** — it is a preset
> of `crt.ExpansionPanel`: a collapsible panel pre-filled with a `crt.ApprovalList` in its body and a refresh /
> search toolbar. Dropping it in the designer runs `crt.AddApprovalListCommand`, which inserts the panel + list and
> then **connects the refresh button and search filter to the list**. Reproducing it by hand means replicating BOTH
> the nested structure AND that connection.
>
> Cross-references (do not re-document them here):
> - Base panel mechanics: [`expansion-panel.component.md`](./expansion-panel.component.md).
> - Full `crt.ApprovalList` property reference: its own guide
>   (`componentType: "crt.ApprovalList"` in `ComponentRegistry.json`, `approval-list.component.md`).

## Metadata

- **Designer element**: `Components.ApprovalList.Caption` — toolbar group `Components`, position `140`.
- **Create command**: `crt.AddApprovalListCommand` (extends `crt.AddViewItemCommand`).
- **Wraps**: `crt.ExpansionPanel` → body (`items`) holds a `crt.ApprovalList`; header (`tools`) holds refresh / search.
- **Requires**: no extra package/feature.

---

## 1. What the designer drop produces

```
crt.ExpansionPanel (title, expanded:true, fullWidthHeader:false)
├─ items → crt.GridContainer (2 columns)
│           └─ crt.ApprovalList  name:"ApprovalList_<guid>"  masterRecordColumnValue:"$Id"  recordColumnName:"RecordId"  rowSpan:10
└─ tools → crt.GridContainer → crt.FlexContainer (row)
           ├─ crt.Button "refresh" icon:reload-icon  clicked: crt.LoadDataRequest { config:{ loadType:"reload" } }
           └─ crt.SearchFilter  (placeholder: Components.SearchFilter.Placeholder)
```

`masterRecordColumnValue: "$Id"` binds the list to the current record; `recordColumnName` (`RecordId` by default)
is the column on the approval entity that points back to the master. Real pages also set `features.editable`.

## 2. Post-insert wiring — the part hand-authoring gets wrong

After insert, `AddApprovalListCommand._updateExpansionPanelElements` finds the `crt.ApprovalList` and patches:

| Toolbar element | Matched by | Field the command sets |
|---|---|---|
| Refresh button | `clicked.request === "crt.LoadDataRequest"` | `clicked.params.dataSourceName = <approvalList.name> + "DS"` |
| Search filter | `type === "crt.SearchFilter"` | `targetAttributes = [<approvalList.name>]` (only if not already set) |

There is **no add/create button** in this preset (approvals are system-driven, not user-created from the toolbar).
Both wiring values derive from the approval list's `name`.

## 3. Step-by-step recipe

```jsonc
[
  { "operation": "insert", "name": "ApprovalsPanel", "parentName": "MainContainer", "propertyName": "items", "index": 0,
    "values": { "type": "crt.ExpansionPanel", "title": "#ResourceString(ApprovalsPanel_title)#", "expanded": true,
      "layoutConfig": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 10 } } },

  { "operation": "insert", "name": "ApprovalsPanel_wrap", "parentName": "ApprovalsPanel", "propertyName": "items", "index": 0,
    "values": { "type": "crt.GridContainer", "columns": ["minmax(32px, 1fr)", "minmax(32px, 1fr)"] } },

  { "operation": "insert", "name": "ApprovalList_main", "parentName": "ApprovalsPanel_wrap", "propertyName": "items", "index": 0,
    "values": { "type": "crt.ApprovalList", "masterRecordColumnValue": "$Id", "recordColumnName": "RecordId",
      "features": { "editable": false },
      "layoutConfig": { "column": 1, "row": 1, "colSpan": 2, "rowSpan": 10 } } },

  { "operation": "insert", "name": "ApprovalList_mainRefreshBtn", "parentName": "ApprovalsPanel", "propertyName": "tools", "index": 0,
    "values": { "type": "crt.Button", "icon": "reload-icon", "iconPosition": "only-icon", "color": "default",
      "clicked": { "request": "crt.LoadDataRequest", "params": { "config": { "loadType": "reload" }, "dataSourceName": "ApprovalList_mainDS" } } } },

  { "operation": "insert", "name": "ApprovalList_mainSearch", "parentName": "ApprovalsPanel", "propertyName": "tools", "index": 1,
    "values": { "type": "crt.SearchFilter", "targetAttributes": ["ApprovalList_main"] } }
]
```

The `crt.ApprovalList` also needs its own data source (`modelConfigDiff`) keyed `ApprovalList_mainDS` — see the
`crt.ApprovalList` guide. The key MUST be `<approvalList.name> + "DS"` so the refresh button resolves it.

> **`crt.SearchFilter` is preprocessor-driven.** `targetAttributes` is the high-level wiring `AddApprovalListCommand` sets; at save time the preprocessor expands it into a `_filterOptions.expose` block (with a `crt.SearchFilterAttributeConverter`). Saved schemas show that expanded form, but authoring `targetAttributes` is sufficient.

## 4. Common pitfalls

1. **Refresh `dataSourceName` mismatch.** It must be exactly `<approvalList.name>DS`, or refresh targets nothing.
2. **Search not scoped.** `targetAttributes` must contain the approval list name.
3. **Expecting an add button.** This preset has none — do not invent a create action; approvals come from the approval process.
4. **`recordColumnName` left at `RecordId` on a non-default entity.** Set it to the FK column on the approval entity that references the master record.
5. **Nested element is one level deeper.** The list goes into the inner `crt.GridContainer`, not directly into `crt.ExpansionPanel.items`.

## 5. Quick checklist

- [ ] `crt.ExpansionPanel` inserted with a localized `title` and generous `rowSpan`.
- [ ] `crt.ApprovalList` inside the panel's inner `crt.GridContainer`, with `masterRecordColumnValue: "$Id"` and correct `recordColumnName`.
- [ ] Approval list data source added (`modelConfigDiff`) keyed `<approvalList.name>DS`.
- [ ] Refresh button `params.dataSourceName` = list name + `DS`.
- [ ] Search filter `targetAttributes` = `[<approvalList.name>]`.
