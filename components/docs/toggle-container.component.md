# How to Add a Toggle Container (`crt.ToggleContainer`) to a Freedom UI Page

> Audience: code agent inserting `crt.ToggleContainer` into a Creatio Freedom UI page schema.
> `crt.ToggleContainer` is a **tab-switching container** that shows one child `crt.ToggleContainerItem`
> at a time based on `selectedTabIndex`; it coordinates multiple sections by lazy-rendering only the
> active item.

## Metadata

- **Category**: containers
- **Container**: yes (children go into the `items` slot)
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.HeaderContainer`
- **Typical children**: `crt.ToggleContainerItem`

---

## 1. Mental model — the 2-3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.ToggleContainer"` and child `crt.ToggleContainerItem` inserts. **Always present.** |
| 2 | `viewModelConfigDiff` | *(only if binding `selectedTabIndex` or `selectedTab` to an attribute)* |
| 3 | `handlers` | *(only if `selectedTabIndexChange` / `selectedTabChange` outputs need custom logic)* |

### 1.1 Naming convention

```
ToggleContainer_<id>      // container view element name
ToggleContainerItem_<id>  // each child item view element name
$ToggleContainer_<id>_selectedTabIndex  // $-prefix attribute for the active tab index
```

---

## 2. Step-by-step recipe

### 2.1 Insert the toggle container and its items (`viewConfigDiff` entries)

```jsonc
// Insert the container
{
  "operation": "insert",
  "name": "ToggleContainer_main",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ToggleContainer",
    "selectedTabIndex": 0,
    "preserveContent": true,
    "slidingAnimation": false,
    "items": [],
    "visible": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}

// Insert each child item (must use parentName + propertyName: "items")
{
  "operation": "insert",
  "name": "ToggleContainerItem_first",
  "parentName": "ToggleContainer_main",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ToggleContainerItem",
    "items": [],
    "visible": true
  }
}
```

### 2.2 (Optional) Handle tab-change in `handlers`

```jsonc
{
  "request": "crt.MyTabChangedRequest",
  "handler": async (request, next) => {
    // tabIndex available via request context
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ToggleContainer` are in `ComponentRegistry.json` under `componentType: "crt.ToggleContainer"`. This
guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// selectedTab — used when identifying tabs by name rather than index
interface TabValue {
  value: string;  // must match the child ToggleContainerItem's name
}

// items collection — only crt.ToggleContainerItem is valid here
type ContainerViewElements = Array<{ type: "crt.ToggleContainerItem"; name?: string; /* + item props */ }>;
```

`items` uses a lazy `contentSlot` — the `crt.ToggleContainerItem` children are lazy-rendered; the active
item is mounted on first selection and hidden (or destroyed if `preserveContent: false`) on deselection.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff — container
{
  "operation": "insert",
  "name": "ToggleContainer_main",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ToggleContainer",
    "selectedTabIndex": 0,
    "preserveContent": true,
    "slidingAnimation": false,
    "items": [],
    "visible": true
  }
}

// viewConfigDiff — first child
{
  "operation": "insert",
  "name": "ToggleContainerItem_details",
  "parentName": "ToggleContainer_main",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ToggleContainerItem",
    "items": [],
    "visible": true
  }
}

// viewConfigDiff — second child
{
  "operation": "insert",
  "name": "ToggleContainerItem_history",
  "parentName": "ToggleContainer_main",
  "propertyName": "items",
  "index": 1,
  "values": {
    "type": "crt.ToggleContainerItem",
    "items": [],
    "visible": true
  }
}
```

---

## 6. Driving the container from page state

```jsonc
// viewModelConfigDiff
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "ActiveTabIndex": { "value": 0 }
    }
  }
}

// viewConfigDiff.values on ToggleContainer
"selectedTabIndex": "$ActiveTabIndex"

// handlers — respond to tab changes
{
  "request": "crt.HandleViewModelAttributeChangeRequest",
  "handler": async (request, next) => {
    if (request.attributePath === "ActiveTabIndex") {
      // react to tab change
    }
    return next?.handle(request);
  }
}
```

`selectedTabIndex` and `selectedTab` are both `propertyBindable`; use either for two-way tab control.
`selectedTabIndexChange` / `selectedTabChange` fire when the user closes a child item (via `closeContainer`).

---

## 7. Common pitfalls

1. **Children must be `crt.ToggleContainerItem`.** Inserting any other type into the `items` slot produces no visible output.
2. **`items: []` must be present.** Without the array the content-slot projection fails.
3. **`selectedTabIndex: -1`** collapses all panels (all items hidden). This is the intended "none selected" state.
4. **`preserveContent: false`** destroys and re-creates child content on every tab switch, which may cause state loss; prefer `true` unless intentional.
5. **`slidingAnimation: true` with `preserveContent: false`** — the animation expects the old item's element to still exist during the slide; combining both may cause visual glitches.
6. **Mixing `selectedTab` and `selectedTabIndex`** — both inputs drive the active item; setting both simultaneously causes a double-apply. Use one or the other.
7. **Index bounds** — setting `selectedTabIndex` to a value >= the number of children auto-resets it to `0`; do not rely on out-of-bounds values to hide all items.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.ToggleContainer"`, unique `name`, valid `parentName`.
- [ ] `items: []` present in `values`.
- [ ] `selectedTabIndex` set to a valid zero-based index (or `-1` for no selection).
- [ ] All children are `crt.ToggleContainerItem` with their own `insert` ops using `parentName` pointing to the container.
- [ ] Each child has `items: []` in its own `values`.
- [ ] If `selectedTabIndex` is attribute-bound, the attribute is declared in `viewModelConfigDiff`.
- [ ] If `selectedTabIndexChange` needs custom logic, a matching handler exists in `handlers`.
