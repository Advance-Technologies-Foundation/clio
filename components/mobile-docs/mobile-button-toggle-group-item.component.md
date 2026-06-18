# How to Add a Button Toggle Group Item (`crt.ButtonToggleGroupItem`) to a Mobile Page

> Audience: code agent inserting `crt.ButtonToggleGroupItem` into a Creatio mobile Freedom UI page schema.
> A toggleable button that lives inside a `crt.ButtonToggleGroup`; represents a single selectable
> option with optional icon, badge, and dropdown menu.

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.ButtonToggleGroup` (via the `items` slot — the toggle group manages item rendering)
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | Items are injected into the parent `crt.ButtonToggleGroup`'s `items` array, **not** as separate `insert` ops. |

`crt.ButtonToggleGroupItem` is consumed by a `crt.ButtonToggleGroup` preprocessor — items are
provided directly in the parent's `items` array as inline objects rather than separate view
element inserts.

### 1.1 Naming convention

Items inside a `crt.ButtonToggleGroup.items` array are typically anonymous inline objects. If
inserted as standalone view elements, follow:

```
ButtonToggleGroupItem_<id>      // view element name
```

---

## 2. Step-by-step recipe

### 2.1 Insert items as part of the parent `crt.ButtonToggleGroup`

```jsonc
{
  "operation": "insert",
  "name": "ViewModeToggle",
  "parentName": "HeaderContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ButtonToggleGroup",
    "items": [
      {
        "type": "crt.ButtonToggleGroupItem",
        "value": "list",
        "displayValue": "#ResourceString(ViewMode_List)#",
        "icon": "crt-icon-list"
      },
      {
        "type": "crt.ButtonToggleGroupItem",
        "value": "grid",
        "displayValue": "#ResourceString(ViewMode_Grid)#",
        "icon": "crt-icon-grid"
      }
    ]
  }
}
```

### 2.2 Standalone insert (rare)

```jsonc
{
  "operation": "insert",
  "name": "ListViewItem",
  "parentName": "ViewModeToggle",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ButtonToggleGroupItem",
    "value": "list",
    "displayValue": "List",
    "icon": "crt-icon-list"
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ButtonToggleGroupItem` are in `ComponentRegistry.json` under
`componentType: "crt.ButtonToggleGroupItem"`. This guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// BadgeConfig — badge color/offset pair
interface BadgeConfig {
  color: "primary" | "accent" | "warn";  // required
  offset: number;                         // required
}

// BadgeOptions — controls badge dot visibility
interface BadgeOptions {
  visible: boolean;  // required
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — toggle group with two mobile items
{
  "operation": "insert",
  "name": "ViewModeToggle",
  "parentName": "Toolbar",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ButtonToggleGroup",
    "items": [
      {
        "type": "crt.ButtonToggleGroupItem",
        "value": "list",
        "displayValue": "#ResourceString(ViewModeToggle_list)#",
        "icon": "crt-icon-list",
        "iconPosition": "only-icon",
        "size": "medium",
        "pressed": true
      },
      {
        "type": "crt.ButtonToggleGroupItem",
        "value": "card",
        "displayValue": "#ResourceString(ViewModeToggle_card)#",
        "icon": "crt-icon-grid",
        "iconPosition": "only-icon",
        "size": "medium",
        "pressed": false
      }
    ]
  }
}
```

---

## 7. Common pitfalls

1. **Always place inside `crt.ButtonToggleGroup.items`** — rendering a `crt.ButtonToggleGroupItem` standalone is not supported; it requires a parent group to manage pressed state and selection.
2. **`value` is required** — the `toggleItemClicked` event payload includes `value`; without it the handler cannot identify which item was clicked.
3. **`pressed` for active state** — set `pressed: true` on the initially active item to visually indicate the selected state on first render.
4. **`iconPosition: "only-icon"` hides `displayValue`** — use `"left-icon"` or `"right-icon"` if the label should be visible alongside the icon.
5. **`menuButtonsMode: true` requires `menuItems`** — activates dropdown menu behavior; pair with a `menuItems` array for this mode.

---

## 8. Quick checklist

- [ ] Items are embedded in the parent `crt.ButtonToggleGroup`'s `items` array.
- [ ] Each item has `type: "crt.ButtonToggleGroupItem"` and a unique `value`.
- [ ] `displayValue` set (use `#ResourceString(<key>)#` for localizable labels).
- [ ] `pressed` set appropriately to mark the initial selection.
- [ ] `iconPosition` chosen to match display requirements.
