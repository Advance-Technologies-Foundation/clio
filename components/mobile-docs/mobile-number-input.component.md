# How to Add a Number Input (`crt.NumberInput`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.NumberInput` into a Creatio mobile page schema.
>
> A numeric text input with configurable decimal precision, bound to a page attribute that
> holds an integer or floating-point value.

## Metadata

- **Category**: fields
- **Container**: no
- **Parent types**: `crt.GridContainer` items, `crt.FlexContainer` items, `crt.TabContainer` items
- **Typical children**: none

---

## 1. Mental model — the 2-3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.NumberInput"`, `label`, `control: "$<attr>"`. |
| 2 | `viewModelConfigDiff` | A `merge` op declaring the page attribute bound to an entity column. |
| 3 | `modelConfigDiff` (only if the column is not yet loaded) | A `merge` op registering the column in the data source. |

`crt.NumberInput` is a **form control** (`formControlConfig: { relatesTo: 'control' }`). The `control` field must reference a page attribute via `$`-prefix.

### 1.1 `format` shape

```jsonc
"format": { "decimalPrecision": 2 }   // shows two fractional digits, e.g. "3.14"
"format": { "decimalPrecision": 0 }   // integer mode — no decimal separator
```

---

## 2. Step-by-step recipe

### 2.1 Declare / bind the page attribute (`viewModelConfigDiff` entry)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "Contact_Age": {
        "modelConfig": { "path": "PDS.Age" }
      }
    }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "AgeInput",
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.NumberInput",
    "label": "#ResourceString(AgeInput_label)#",
    "control": "$Contact_Age",
    "format": { "decimalPrecision": 0 },
    "labelPosition": "auto",
    "readonly": false
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.NumberInput` are in
`ComponentRegistry.json` under `componentType: "crt.NumberInput"`. This guide covers the assembly
mechanics — what to put where in Clio mobile page operations.

Additional runtime properties (not Angular `@Input` — applied via schema binding):

| Property | Type | Description |
|---|---|---|
| `visible` | `string` | Visibility binding. Bind to a boolean expression, e.g. `'$CardState \| crt.IsEqual : \'edit\''`, to show/hide the component conditionally. |

---

## 4. Copy-paste minimal example

```jsonc
{
  "operation": "insert",
  "name": "AgeInput",
  "values": {
    "type": "crt.NumberInput",
    "label": "#ResourceString(AgeInput_label)#",
    "control": "$PDS_Age",
    "format": { "decimalPrecision": 0 }
  },
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0
}
```

---

## 5. Common pitfalls

1. **Missing `$`-prefix on `control`** — `"Contact_Age"` is a literal string; `"$Contact_Age"` references the page attribute.
2. **Omitting `format`** — without it, the platform applies a default precision that may not match the entity column type (e.g. showing `.00` for an integer column). Always specify `decimalPrecision`.
3. **Binding a non-numeric attribute** — binding a text or lookup attribute produces `NaN` display. Ensure the attribute's data source column is `Integer`, `Float`, `Money`, or `Double`.
