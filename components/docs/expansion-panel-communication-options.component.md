# How to Add Communication Options (`crt.ExpansionPanel` preset) to a Freedom UI Page

> Audience: code agent assembling the **"Communication options"** designer element into a Creatio Freedom UI page schema.
>
> "Communication options" (caption key `Components.CommunicationOptions.Caption`) is **not a standalone component** —
> it is a preset of `crt.ExpansionPanel`: a collapsible panel pre-filled with a `crt.CommunicationOptions` widget
> (phones, emails, web) in its body and an "add" button in the header. Dropping it in the designer runs
> `crt.AddCommunicationOptionsCommand`, which inserts the panel + widget and then **binds the widget to the master
> record's Contact/Account column**. Reproducing it by hand means replicating BOTH the nested structure AND that binding.
>
> Cross-references (do not re-document them here):
> - Base panel mechanics: [`expansion-panel.component.md`](./expansion-panel.component.md).
> - Full `crt.CommunicationOptions` property reference: its own guide
>   (`componentType: "crt.CommunicationOptions"` in `ComponentRegistry.json`, `communication-options.component.md`).

## Metadata

- **Designer element**: `Components.CommunicationOptions.Caption` — toolbar group `Components`, position `100`.
- **Create command**: `crt.AddCommunicationOptionsCommand` (extends `crt.BaseAddReferencedItemCommand`).
- **Wraps**: `crt.ExpansionPanel` → body (`items`) holds a `crt.CommunicationOptions`; header (`tools`) holds the "add" button.
- **Requires** (gating — the element is hidden in the toolbox without these):
  - feature `CommonCommunicationsBehavior` enabled, **and**
  - package `CrtCustomer360App` installed.
- **Master entity must be `Contact` or `Account`** — the wiring only fires for those two (`allowedEntityNames`).

---

## 1. What the designer drop produces

```
crt.ExpansionPanel (title, expanded:true, fullWidthHeader:false)
├─ items → crt.GridContainer (2 columns)
│           └─ crt.CommunicationOptions  name:"CommunicationOptions_<guid>"
│              readonly:true (→ flipped to false by the command)  columnsCount:2  showNoDataPlaceholder:true  labelPosition:"auto"  rowSpan:1
└─ tools → crt.GridContainer → crt.FlexContainer (row)
           └─ crt.Button "add"  icon:add-button-icon  clicked: crt.AddCommunicationOptionsRequest
```

## 2. Post-insert wiring — the part hand-authoring gets wrong

This preset's command extends `BaseAddReferencedItemCommand`, so it does two things after insert:

**a) Reference-column binding (`trySetRefereceColumn` + the command's overrides).** It reads the page's primary
data source, and **only if that entity is `Contact` or `Account`**:
1. resolves (or creates) a view-model attribute over `<PrimaryDataSource>.Id` — typically the `Id` attribute;
2. sets on the widget: `masterRecordColumnValue = "$<attr>"` (i.e. `"$Id"`), `masterRecordColumnName = "Contact"` or `"Account"`, and `readonly = false`.

So the `readonly: true` from the static drop is **overwritten to `false`**, and the widget gets a master-record binding. If the page entity is neither Contact nor Account, none of this runs and the widget stays read-only/unbound.

**b) Add-button binding (`_updateExpansionPanelElements`).** It finds the button whose
`clicked.request === "crt.AddCommunicationOptionsRequest"` and sets `clicked.params.viewElementName = <widget.name>`.

## 3. Step-by-step recipe

```jsonc
[
  // panel — declares BOTH slot arrays empty
  { "operation": "insert", "name": "CommOptionsPanel", "parentName": "MainContainer", "propertyName": "items", "index": 0,
    "values": { "type": "crt.ExpansionPanel", "title": "#ResourceString(CommOptionsPanel_title)#", "expanded": true,
      "items": [], "tools": [],
      "layoutConfig": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 4 } } },

  // body: GridContainer in the items slot → CommunicationOptions inside it
  { "operation": "insert", "name": "CommOptionsPanel_wrap", "parentName": "CommOptionsPanel", "propertyName": "items", "index": 0,
    "values": { "type": "crt.GridContainer", "columns": ["minmax(32px, 1fr)", "minmax(32px, 1fr)"], "items": [] } },
  { "operation": "insert", "name": "CommunicationOptions_main", "parentName": "CommOptionsPanel_wrap", "propertyName": "items", "index": 0,
    "values": { "type": "crt.CommunicationOptions", "columnsCount": 2, "showNoDataPlaceholder": true, "labelPosition": "auto",
      "readonly": false, "masterRecordColumnValue": "$Id", "masterRecordColumnName": "Contact",
      "layoutConfig": { "column": 1, "row": 1, "colSpan": 2, "rowSpan": 1 } } },

  // tools: GridContainer in the tools slot → FlexContainer(row) → the add button (never directly in tools)
  { "operation": "insert", "name": "CommOptionsPanel_toolbar", "parentName": "CommOptionsPanel", "propertyName": "tools", "index": 0,
    "values": { "type": "crt.GridContainer", "columns": ["minmax(32px, 1fr)"], "items": [] } },
  { "operation": "insert", "name": "CommOptionsPanel_toolbarRow", "parentName": "CommOptionsPanel_toolbar", "propertyName": "items", "index": 0,
    "values": { "type": "crt.FlexContainer", "direction": "row", "alignItems": "center", "gap": "none", "items": [] } },
  { "operation": "insert", "name": "CommunicationOptions_mainAddBtn", "parentName": "CommOptionsPanel_toolbarRow", "propertyName": "items", "index": 0,
    "values": { "type": "crt.Button", "icon": "add-button-icon", "iconPosition": "only-icon", "color": "default",
      "clicked": { "request": "crt.AddCommunicationOptionsRequest", "params": { "viewElementName": "CommunicationOptions_main" } } } }
]
```

Use `masterRecordColumnName: "Account"` instead of `"Contact"` when the page entity is `Account`. `masterRecordColumnValue`
references the id attribute (`"$Id"`); ensure that attribute exists in `viewModelConfigDiff`.

## 4. Common pitfalls

1. **Element absent from the toolbox.** It is gated behind the `CommonCommunicationsBehavior` feature **and** the `CrtCustomer360App` package — without both, it never appears, so it cannot be dropped.
2. **Wrong master entity.** The auto-binding only fires for `Contact`/`Account`. On any other entity the widget stays `readonly` and unbound — there is no Communication Options support there.
3. **`readonly` left `true`.** The static drop ships `readonly: true`; the command flips it to `false`. When hand-authoring an editable widget, set `readonly: false` yourself.
4. **Add button not bound.** Without `params.viewElementName = <widget.name>` the add action has no target.
5. **Slot composition.** The panel's `values` must declare `"items": []` **and** `"tools": []`; the widget goes into a `crt.GridContainer` in the `items` slot, and the add button into a `crt.GridContainer` (+ a `crt.FlexContainer` row) in the `tools` slot — never directly on the panel. Omitting a slot array throws `Item "<PanelName>" is not a container for other items` at runtime and the form does not render.

## 5. Quick checklist

- [ ] `CommonCommunicationsBehavior` feature + `CrtCustomer360App` package present, and the page entity is `Contact` or `Account`.
- [ ] `crt.ExpansionPanel` inserted with a localized `title`.
- [ ] `crt.CommunicationOptions` inside the panel's inner `crt.GridContainer`, with `readonly: false`, `masterRecordColumnValue: "$Id"`, `masterRecordColumnName: "Contact"`/`"Account"`.
- [ ] Add button `params.viewElementName` = widget name.
