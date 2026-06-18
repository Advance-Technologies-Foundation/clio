# How to Add a Tab Panel Header (`crt.TabPanelHeader`) to a Freedom UI Page

> Audience: code agent inserting `crt.TabPanelHeader` into a Creatio Freedom UI page schema.
> A `crt.TabPanelHeader` is a navigation component that renders a Material tab strip and coordinates tab selection with its `crt.TabPanelHeaderItem` children. It holds its children through a `tabs` content slot (not `items`), and tracks the active tab via a zero-based `selectedTabIndex`.

## Metadata
- **Category**: navigation
- **Container**: yes (children go into the `tabs` content slot)
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.TabContainer`, root page container
- **Typical children**: `crt.TabPanelHeaderItem`

---

## 1. Mental model — the 2 places you must edit
| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op for `crt.TabPanelHeader` with `selectedTabIndex` and `tabs: []`. |
| 2 | `viewConfigDiff` | One `insert` op per `crt.TabPanelHeaderItem` child, each pointing to this header as `parentName` with `propertyName: "tabs"`. |

`crt.TabPanelHeader` uses a **content slot** named `tabs` — not `items`. Every child must use `propertyName: "tabs"` in its own `insert` op.

### 1.1 Naming convention
```
TabPanelHeader_<id>       // header element name
TabPanelHeaderItem_<id>   // child tab item name (parentName = TabPanelHeader_<id>, propertyName = "tabs")
$TabPanelHeader_<id>_selectedTabIndex  // attribute if you bind selectedTabIndex from viewModel
```

---

## 2. Step-by-step recipe

### 2.1 Insert the `crt.TabPanelHeader`

```jsonc
{
  "operation": "insert",
  "name": "TabPanelHeader_abc1",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TabPanelHeader",
    "selectedTabIndex": 0,
    "tabs": []
  }
}
```

### 2.2 Insert each `crt.TabPanelHeaderItem` child

```jsonc
{
  "operation": "insert",
  "name": "TabPanelHeaderItem_abc1_tab1",
  "parentName": "TabPanelHeader_abc1",
  "propertyName": "tabs",
  "index": 0,
  "values": {
    "type": "crt.TabPanelHeaderItem",
    "caption": "#ResourceString(TabPanelHeaderItem_abc1_tab1_Caption)#",
    "selected": true
  }
}
```

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.TabPanelHeader` are in `ComponentRegistry.json` under `componentType: "crt.TabPanelHeader"`. This guide covers
only the assembly mechanics.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entries — header + two tab items
{
  "operation": "insert",
  "name": "CardTabsHeader",
  "parentName": "CardContentContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TabPanelHeader",
    "selectedTabIndex": 0,
    "tabs": []
  }
},
{
  "operation": "insert",
  "name": "CardTab_General",
  "parentName": "CardTabsHeader",
  "propertyName": "tabs",
  "index": 0,
  "values": {
    "type": "crt.TabPanelHeaderItem",
    "caption": "#ResourceString(CardTab_General_Caption)#",
    "selected": true
  }
},
{
  "operation": "insert",
  "name": "CardTab_History",
  "parentName": "CardTabsHeader",
  "propertyName": "tabs",
  "index": 1,
  "values": {
    "type": "crt.TabPanelHeaderItem",
    "caption": "#ResourceString(CardTab_History_Caption)#",
    "selected": false
  }
}
```

---

## 6. Driving from page state

```jsonc
// viewModelConfigDiff — declare attribute
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "CardTabsHeader_selectedTabIndex": { "value": 0 }
    }
  }
}

// viewConfigDiff.values — bind
"selectedTabIndex": "$CardTabsHeader_selectedTabIndex"
```

`selectedTabIndex` is `propertyBindable` — it accepts a `$Attribute` binding so the active tab can be driven from a handler or set by an external request.

---

## 7. Common pitfalls

- **Using `propertyName: "items"` for tab children.** Tab items must use `propertyName: "tabs"` — this is a named content slot, not the generic `items` slot.
- **Setting `selected: true` on multiple items.** Only one `crt.TabPanelHeaderItem` should be `selected: true` initially; having multiple selected causes the header to pick the last one and discard the others.
- **`selectedTabIndex` default is `-1`.** An uninitialized header shows no selected tab; always set `selectedTabIndex: 0` (or bind it) to pre-select the first tab.
- **Removing a tab without recalculating `selectedTabIndex`.** When tab items are removed dynamically the header recomputes the index; a stale bound attribute value may cause the wrong tab to appear selected after removal.
- **Forgetting `tabs: []` in `values`.** The content slot must be declared as an empty array even when children are added via separate `insert` ops.

---

## 8. Quick checklist

- [ ] `insert` op with `type: "crt.TabPanelHeader"`, unique `name`, valid `parentName`, `propertyName: "items"` (into its own parent).
- [ ] `tabs: []` present in `values`.
- [ ] `selectedTabIndex` is `0` or bound to a viewModel attribute.
- [ ] Each tab child uses `parentName: "<headerName>"` and `propertyName: "tabs"`.
- [ ] Exactly one child has `selected: true` initially.
- [ ] Each child's `caption` uses `#ResourceString(...)#` for localized text.
