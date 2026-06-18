# How to Add a Slider (`crt.Slider`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.Slider` into a mobile page schema.
> Renders a horizontal numeric range slider for bounded numeric fields on a mobile page.

## Metadata
- **Category**: fields
- **Container**: no
- **Parent types**: any layout container (e.g. `crt.GridContainer`, `crt.DetailsGrid`)
- **Typical children**: none

---

## 1. Mental model
`crt.Slider` lets the user pick a numeric value by dragging a thumb along a horizontal
track between `minValue` and `maxValue`. The thumb snaps to `step` increments. The
current value is written to the `control` bound attribute and can be seeded with an
initial value via the `value` input. `color` accepts a CSS color token to tint the
filled track.

---

## 2. Clio operation
```jsonc
{
  "operation": "insert",
  "name": "PrioritySlider",
  "values": {
    "type": "crt.Slider",
    "label": "$Resources.Strings.PrioritySlider_label",
    "control": "$PDS_Priority",
    "minValue": 0,
    "maxValue": 10,
    "step": 1
  },
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0
}
```

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.Slider` are in
`ComponentRegistry.json` under `componentType: "crt.Slider"`.

Additional runtime properties (not Angular `@Input` — applied via schema binding):

| Property | Type | Description |
|---|---|---|
| `visible` | `string` | Visibility binding. Bind to a boolean expression, e.g. `'$CardState \| crt.IsEqual : \'edit\''`, to show/hide the component conditionally. |
| `labelPosition` | `string` | Label placement relative to the slider. Values: `above`, `auto`, `hidden`. Default: `auto`. |

---

## 4. Copy-paste minimal example
```jsonc
{
  "operation": "insert",
  "name": "PrioritySlider",
  "values": {
    "type": "crt.Slider",
    "label": "$Resources.Strings.PrioritySlider_label",
    "control": "$PDS_Priority",
    "minValue": 0,
    "maxValue": 10,
    "step": 1
  },
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0
}
```

---

## 5. Common pitfalls
- **`step` must evenly divide the range** — if `(maxValue - minValue) % step !== 0` the thumb can get stuck before reaching `maxValue`; always ensure the step divides the range exactly.
- **Missing `minValue` / `maxValue`** — both are required; leaving either `null` causes the designer-preview to show the thumb at position 0 and the runtime slider to behave unpredictably.
- **`control` vs. `value`** — `control` binds a page data-source attribute (reads and writes through the form model), while `value` is a plain scalar default; use `control` for persisted fields and `value` only for non-persisted display-only scenarios.
