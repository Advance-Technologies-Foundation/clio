# How to Add a Button Toggle Group (`crt.ButtonToggleGroup`) to a Mobile Page

> Audience: code agent inserting `crt.ButtonToggleGroup` into a Creatio mobile Freedom UI page schema.
>
> The component renders a row (or column) of mutually-exclusive toggle buttons. The dominant
> Creatio pattern links it to a sibling `crt.TabPanel` via `for`, so toggling a button switches
> the panel's selected tab without any handler wiring. A standalone form (inline `items` + `value`
> binding) also exists for general-purpose use.

## Metadata

- **Category**: interactive
- **Container**: yes
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`
- **Typical children**: `crt.ButtonToggleGroupItem` (via `items` array, or auto-populated from a linked `crt.TabPanel`)

---

## 1. Mental model — two patterns

### Pattern 1 (canonical): linked to a sibling TabPanel

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.ButtonToggleGroup"` and `for: "<TabPanelName>"`. |

A platform preprocessor reads `for`, finds the sibling `crt.TabPanel` (`mode: "toggle"`), and
populates the toggle's `items` from that panel's tabs. Switching a toggle switches the panel's
selected tab. No `value` binding, no `viewModelConfigDiff` attribute, no handler required.

### Pattern 2 (standalone): inline items

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.ButtonToggleGroup"`, inline `items`, `value` binding. |
| 2 | `viewModelConfigDiff` (optional) | A scalar attribute for the selected toggle value. |

### 1.1 Naming convention

```
ButtonToggleGroup_<id>             // view element name (standalone form)
<TabPanelName>ToggleButtons        // common convention when linked to a TabPanel
ButtonToggleGroup_<id>_value       // page attribute holding the selected ToggleValue (standalone only)
```

---

## 2. Step-by-step recipe — Pattern 2 (standalone, inline items)

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "ViewModeToggle",
  "parentName": "HeaderContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ButtonToggleGroup",
    "direction": "row",
    "items": [],
    "valueChange": { "request": "crt.UpdateDataRequest" }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ButtonToggleGroup` are in `ComponentRegistry.json` under `componentType: "crt.ButtonToggleGroup"`.
This guide covers only the assembly mechanics.

---

## 4. `ToggleValue` shape

```ts
interface ToggleValue {
  value: string;          // unique id of the toggle (this is what gets stored)
  displayValue?: string;  // localized caption
  icon?: string;          // optional icon name
  iconPosition?: "left-icon" | "right-icon" | "only-icon" | "only-text";
}
```

---

## 5. Copy-paste minimal example

### Pattern 1: linked to a TabPanel (canonical)

```jsonc
[
  {
    "operation": "insert",
    "name": "ViewModeTabPanel",
    "parentName": "MainContainer",
    "propertyName": "items",
    "index": 0,
    "values": {
      "type": "crt.TabPanel",
      "mode": "toggle"
    }
  },
  {
    "operation": "insert",
    "name": "ViewModeToggleButtons",
    "parentName": "MainContainer",
    "propertyName": "items",
    "index": 1,
    "values": {
      "for": "ViewModeTabPanel",
      "type": "crt.ButtonToggleGroup",
      "allowUntoggle": false,
      "direction": "row",
      "size": "medium",
      "gap": "small"
    }
  }
]
```

### Pattern 2: standalone (inline items)

```jsonc
{
  "operation": "insert",
  "name": "ViewModeToggle",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ButtonToggleGroup",
    "items": [
      { "value": "list", "displayValue": "#ResourceString(ViewMode_List)#", "icon": "list-view" },
      { "value": "grid", "displayValue": "#ResourceString(ViewMode_Grid)#", "icon": "grid-view" }
    ],
    "value": "$ViewModeToggle_value",
    "size": "medium",
    "allowUntoggle": false
  }
}
```

---

## 7. Common pitfalls

1. **Setting both `for` and `items`** — `for` populates `items` from the linked TabPanel. Setting both creates ambiguity; pick one pattern.
2. **`for` referencing a non-existent TabPanel** — the toggle group renders empty. Verify the TabPanel `name` exists in the same schema.
3. **`value` bound to a primitive instead of a `ToggleValue`** — the active toggle highlights by matching `value.value === item.value`. Storing `"tab1"` in the attribute leaves nothing selected.
4. **`allowUntoggle: true` on a TabPanel-linked group** — re-clicking an active toggle clears the bound tab, leaving the panel unselected. Always set `allowUntoggle: false` for TabPanel-linked patterns.
5. **Passing items as raw strings** — items must be objects with `value` (and ideally `displayValue`). Passing `["tab1"]` results in empty toggle captions.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.ButtonToggleGroup"`, unique `name`, valid `parentName`, `propertyName: "items"`, `index`.
- [ ] For TabPanel-linked groups (canonical): `for: "<TabPanelName>"` set; `items`/`value` omitted; `allowUntoggle: false`.
- [ ] For standalone groups: `items` is a `ToggleValue[]` (each with `value` and `displayValue`); `value` bound to `$attr` or set to a literal `ToggleValue`.
- [ ] `size`, `direction`, `gap` use allowed literals.
