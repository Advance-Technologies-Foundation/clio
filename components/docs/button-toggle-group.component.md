# How to Add a Button Toggle Group (`crt.ButtonToggleGroup`) to a Freedom UI Page

> Audience: code agent inserting a `crt.ButtonToggleGroup` into a Creatio Freedom UI page schema.
>
> The component renders a row (or column) of mutually-exclusive toggle buttons. The
> dominant Creatio pattern links it to a sibling `crt.TabPanel` via `for`, so toggling a
> button switches the panel's selected tab without any handler wiring. A standalone form
> (inline `items` + `value` binding) also exists for general-purpose use.

## Metadata

- **Category**: interactive
- **Container**: yes
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`
- **Typical children**: items populated either inline (Pattern 2) or from a linked `crt.TabPanel` via `for: "<TabPanelName>"` (Pattern 1, canonical)

---

## 1. Mental model — two patterns

### Pattern 1 (canonical): linked to a sibling TabPanel

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.ButtonToggleGroup"` and `for: "<TabPanelName>"`. |

A platform preprocessor reads `for`, finds the sibling `crt.TabPanel` (typically `mode: "toggle"`), and populates the toggle's `items` from that panel's tabs. Switching a toggle switches the panel's selected tab. No `value` binding, no `viewModelConfigDiff` attribute, no handler required.

### Pattern 2 (standalone): inline items

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.ButtonToggleGroup"`, inline `items`, `value` binding. |
| 2 | `viewModelConfigDiff` (optional) | A scalar attribute for the selected toggle value. |

Used when the toggle group is not bound to tabs.

### 1.1 Naming convention

```
ButtonToggleGroup_<id>             // view element name (standalone form)
<TabPanelName>ToggleButtons        // common convention when linked to a TabPanel
ButtonToggleGroup_<id>_value       // page attribute holding the selected ToggleValue (standalone form only)
```

---

## 2. Step-by-step recipe — Pattern 1 (canonical, TabPanel-linked)

### 2.1 Insert the toggle group (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "LegacySidePanelItemsToggleButtons",
  "parentName": "LegacySidePanelItemsContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "for": "LegacySidePanelItemsTabPanel",
    "type": "crt.ButtonToggleGroup",
    "allowUntoggle": false,
    "direction": "column",
    "size": "extra-large",
    "iconSize": "extra-large",
    "contentAlign": "center",
    "gap": "none",
    "tooltipPosition": {
      "originX": "end",
      "originY": "center",
      "overlayX": "start",
      "overlayY": "center"
    }
  }
}
```

The corresponding `crt.TabPanel` (here `LegacySidePanelItemsTabPanel`) is typically `mode: "toggle"`. Tabs inserted into the panel become the toggle group's items automatically. See crt.TabPanel guide for the panel side.

### 2.2 (Optional) `tooltipPosition`

CDK overlay-position for the per-toggle tooltip. Common values for column-direction (side-panel) toggles:

```jsonc
"tooltipPosition": {
  "originX": "end", "originY": "center",
  "overlayX": "start", "overlayY": "center"
}
```

Omit for default tooltip placement.

---

## 3. Step-by-step recipe — Pattern 2 (standalone, inline items)

### 3.1 (Optional) Declare the value attribute (`viewModelConfigDiff` entry)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "ButtonToggleGroup_xkp4r_value": { "value": { "value": "tab1" } }
    }
  }
}
```

`ToggleValue` is `{ value: string, displayValue?: string }` (compatible with `LookupValue`).

### 3.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "ButtonToggleGroup_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ButtonToggleGroup",
    "items": [
      { "value": "tab1", "displayValue": "#ResourceString(Tab1_Caption)#" },
      { "value": "tab2", "displayValue": "#ResourceString(Tab2_Caption)#" },
      { "value": "tab3", "displayValue": "#ResourceString(Tab3_Caption)#" }
    ],
    "value": "$ButtonToggleGroup_xkp4r_value",
    "size": "medium",
    "iconSize": "large",
    "allowUntoggle": true,
    "direction": "row",
    "gap": "small",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 2, "rowSpan": 1 }
  }
}
```

The `valueChange` output (the component's sole `@CrtOutput`) fires when the user toggles a button. For Pattern 2 you may wire it to a handler if you need a side-effect beyond updating the bound attribute; the platform auto-binds the value attribute regardless.

---

## 4. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ButtonToggleGroup` are in `ComponentRegistry.json` under `componentType: "crt.ButtonToggleGroup"`. This guide covers
only the assembly mechanics — what to put where in `viewConfigDiff` /
`viewModelConfigDiff` / `modelConfigDiff`.

---

## 5. `ToggleValue` shape

```ts
interface ToggleValue {
  value: string;          // unique id of the toggle (this is what gets stored)
  displayValue?: string;  // localized caption
  icon?: string;          // optional icon name
  iconPosition?: "left-icon" | "right-icon" | "only-icon" | "only-text";
}
```

---

## 6. Copy-paste minimal example

### Pattern 1: linked to a TabPanel (most common)

```jsonc
// viewConfigDiff entries — toggle group linked to its TabPanel sibling
[
  {
    "operation": "insert",
    "name": "ViewModeTabPanel",
    "parentName": "MainContainer",
    "propertyName": "items",
    "index": 0,
    "values": {
      "type": "crt.TabPanel",
      "mode": "toggle",
      "styleType": "default"
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
      "iconSize": "medium",
      "contentAlign": "center",
      "gap": "small"
    }
  }
  // ... plus crt.TabContainer inserts whose tabs become the toggle items
]
```

### Pattern 2: standalone (inline items)

```jsonc
// viewConfigDiff entry
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
    "allowUntoggle": false,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

---

## 7. Common pitfalls

1. **Setting both `for` and `items`** — `for` populates `items` from the linked TabPanel. Setting both creates ambiguity; the preprocessor's behavior is not guaranteed. Pick one pattern.
2. **`for` referencing a non-existent or non-toggle TabPanel** — the toggle group renders empty. Verify the TabPanel `name` exists in the same schema and that its `mode` makes the linkage meaningful (usually `"toggle"`).
3. **Passing items as raw strings** — items must be objects with `value` (and ideally `displayValue`). Passing `["tab1", "tab2"]` results in empty toggle captions.
4. **`value` bound to a primitive instead of a `ToggleValue`** — the active toggle highlights by matching `value.value === item.value`. Storing `"tab1"` in the attribute leaves nothing selected.
5. **`allowUntoggle: true` on a TabPanel-linked group** — re-clicking an active toggle clears the bound tab, leaving the panel unselected. TabPanel-linked patterns always set `allowUntoggle: false`.
6. **`direction: "column"` without enough `rowSpan`** — vertical layouts need `rowSpan ≥ items.length`; otherwise the toggles overflow the layout cell.
7. **`size: "default"` or `size: "none"`** — both coerce to `"medium"` internally (gap defaults to `"small"`). Pick a concrete size.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.ButtonToggleGroup"`, unique `name`, valid `parentName`, `propertyName: "items"`, `index`.
- [ ] For TabPanel-linked groups (canonical): `for: "<TabPanelName>"` set; `items`/`value` omitted; `allowUntoggle: false`.
- [ ] For standalone groups: `items` is a `ToggleValue[]` (each with `value` and `displayValue`); `value` either bound to a `$attr` (declared in `viewModelConfigDiff`) or set to a literal `ToggleValue`.
- [ ] `size`, `direction`, `gap` use allowed literals.
- [ ] `tooltipPosition` set when `direction: "column"` for proper tooltip placement.
- [ ] `layoutConfig` set when the parent is a grid container; omitted when the parent is a flex container.
