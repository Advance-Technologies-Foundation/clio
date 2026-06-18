# How to Add a Toggle (`crt.Toggle`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.Toggle` into a mobile page schema.
> Renders a Boolean on/off slide toggle — the default Boolean input for mobile pages.

## Metadata
- **Category**: fields
- **Container**: no
- **Parent types**: any layout container (e.g. `crt.GridContainer`, `crt.DetailsGrid`)
- **Typical children**: none

---

## 1. Mental model
`crt.Toggle` is the canonical Boolean input for mobile pages. When the mobile designer
auto-generates a form for a Boolean column it inserts `crt.Toggle`, not `crt.Checkbox`.
The user flips the toggle on or off; the bound `control` attribute is updated immediately.
Use `readonly` to lock the toggle without hiding it.

---

## 2. Clio operation
```jsonc
{
  "operation": "insert",
  "name": "ActiveToggle",
  "values": {
    "type": "crt.Toggle",
    "label": "$Resources.Strings.ActiveToggle_label",
    "control": "$PDS_IsActive"
  },
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0
}
```

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.Toggle` are in
`ComponentRegistry.json` under `componentType: "crt.Toggle"`.

Additional runtime properties (not Angular `@Input` — applied via schema binding):

| Property | Type | Description |
|---|---|---|
| `visible` | `string` | Visibility binding. Bind to a boolean expression, e.g. `'$CardState \| crt.IsEqual : \'edit\''`, to show/hide the component conditionally. |

---

## 4. Copy-paste minimal example
```jsonc
{
  "operation": "insert",
  "name": "ActiveToggle",
  "values": {
    "type": "crt.Toggle",
    "label": "$Resources.Strings.ActiveToggle_label",
    "control": "$PDS_IsActive"
  },
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0
}
```

---

## 5. Common pitfalls
- **`control` must bind to a Boolean attribute** — binding `control` to a non-Boolean column (e.g. an integer or text) causes silent rendering issues where the toggle appears in an indeterminate state.
- **Prefer `crt.Toggle` over `crt.Checkbox` in mobile** — both accept Boolean values, but `crt.Toggle` is the platform-standard choice for mobile pages; `crt.Checkbox` should only be used when tristate or an inverted checkbox visual is explicitly required.
- **`labelPosition: 'hidden'`** — hides the label entirely; use `'above'` or `'auto'` (default) for accessible forms.
