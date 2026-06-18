# How to Add Next Steps (`crt.ExpansionPanel` preset) to a Freedom UI Page

> Audience: code agent assembling the **"Next steps"** designer element into a Creatio Freedom UI page schema.
>
> "Next steps" (caption key `NextSteps.Caption`) is **not a standalone component** — it is a preset of
> `crt.ExpansionPanel`: a collapsible panel pre-filled with a `crt.NextSteps` in its body and an "add step" menu
> button (create activity / create email) in the header. Dropping it in the designer runs `crt.AddNextStepsCommand`,
> which inserts the panel + widget and then **links the widget to the page's master record**. Reproducing it by
> hand means replicating BOTH the nested structure AND that link.
>
> Cross-references (do not re-document them here):
> - Base panel mechanics: [`expansion-panel.component.md`](./expansion-panel.component.md).
> - Full `crt.NextSteps` property reference: its own guide
>   (`componentType: "crt.NextSteps"` in `ComponentRegistry.json`, `next-steps.component.md`).

## Metadata

- **Designer element**: `NextSteps.Caption` — toolbar group `Components`, position `150`.
- **Create command**: `crt.AddNextStepsCommand` (extends `crt.AddViewItemCommand`).
- **Wraps**: `crt.ExpansionPanel` → body (`items`) holds a `crt.NextSteps`; header (`tools`) holds the "add step" menu button.
- **Distinct panel defaults**: this preset overrides the shell — `toggleType: "material"`, `togglePosition: "after"`, and zero padding on all sides. The panel container also gets a fixed `name` (`NextStepsContainer_<guid>`).
- **Requires**: no extra package/feature.

---

## 1. What the designer drop produces

```
crt.ExpansionPanel (toggleType:"material", togglePosition:"after", padding:all none, expanded:true)
├─ items → crt.GridContainer (2 columns)
│           └─ crt.NextSteps  name:"NextSteps_<guid>"  masterSchemaId:"$Id"  cardState:"$CardState"  rowSpan:1
└─ tools → crt.GridContainer → crt.FlexContainer (row)
           └─ crt.Button "add step"  icon:add-button-icon  clickMode:"menu"  visible: "$CardState | crt.IsEqual : 'edit'"
              ├─ crt.MenuItem "create activity"  clicked: crt.AddNextStepRequest { entityName:"Activity" }
              └─ crt.MenuItem "create email"     clicked: crt.CreateEmailRequest
```

The widget binds to two page attributes: `masterSchemaId: "$Id"` (the current record) and `cardState: "$CardState"`
(so the add-step button is visible only while the page is in `edit` state).

## 2. Post-insert wiring — the part hand-authoring gets wrong

After insert, `AddNextStepsCommand._linkWithEntitySchemaRecord` finds the `crt.NextSteps` and, **only when
`masterSchemaName` or `masterSchemaId` is missing**, sets:

- `masterSchemaName` = the page's default entity schema (e.g. `"Lead"`), falling back to the `#DataSourceEntityName()#` macro.
- `masterSchemaId` = `"$Id"`.

`cardState: "$CardState"` is part of the static drop, not the command. The toolbar button's
`visible: "$CardState | crt.IsEqual : 'edit'"` and the menu's `crt.AddNextStepRequest` / `crt.CreateEmailRequest`
are fixed by the preset.

## 3. Step-by-step recipe

```jsonc
[
  { "operation": "insert", "name": "NextStepsContainer", "parentName": "MainContainer", "propertyName": "items", "index": 0,
    "values": { "type": "crt.ExpansionPanel", "title": "#ResourceString(NextStepsContainer_title)#", "expanded": true,
      "toggleType": "material", "togglePosition": "after",
      "padding": { "top": "none", "bottom": "none", "left": "none", "right": "none" },
      "layoutConfig": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 2 } } },

  { "operation": "insert", "name": "NextStepsContainer_wrap", "parentName": "NextStepsContainer", "propertyName": "items", "index": 0,
    "values": { "type": "crt.GridContainer", "columns": ["minmax(32px, 1fr)", "minmax(32px, 1fr)"] } },

  { "operation": "insert", "name": "NextSteps_widget", "parentName": "NextStepsContainer_wrap", "propertyName": "items", "index": 0,
    "values": { "type": "crt.NextSteps", "masterSchemaName": "Lead", "masterSchemaId": "$Id", "cardState": "$CardState",
      "layoutConfig": { "column": 1, "row": 1, "colSpan": 2, "rowSpan": 1 } } },

  { "operation": "insert", "name": "NextSteps_widgetAddBtn", "parentName": "NextStepsContainer", "propertyName": "tools", "index": 0,
    "values": { "type": "crt.Button", "icon": "add-button-icon", "iconPosition": "only-icon", "color": "default",
      "clickMode": "menu", "visible": "$CardState | crt.IsEqual : 'edit'",
      "menuItems": [
        { "type": "crt.MenuItem", "caption": "#ResourceString(NextSteps_CreateActivity)#", "clicked": { "request": "crt.AddNextStepRequest", "params": { "entityName": "Activity" } } },
        { "type": "crt.MenuItem", "caption": "#ResourceString(NextSteps_CreateEmail)#",    "clicked": { "request": "crt.CreateEmailRequest" } }
      ] } }
]
```

`masterSchemaName` must be the page's entity (here `"Lead"`); use the `#DataSourceEntityName()#` macro if you want
the platform to resolve it. `$CardState` must be a real page attribute, or the add-step button's `visible`
expression never evaluates true.

## 4. Common pitfalls

1. **`masterSchemaName` left empty.** The widget cannot load steps without the master entity name; the command sets it automatically, so when hand-authoring set it explicitly to the page entity.
2. **`masterSchemaId` not `"$Id"`.** It must reference the current record's id attribute.
3. **Missing `$CardState` attribute.** Both `cardState` and the add button's `visible` rely on it; without the attribute the add action never shows.
4. **Different panel chrome.** This preset uses `toggleType: "material"` + `togglePosition: "after"` + zero padding — keep them to match the standard Next Steps look.
5. **Nested element is one level deeper.** The widget goes into the inner `crt.GridContainer`, not directly into `crt.ExpansionPanel.items`.
6. **Real pages often host Next Steps in a `crt.TabContainer` instead.** In practice `crt.NextSteps` is most commonly placed in a dedicated "Next steps" `crt.TabContainer` tab (no ExpansionPanel/GridContainer wrapper, `masterSchemaName` resolved via the `#DataSourceEntityName()#` macro). This recipe covers the ExpansionPanel toolbar preset; when matching an existing page, check which host it actually uses.

## 5. Quick checklist

- [ ] `crt.ExpansionPanel` inserted with `toggleType: "material"`, `togglePosition: "after"`, zero padding.
- [ ] `crt.NextSteps` inside the panel's inner `crt.GridContainer`, with `masterSchemaName` = page entity and `masterSchemaId: "$Id"`.
- [ ] `cardState: "$CardState"` set and a `CardState` page attribute exists.
- [ ] Add-step menu button present with `visible: "$CardState | crt.IsEqual : 'edit'"` and the two menu items.
