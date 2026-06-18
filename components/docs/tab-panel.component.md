# How to Add a Tab Panel (`crt.TabPanel`) to a Freedom UI Page

> Audience: code agent inserting a `crt.TabPanel` into a Creatio Freedom UI page schema.
>
> `crt.TabPanel` is the **outer multi-tab container**. It renders a tab header and a
> body area; each child (a `crt.TabContainer`) becomes one tab.

> **Naming note.** The runtime type `"crt.TabPanel"` is registered by **two** Angular
> classes:
>
> - `CrtTabPanelContainerComponent` — the lighter container with `styleType`,
>   `selectedTab`, header-color knobs. Reached via `tab-panel-container.component.ts`.
> - `CrtTabPanelComponent` (extends `CrtBaseTabPanelComponent`) — the richer class that
>   adds `mode`, `bodyBackgroundColor`, `tabTitleColor`, `selectedTabTitleColor`,
>   `fitContent`, `stretch`, `allowToggleClose`, `isToggleTabHeaderVisible`,
>   `showTabViewModelContent`, and the `items` setter.
>
> Both classes resolve to the same view-element type. The full property surface this
> document references is the **union** of both; in practice the designer emits any of
> these fields onto a single `"crt.TabPanel"` insert without distinguishing classes.

For the per-tab card, see crt.TabContainer guide.

## Metadata

- **Category**: containers
- **Container**: yes
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.HeaderContainer`
- **Typical children**: `crt.TabContainer`

---

## 1. Mental model

```
crt.TabPanel              <-- this doc (outer container, with header)
└── crt.TabContainer       (tab card 1)
└── crt.TabContainer       (tab card 2)
└── ...
```

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.TabPanel"`. |
| 2+ | `viewConfigDiff` (more entries) | One `insert` op per tab, each with `type: "crt.TabContainer"` and `parentName: "<TabPanel_id>"`. |
| 3 | `viewModelConfigDiff` (optional) | A page attribute for the currently selected tab (`selectedTab` or `selectedTabIndex`). |

`crt.TabPanel` itself does not bind to entity columns — it only manages tab navigation.

### 1.1 `contentSlots`

```
contentSlots: ['items']
```

- `"items"` — each child is a `crt.TabContainer`.

### 1.2 Naming convention

```
TabPanel_<id>           // outer panel
TabContainer_<id>       // each tab card
TabPanel_<id>_selected  // optional attribute for the selected tab
```

---

## 2. Step-by-step recipe

### 2.1 (Optional) Declare the selection attribute (`viewModelConfigDiff` entry)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "TabPanel_xkp4r_selected": {
        "value": { "value": "general", "displayValue": "General" }
      }
    }
  }
}
```

### 2.2 Insert the outer tab panel (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "TabPanel_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TabPanel",
    "styleType": "default",
    "selectedTab": "$TabPanel_xkp4r_selected",
    "selectedTabChange": { "request": "crt.PersistSelectedTabRequest" },
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 10 }
  }
}
```

### 2.3 Insert tab children (`viewConfigDiff` entries)

```jsonc
[
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
      "layoutConfig": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 8 }
    }
  },
  {
    "operation": "insert",
    "name": "TabContainer_history",
    "parentName": "TabPanel_xkp4r",
    "propertyName": "items",
    "index": 1,
    "values": {
      "type": "crt.TabContainer",
      "caption": "#ResourceString(Tab_History_Caption)#",
      "icon": "history",
      "layoutConfig": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 8 }
    }
  }
]
```

Each `TabContainer` then receives its own body children via further `insert` ops — see crt.TabContainer guide.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.TabPanel` are in `ComponentRegistry.json` under `componentType: "crt.TabPanel"`. This
guide covers only the assembly mechanics — what to put where in `viewConfigDiff` /
`viewModelConfigDiff` / `modelConfigDiff`.

---

## 4. Copy-paste minimal example

```jsonc
// viewConfigDiff entries — TabPanel + two TabContainers
[
  {
    "operation": "insert",
    "name": "RecordTabs",
    "parentName": "MainContainer",
    "propertyName": "items",
    "index": 0,
    "values": {
      "type": "crt.TabPanel",
      "styleType": "default",
      "layoutConfig": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 10 }
    }
  },
  {
    "operation": "insert",
    "name": "RecordTabs_Profile",
    "parentName": "RecordTabs",
    "propertyName": "items",
    "index": 0,
    "values": {
      "type": "crt.TabContainer",
      "caption": "#ResourceString(Tab_Profile_Caption)#",
      "icon": "profile",
      "layoutConfig": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 8 }
    }
  },
  {
    "operation": "insert",
    "name": "RecordTabs_Activities",
    "parentName": "RecordTabs",
    "propertyName": "items",
    "index": 1,
    "values": {
      "type": "crt.TabContainer",
      "caption": "#ResourceString(Tab_Activities_Caption)#",
      "icon": "activity",
      "layoutConfig": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 8 }
    }
  }
]
```

---

## 5. Common pitfalls

1. **No `crt.TabContainer` children** — the panel renders the header strip but no body content; no tabs appear.
2. **Mixing `selectedTabIndex` and `selectedTab` bindings** — they fire `*Change` events independently. Pick one to bind; treat the other as read-only.
3. **`selectedTabIndex` bound to a stale attribute** — when the page reorders or hides tabs, the index becomes meaningless. Prefer `selectedTab` (a `LookupValue` containing the tab `name`).
4. **`styleType: "fullyColored"` + transparent `headerBackgroundColor: "auto"`** — gives an unstyled header. Set `headerBackgroundColor` explicitly when overriding the style type.
5. **Hidden tabs without `selectedTab` update** — if the currently active tab becomes `visible: false`, the panel falls back to the first tab. Update the bound attribute to a still-visible tab to avoid the jump.
6. **Heavy children mount eagerly** — all tabs render at the same time by default. Avoid putting expensive components (Feed, DataGrid with many columns) in every tab; or lazily mount them via a `visible: "$tabActiveAttr"` flag.
7. **Forgetting `mode: "toggle"` when linking a sibling `crt.ButtonToggleGroup`** — the group's `for: "<TabPanel>"` only behaves correctly when the panel is in toggle mode. With `mode: "tab"` (the default) the toggle group renders empty.

---

## 6. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.TabPanel"`, unique `name`, `propertyName: "items"`.
- [ ] At least one `crt.TabContainer` child (more for multi-tab UX).
- [ ] Each `TabContainer` has a unique `name` and `caption`.
- [ ] If you need to persist or react to tab changes, bind `selectedTab` to a `viewModelConfigDiff` attribute and wire `selectedTabChange.request`.
- [ ] `mode` chosen intentionally (`"tab"` for traditional tabs; `"toggle"` for panels driven by an external `crt.ButtonToggleGroup`).
- [ ] `styleType` chosen intentionally.
- [ ] `layoutConfig` matches the parent container shape (grid `{column,row,colSpan,rowSpan}` vs flex `{basis,maxWidth,minWidth}`) and provides enough room for the largest tab body.
