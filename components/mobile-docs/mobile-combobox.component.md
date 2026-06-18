# How to Add a ComboBox (`crt.ComboBox`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.ComboBox` into a mobile page schema.
> Renders a searchable lookup dropdown for Lookup and enumeration fields on a mobile page.

## Metadata
- **Category**: fields
- **Container**: no
- **Parent types**: any layout container (e.g. `crt.GridContainer`, `crt.DetailsGrid`)
- **Typical children**: none

---

## 1. Mental model
`crt.ComboBox` is the standard dropdown for Lookup and enumeration data-type columns on
mobile pages. It opens a modal list of options when tapped. The list is fetched from the
data source bound via `control`. When `isAddAllowed` is true an inline "Add" button lets
the user create a new lookup record without leaving the page.

---

## 2. Clio operation
```jsonc
{
  "operation": "insert",
  "name": "StatusComboBox",
  "values": {
    "type": "crt.ComboBox",
    "label": "$Resources.Strings.StatusComboBox_label",
    "control": "$PDS_Status"
  },
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0
}
```

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.ComboBox` are in
`ComponentRegistry.json` under `componentType: "crt.ComboBox"`.

Additional runtime properties (not Angular `@Input` — applied via schema binding):

| Property | Type | Description |
|---|---|---|
| `visible` | `string` | Visibility binding. Bind to a boolean expression, e.g. `'$CardState \| crt.IsEqual : \'edit\''`, to show/hide the component conditionally. |

---

## 4. Copy-paste minimal example
```jsonc
{
  "operation": "insert",
  "name": "StatusComboBox",
  "values": {
    "type": "crt.ComboBox",
    "label": "$Resources.Strings.StatusComboBox_label",
    "control": "$PDS_Status"
  },
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0
}
```

---

## 5. Common pitfalls
- **Wrong data-value type** — `crt.ComboBox` only works correctly with Lookup and enumeration columns; binding it to a text or integer column will cause unexpected behavior.
- **Missing `control` binding** — without `control` the field renders but shows no value and cannot save; always provide a `$PDS_<ColumnName>` reference.
- **`labelPosition: 'auto'`** — the default; the platform resolves the position based on available space. Override only when you need a fixed `'above'` or `'hidden'` layout.
