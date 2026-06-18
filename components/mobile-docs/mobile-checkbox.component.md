# How to Add a Checkbox (`crt.Checkbox`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.Checkbox` into a mobile page schema.
> Renders a Boolean checkbox on a mobile page; prefer `crt.Toggle` for standard Boolean fields.

## Metadata
- **Category**: fields
- **Container**: no
- **Parent types**: any layout container (e.g. `crt.GridContainer`, `crt.DetailsGrid`)
- **Typical children**: none

---

## 1. Mental model
`crt.Checkbox` is a three-state-capable Boolean input. For ordinary Boolean data-type
fields in mobile pages the mobile designer maps them to `crt.Toggle` automatically —
reserve `crt.Checkbox` only when you explicitly need a visual checkbox, tristate
(`indeterminate`), or an inverted value (`inversed`). Bind the displayed value through
`control` so the field stays in sync with the page data source.

---

## 2. Clio operation
```jsonc
{
  "operation": "insert",
  "name": "ActiveCheckbox",
  "values": {
    "type": "crt.Checkbox",
    "label": "$Resources.Strings.ActiveCheckbox_label",
    "control": "$PDS_Active"
  },
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0
}
```

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.Checkbox` are in
`ComponentRegistry.json` under `componentType: "crt.Checkbox"`.

Additional runtime properties (not Angular `@Input` — applied via schema binding):

| Property | Type | Description |
|---|---|---|
| `visible` | `string` | Visibility binding. Bind to a boolean expression, e.g. `'$CardState \| crt.IsEqual : \'edit\''`, to show/hide the component conditionally. |

---

## 4. Copy-paste minimal example
```jsonc
{
  "operation": "insert",
  "name": "ActiveCheckbox",
  "values": {
    "type": "crt.Checkbox",
    "label": "$Resources.Strings.ActiveCheckbox_label",
    "control": "$PDS_Active"
  },
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0
}
```

---

## 5. Common pitfalls
- **Prefer `crt.Toggle` for Boolean fields in mobile** — the mobile designer maps Boolean data types to `crt.Toggle` by default; inserting `crt.Checkbox` for a plain Boolean field may look inconsistent with other fields on the page.
- **Missing `control` binding** — without `control` the checkbox renders but does not read from or write to the page data source; always provide a `$PDS_<ColumnName>` reference.
- **`inversed` vs. `readonly`** — `inversed` flips the displayed value logically (true renders as unchecked); do not confuse it with `readonly`, which locks the control from user interaction.
