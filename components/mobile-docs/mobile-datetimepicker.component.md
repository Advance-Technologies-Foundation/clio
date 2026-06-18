# How to Add a DateTimePicker (`crt.DateTimePicker`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.DateTimePicker` into a mobile page schema.
> Renders a date and/or time picker for Date, Time, and DateTime data-type fields on a mobile page.

## Metadata
- **Category**: fields
- **Container**: no
- **Parent types**: any layout container (e.g. `crt.GridContainer`, `crt.DetailsGrid`)
- **Typical children**: none

---

## 1. Mental model
`crt.DateTimePicker` is the standard date/time entry field for mobile pages. The
`pickerType` input selects which parts of the value the user can edit: `'date'` for
date-only fields, `'time'` for time-only, and `'datetime'` for combined date+time fields.
The displayed format follows the user's locale settings automatically.

---

## 2. Clio operation
```jsonc
{
  "operation": "insert",
  "name": "BirthDatePicker",
  "values": {
    "type": "crt.DateTimePicker",
    "label": "$Resources.Strings.BirthDatePicker_label",
    "control": "$PDS_BirthDate",
    "pickerType": "date"
  },
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0
}
```

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.DateTimePicker` are in
`ComponentRegistry.json` under `componentType: "crt.DateTimePicker"`.

Additional runtime properties (not Angular `@Input` — applied via schema binding):

| Property | Type | Description |
|---|---|---|
| `visible` | `string` | Visibility binding. Bind to a boolean expression, e.g. `'$CardState \| crt.IsEqual : \'edit\''`, to show/hide the component conditionally. |

---

## 4. Copy-paste minimal example
```jsonc
{
  "operation": "insert",
  "name": "BirthDatePicker",
  "values": {
    "type": "crt.DateTimePicker",
    "label": "$Resources.Strings.BirthDatePicker_label",
    "control": "$PDS_BirthDate",
    "pickerType": "date"
  },
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0
}
```

---

## 5. Common pitfalls
- **`pickerType` mismatch** — using `'date'` for a DateTime column truncates the time component silently; match `pickerType` to the actual column data-value type (`DataValueType.Date` → `'date'`, `DataValueType.Time` → `'time'`, `DataValueType.DateTime` → `'datetime'`).
- **Missing `control` binding** — without `control` the field renders but does not read from or write to the page data source.
- **Locale formatting** — the display format is user-locale-driven; do not hard-code format strings in `placeholder` — use the localizable strings mechanism instead.
