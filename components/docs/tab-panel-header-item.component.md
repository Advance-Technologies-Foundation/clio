# How to Add a Tab Panel Header Item (`crt.TabPanelHeaderItem`) to a Freedom UI Page

> Audience: code agent inserting `crt.TabPanelHeaderItem` into a Creatio Freedom UI page schema.
> A `crt.TabPanelHeaderItem` is a single tab within a `crt.TabPanelHeader`. It renders a labeled tab button with optional icon and color styling, and emits `selectedChange` when the user clicks it.

## Metadata
- **Category**: navigation
- **Container**: no
- **Parent types**: `crt.TabPanelHeader` (via `propertyName: "tabs"`)
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit
| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `parentName` pointing to a `crt.TabPanelHeader` and `propertyName: "tabs"`. |

`crt.TabPanelHeaderItem` is always a child of a `crt.TabPanelHeader` — never a top-level element or child of a generic container.

### 1.1 Naming convention
```
TabPanelHeaderItem_<id>    // view element name
```

---

## 2. Step-by-step recipe

### 2.1 Insert the `crt.TabPanelHeaderItem`

```jsonc
{
  "operation": "insert",
  "name": "TabPanelHeaderItem_xyz1",
  "parentName": "CardTabsHeader",
  "propertyName": "tabs",
  "index": 0,
  "values": {
    "type": "crt.TabPanelHeaderItem",
    "caption": "#ResourceString(TabPanelHeaderItem_xyz1_Caption)#",
    "selected": true,
    "styleType": "default"
  }
}
```

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.TabPanelHeaderItem` are in `ComponentRegistry.json` under `componentType: "crt.TabPanelHeaderItem"`. This guide covers
only the assembly mechanics.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — two tab items inside a TabPanelHeader
{
  "operation": "insert",
  "name": "GeneralTab",
  "parentName": "CardTabsHeader",
  "propertyName": "tabs",
  "index": 0,
  "values": {
    "type": "crt.TabPanelHeaderItem",
    "caption": "#ResourceString(GeneralTab_Caption)#",
    "selected": true,
    "styleType": "default"
  }
},
{
  "operation": "insert",
  "name": "HistoryTab",
  "parentName": "CardTabsHeader",
  "propertyName": "tabs",
  "index": 1,
  "values": {
    "type": "crt.TabPanelHeaderItem",
    "caption": "#ResourceString(HistoryTab_Caption)#",
    "selected": false,
    "styleType": "default",
    "selectedChange": { "request": "crt.HistoryTabSelectedRequest" }
  }
}
```

---

## 7. Common pitfalls

- **Using `propertyName: "items"` instead of `"tabs"`.** The parent `crt.TabPanelHeader` uses a content slot named `tabs`; using `items` causes the child to be invisible.
- **Multiple items with `selected: true`.** Only one item should have `selected: true` initially; conflicts cause the parent to pick the last selected item.
- **`titleColor`/`selectedTitleColor` with `styleType: "default"`.** Color overrides only render when `styleType` is `"fullyColored"` or `"partiallyColored"`; they are silently ignored for `"default"`.
- **`icon` without `iconSource`.** Set `iconSource: "system-icon"` when using a platform icon name, or `"url"` for a remote image URL. Without the correct `iconSource` the icon may not render.
- **`selectedChange` with no matching handler.** The event fires silently; wire a request handler if custom logic is needed when the tab is activated.

---

## 8. Quick checklist

- [ ] `insert` op with `parentName` pointing to a `crt.TabPanelHeader` element.
- [ ] `propertyName: "tabs"` (not `"items"`).
- [ ] `caption` uses `#ResourceString(...)#` for localized text.
- [ ] Exactly one sibling has `selected: true` initially.
- [ ] If `icon` is set, `iconSource` is also set (`"system-icon"` or `"url"`).
- [ ] `styleType` matches the intended color scheme (`"default"`, `"partiallyColored"`, `"fullyColored"`).
