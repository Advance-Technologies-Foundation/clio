# How to Add a Toggle Container Item (`crt.ToggleContainerItem`) to a Freedom UI Page

> Audience: code agent inserting `crt.ToggleContainerItem` into a Creatio Freedom UI page schema.
> `crt.ToggleContainerItem` is a **child panel** of `crt.ToggleContainer`; it holds content in its
> `items` slot and an optional header toolbar in its `tools` slot.

## Metadata

- **Category**: containers
- **Container**: yes (children go into the `items` and `tools` slots)
- **Parent types**: `crt.ToggleContainer` (must be a direct child; cannot be placed anywhere else)
- **Typical children**: any view element in `items`; toggle/action buttons in `tools`

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.ToggleContainerItem"` as a child of a `crt.ToggleContainer`. **Always present.** |

`crt.ToggleContainerItem` is **view-only** — no datasource, no attributes needed. All you do is nest it
inside a `crt.ToggleContainer` and populate its `items` (content) and `tools` (header actions) slots.

### 1.1 Naming convention

```
ToggleContainerItem_<id>     // view element name; must be unique across the page
```

---

## 2. Step-by-step recipe

### 2.1 Insert the item inside a `crt.ToggleContainer` (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "ToggleContainerItem_details",
  "parentName": "ToggleContainer_main",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ToggleContainerItem",
    "allowToggleClose": true,
    "backgroundColor": "primary-contrast-500",
    "isToggleTabHeaderVisible": true,
    "items": [],
    "visible": true
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ToggleContainerItem` are in `ComponentRegistry.json` under `componentType: "crt.ToggleContainerItem"`.
This guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// backgroundColor — ThemeColor enum values (canonical from PackageStore)
type ThemeColor =
  | 'primary-contrast-500'   // default white-ish background
  | 'primary-100'
  | 'primary-200'
  | 'primary-300'
  // ... see ThemeColor enum in util/common

// items and tools — standard content slots
type ContainerViewElements = Array<{ type: string; name?: string; /* + element-specific props */ }>;
```

When `backgroundColor` is `null` or absent, the component falls back to `ThemeColor.PrimaryContrast500`.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff — ToggleContainer wrapper
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
    "items": [],
    "visible": true
  }
}

// viewConfigDiff — first item
{
  "operation": "insert",
  "name": "ToggleContainerItem_details",
  "parentName": "ToggleContainer_main",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ToggleContainerItem",
    "allowToggleClose": true,
    "backgroundColor": "primary-contrast-500",
    "items": [],
    "visible": true
  }
}
```

---

## 6. Driving from page state

`crt.ToggleContainerItem` itself has no `propertyBindable` inputs. Control which item is visible by
binding `selectedTabIndex` on the parent `crt.ToggleContainer`.

```jsonc
// viewConfigDiff.values on parent ToggleContainer
"selectedTabIndex": "$ActiveTabIndex"
```

---

## 7. Common pitfalls

1. **`crt.ToggleContainerItem` must be a direct child of `crt.ToggleContainer`.** Placing it inside a `crt.FlexContainer` or other container produces no tab-switching behavior.
2. **`items: []` must be present.** The content slot projection fails without the array in `values`.
3. **`isToggleTabHeaderVisible: true`** only shows the header bar when the `tools` slot has children; if `tools` is empty the bar is hidden regardless.
4. **`allowToggleClose: false`** suppresses the close action on the header, which also suppresses `closeContainer` emissions; the parent's `selectedTabIndexChange` with `-1` will not fire from user interaction.
5. **`backgroundColor` accepts `ThemeColor` string literals only.** Passing a raw hex color or CSS variable is ignored; the component uses `getColorByTheme()` to resolve the CSS variable.
6. **Binding activation** — when `preserveContent: true` on the parent, the `BindingActivator` pauses change detection for hidden items; items deactivated while hidden automatically re-activate on show.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.ToggleContainerItem"`, unique `name`.
- [ ] `parentName` points to a `crt.ToggleContainer`.
- [ ] `propertyName: "items"`.
- [ ] `items: []` present in `values`.
- [ ] `backgroundColor` is a valid `ThemeColor` string (or omitted to use the default).
- [ ] If the item can be closed by the user, `allowToggleClose: true` and the parent has a `selectedTabIndexChange` handler.
