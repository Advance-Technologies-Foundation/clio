# How to Add a Navigation Panel Item (`crt.NavigationPanelItem`) to a Freedom UI Page

> Audience: code agent inserting a `crt.NavigationPanelItem` into a Creatio Freedom UI page schema.
>
> `crt.NavigationPanelItem` is a single clickable row inside a `crt.NavigationPanel`. It renders an icon
> (with optional colored background), a caption, and an optional link. In practice it is always created
> internally by the `crt.NavigationPanel` from its `dataSource` — you rarely insert it directly.

## Metadata

- **Category**: navigation
- **Container**: no
- **Parent types**: `crt.NavigationPanel` (internal slot)
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.NavigationPanelItem"` and the item data. |

`crt.NavigationPanel` creates its items automatically from `dataSource`. Only insert
`crt.NavigationPanelItem` directly when building a custom navigation list outside the panel shell.

### 1.1 Naming convention

```
NavigationPanelItem_<id>     // view element name
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "NavigationPanelItem_abc",
  "values": {
    "type": "crt.NavigationPanelItem",
    "panelItem": {
      "code": "my-section",
      "caption": "#ResourceString(MySection_Caption)#",
      "iconUrl": "some-icon-url"
    },
    "isCollapsed": false,
    "usePanelIconBackground": false,
    "openItemAsLink": false,
    "selected": { "request": "crt.NavigationItemSelectedRequest" }
  },
  "parentName": "CustomNavContainer",
  "propertyName": "items",
  "index": 0
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.NavigationPanelItem` are in `ComponentRegistry.json` under `componentType: "crt.NavigationPanelItem"`.
This guide covers only the assembly mechanics.

---

## 5. Copy-paste minimal example

```jsonc
{
  "operation": "insert",
  "name": "NavigationPanelItem_sales",
  "values": {
    "type": "crt.NavigationPanelItem",
    "panelItem": {
      "code": "sales",
      "caption": "#ResourceString(Sales_Caption)#"
    },
    "isCollapsed": false,
    "usePanelIconBackground": false,
    "selected": { "request": "crt.NavigationItemSelectedRequest" }
  },
  "parentName": "CustomNavList",
  "propertyName": "items",
  "index": 0
}
```

---

## 7. Common pitfalls

1. **Inserting manually when a `crt.NavigationPanel` already handles the list.** The panel creates items from `dataSource` automatically — adding duplicate items via `insert` ops results in double entries.
2. **`panelItem` missing `code`.** The item click handler (`onSelectedItem`) emits `panelItem.code`; a missing code breaks the `itemClicked` event chain.
3. **`usePanelIconBackground: null`.** The setter ignores `null` silently; always pass an explicit boolean.
4. **`isCollapsed: true` without the panel being in collapsed mode.** The collapsed layout hides captions; only set it when the parent panel is in `"collapsed"` display mode.

---

## 8. Quick checklist

- [ ] `insert` op with `type: "crt.NavigationPanelItem"`, unique `name`, valid `parentName`.
- [ ] `panelItem.code` and `panelItem.caption` set.
- [ ] `selected.request` wired if click handling is needed.
- [ ] `isCollapsed` matches the parent panel's `panelDisplayMode`.
