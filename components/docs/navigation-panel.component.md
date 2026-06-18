# How to Add a Navigation Panel (`crt.NavigationPanel`) to a Freedom UI Page

> Audience: code agent inserting a `crt.NavigationPanel` into a Creatio Freedom UI page schema.
>
> `crt.NavigationPanel` is a side-navigation shell: it renders a group selector button and a scrollable
> item list for the active group. All navigation data (groups, items) is pushed in via `dataSource` and
> loaded via the `loadData` output. It is typically placed inside the main shell container.

## Metadata

- **Category**: navigation
- **Container**: no
- **Parent types**: `crt.FlexContainer`, shell container (`ShellContainer`)
- **Typical children**: none

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.NavigationPanel"`. **Always present.** |
| 2 | `handlers` | A handler for `loadData` that populates the `dataSource` attribute; optionally handlers for `itemClicked`, `groupChanged`, `panelDisplayModeChanged`. |

`crt.NavigationPanel` is **view-only** — it owns no datasource directly. Populate `dataSource` from a
handler wired to `loadData`. Wire `itemClicked` to navigate and `groupChanged` to switch group context.

### 1.1 Naming convention

```
NavigationPanel_<id>           // view element name
$NavigationPanel_<id>_items    // attribute for the data source array (optional, if bound)
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "LeftNavigationPanel",
  "values": {
    "type": "crt.NavigationPanel",
    "stretch": true,
    "visibilityStrategyMode": "hide"
  },
  "parentName": "ShellContainer",
  "propertyName": "items",
  "index": 0
}
```

### 2.2 Wire `loadData` in `handlers`

```jsonc
{
  "request": "crt.HandleViewModelInitRequest",
  "handler": async (request, next) => {
    // populate the dataSource attribute
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.NavigationPanel` are in `ComponentRegistry.json` under `componentType: "crt.NavigationPanel"`. This
guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// NavigationPanelGroup — items for the dataSource array
interface NavigationPanelGroup {
  code: string;
  caption: string;
  items: NavigationPanelItem[];
  excludeFromSearch?: boolean;
}

// NavigationPanelItem — entries in each group
interface NavigationPanelItem {
  code: string;
  caption: string;
  searchValue?: string;
  iconUrl?: string;
  iconBackgroundColor?: string;
  selected?: boolean;
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — matches real MainShell usage
{
  "operation": "insert",
  "name": "LeftNavigationPanel",
  "values": {
    "type": "crt.NavigationPanel",
    "classes": ["remove-outside-horizontal-padding"],
    "stretch": true,
    "visibilityStrategyMode": "hide"
  },
  "parentName": "ShellContainer",
  "propertyName": "items",
  "index": 0
}
```

---

## 7. Common pitfalls

1. **Forgetting to wire `loadData`.** The panel fires `loadData` on `ngOnInit`; without a handler the `dataSource` attribute is never populated and the panel stays empty.
2. **`currentGroupCode` not matching any group code in `dataSource`.** The panel silently falls back to the first group; verify codes are consistent.
3. **`selectedItemCode` not updated after navigation.** Set it to the code of the currently open item to keep the selection highlight in sync with the page.
4. **`panelDisplayMode` set without wiring `panelDisplayModeChanged`.** The toggle button changes the mode internally, but if the attribute is not updated the mode resets on the next binding cycle.
5. **`openItemAsLink: true` without valid item URLs.** Items rendered as links require each `NavigationPanelItem` to have a resolvable URL; missing URLs produce non-navigable anchors.
6. **`usePanelIconBackground: null`.** The setter guards against `null` but the panel won't apply background colors until a truthy value is set; always pass `true` or `false` explicitly.

---

## 8. Quick checklist

- [ ] `insert` op with `type: "crt.NavigationPanel"`, unique `name`, valid `parentName`, `propertyName: "items"`.
- [ ] `loadData` wired to a handler that sets the `dataSource` attribute.
- [ ] `itemClicked` wired if item selection should trigger navigation or page actions.
- [ ] `groupChanged` wired if switching groups needs custom logic.
- [ ] `dataSource` attribute declared in `viewModelConfigDiff` with an initial empty array.
- [ ] If `panelDisplayMode` is bound, `panelDisplayModeChanged` is also wired to keep the attribute in sync.
