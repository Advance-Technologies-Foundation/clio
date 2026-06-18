# How to Add a Tab Container (`crt.TabContainer`) to a Freedom UI Page

> Audience: code agent inserting a `crt.TabContainer` into a Creatio Freedom UI page schema.
>
> `crt.TabContainer` is the **inner card** for a single tab. It is always a child of a
> `crt.TabPanel` outer container. Each `crt.TabContainer` holds the body of one tab.

For the outer multi-tab container, see crt.TabPanel guide.

## Metadata

- **Category**: containers
- **Container**: yes
- **Parent types**: `crt.TabPanel`
- **Typical children**: `crt.GridContainer`, `crt.FlexContainer`, `crt.ExpansionPanel` (body via `items`); `crt.Button`, `crt.MenuItem` (header via `tools`)

---

## 1. Mental model

```
crt.TabPanel              (outer container with header)
├── crt.TabContainer       (tab card 1)  <-- this doc
│   ├── ... children ...
│   └── (tools)
└── crt.TabContainer       (tab card 2)
    └── ... children ...
```

- `crt.TabPanel` is the parent; multiple `crt.TabContainer` children become **sibling** tabs (do not nest `TabContainer` inside another `TabContainer`).
- Each `crt.TabContainer` has `contentSlots: ['items']` for its body; some variants also support `'tools'`.

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.TabContainer"` and `parentName: "<TabPanel_id>"`. |
| 2+ | `viewConfigDiff` (more entries) | Additional `insert` ops for the tab's body, each with `parentName: "<TabContainer_id>"` and `propertyName: "items"`. |

### 1.1 Naming convention

```
TabPanel_<id>            // outer multi-tab container (see TabPanel.component.md)
TabContainer_<id>        // single tab card; <id> distinct per tab
```

---

## 2. Step-by-step recipe

### 2.1 Insert the tab card (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "TabContainer_general",
  "parentName": "TabPanel_xkp4r",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TabContainer",
    "caption": "#ResourceString(Tab_General_Caption)#",
    "icon": "info-icon",
    "iconPosition": "left-icon",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 8 }
  }
}
```

### 2.2 Insert children into the tab body

```jsonc
{
  "operation": "insert",
  "name": "TabContainer_general_NameInput",
  "parentName": "TabContainer_general",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Input",
    "label": "#ResourceString(NameInput_Label)#",
    "control": "$Contact_Name",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 2, "rowSpan": 1 }
  }
}
```

The bound attribute and entity column setup follows the standard form-control rules — see crt.Input guide.

### 2.3 Insert children into the `tools` header slot

To place an action button (or another view element) in the tab's header — to the right of the caption — insert with `propertyName: "tools"`:

```jsonc
{
  "operation": "insert",
  "name": "TabContainer_general_AddBtn",
  "parentName": "TabContainer_general",
  "propertyName": "tools",
  "index": 0,
  "values": {
    "type": "crt.Button",
    "icon": "add-button-icon",
    "iconPosition": "only-icon",
    "displayType": "text",
    "color": "default",
    "clicked": { "request": "crt.CreateRecordRequest" }
  }
}
```

Tools live in the tab header; keep them compact (icon-only buttons or small inputs) to avoid breaking the flex layout. Real Creatio schemas (e.g. `MainShell.js`) extensively use this slot for per-tab action buttons.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.TabContainer` are in `ComponentRegistry.json` under `componentType: "crt.TabContainer"`.
This guide covers only the assembly mechanics — what to put where in `viewConfigDiff` /
`viewModelConfigDiff` / `modelConfigDiff`.

---

## 4. Copy-paste minimal example

See § 2 above — already minimal. Pair with a `crt.TabPanel` parent.

---

## 5. Common pitfalls

1. **`crt.TabContainer` at the page root** — the platform expects a `crt.TabPanel` parent. A free-standing `TabContainer` renders an empty card.
2. **Missing `caption`** — the tab still appears in the header but with an empty label. Always set `caption`.
3. **Identical `name` between tabs** — names must be unique across the page. Reusing breaks subsequent inserts.
4. **`visible: "$attr"` toggled at runtime** — supported, but the tab header redraws and may jump the user's selection. Prefer hiding via business logic *before* the page renders rather than mid-session toggles.
5. **Heavy children (DataGrid, Feed) in every tab** — they all mount when the tab panel initializes. Use `crt.TabPanel`'s lazy strategy if performance is a concern (see crt.TabPanel guide).

---

## 6. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.TabContainer"`, unique `name`, `parentName: "<TabPanel_id>"`, `propertyName: "items"`.
- [ ] `caption` (localized) provided.
- [ ] `icon` set when the design includes per-tab icons.
- [ ] Children inserted with `parentName: "<TabContainer_id>"` and `propertyName: "items"` (body) or `"tools"` (header toolbar).
- [ ] `layoutConfig` provides enough `rowSpan` for the body content (or is omitted to let the parent TabPanel size the tab).
