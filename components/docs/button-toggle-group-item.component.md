# How to Add a Button Toggle Group Item (`crt.ButtonToggleGroupItem`) to a Freedom UI Page

> Audience: code agent inserting `crt.ButtonToggleGroupItem` into a Creatio Freedom UI page schema.
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
  "name": "ViewToggleButtonGroup",
  "parentName": "ToolbarContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ButtonToggleGroup",
    "items": [
      {
        "type": "crt.ButtonToggleGroupItem",
        "value": "list",
        "displayValue": "#ResourceString(ViewToggle_List)#",
        "icon": "list-view-icon",
        "iconPosition": "left-icon",
        "size": "medium"
      },
      {
        "type": "crt.ButtonToggleGroupItem",
        "value": "card",
        "displayValue": "#ResourceString(ViewToggle_Card)#",
        "icon": "card-view-icon",
        "iconPosition": "left-icon",
        "size": "medium"
      }
    ],
    "toggleItemClicked": { "request": "crt.ViewToggleClickedRequest" }
  }
}
```

### 2.2 (Optional) Handle the toggleItemClicked output

```jsonc
{
  "request": "crt.ViewToggleClickedRequest",
  "handler": async (request, next) => {
    // request.value — the clicked item's value string
    return next?.handle(request);
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

// CrtMenuItemViewElementConfig — inline menu item (when menuButtonsMode: true)
interface CrtMenuItemViewElementConfig {
  type: "crt.MenuItem";
  caption: string;
  icon?: string;
  value?: string;
  selected?: boolean;
  handleItemClick?: () => void;
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — toggle group with two items
{
  "operation": "insert",
  "name": "ListViewToggle",
  "parentName": "SectionToolbar",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ButtonToggleGroup",
    "items": [
      {
        "type": "crt.ButtonToggleGroupItem",
        "value": "list",
        "displayValue": "#ResourceString(ListViewToggle_list)#",
        "icon": "list-view-icon",
        "iconPosition": "only-icon",
        "size": "medium",
        "pressed": true
      },
      {
        "type": "crt.ButtonToggleGroupItem",
        "value": "card",
        "displayValue": "#ResourceString(ListViewToggle_card)#",
        "icon": "card-view-icon",
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

1. **Items are inline objects, not separate view element inserts** — do not use separate `insert` operations for each `crt.ButtonToggleGroupItem`; embed them directly in the parent group's `items` array.
2. **`value` is required** — the `toggleItemClicked` event payload includes `value`; without it, the handler cannot identify which item was clicked.
3. **`pressed` for active state** — set `pressed: true` on the initially active item to visually indicate the selected state.
4. **`iconPosition: "only-icon"` hides `displayValue`** — use `"left-icon"` or `"right-icon"` if the label should be visible alongside the icon.
5. **`menuButtonsMode: true`** — activates dropdown menu behavior on the item; pair with `menuItems` array for this mode.
6. **`size: "none"` or `"default"`** — these values use the medium size at runtime; prefer explicit size keywords (`"small"`, `"medium"`, `"large"`).

---

## 8. Quick checklist

- [ ] Items are embedded in the parent `crt.ButtonToggleGroup`'s `items` array.
- [ ] Each item has `type: "crt.ButtonToggleGroupItem"` and a unique `value`.
- [ ] `displayValue` set (use `#ResourceString(<key>)#` for localizable labels).
- [ ] `pressed` set appropriately to mark the initial selection.
- [ ] `toggleItemClicked` on the parent group is wired to a request if selection changes need handling.
- [ ] `iconPosition` chosen to match display requirements.
