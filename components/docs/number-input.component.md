# How to Add a Number Input (`crt.NumberInput`) to a Freedom UI Page

> Audience: code agent inserting a `crt.NumberInput` into a Creatio Freedom UI page schema.
>
> `crt.NumberInput` is the dedicated input for integer, float, and money columns. Bind it
> to a column whose `dataValueType` is one of `Integer (4)`, `Float (5)`, `Money (6)`,
> or the granular `FLOAT0..8` / `MONEY0..3` variants.

For the underlying contract, see crt.Input guide. This
document highlights only the number-specific differences.

## Metadata

- **Category**: fields
- **Container**: no
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`
- **Typical children**: none

---

## 1. Mental model — the 2-3 places you must edit

Identical to `crt.Input`. The only difference is the recommended column type.

### 1.1 Naming convention

```
NumberInput_<id>           // view element name
NumberInput_<id>_value     // page attribute
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
      "Opportunity_Amount": {
        "modelConfig": { "path": "PDS.Amount" }
      }
    }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "NumberInput_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.NumberInput",
    "label": "#ResourceString(NumberInput_xkp4r_Label)#",
    "control": "$Opportunity_Amount",
    "placeholder": "0",
    "tooltip": "#ResourceString(NumberInput_xkp4r_Tooltip)#",
    "labelPosition": "auto",
    "readonly": false,
    "format": { "decimalPrecision": 2 },
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

### 2.3 (Optional) Register the column in `modelConfigDiff`

```jsonc
{
  "operation": "merge",
  "path": ["dataSources", "PDS", "config", "attributes"],
  "values": {
    "Amount": { "path": "Amount" }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.NumberInput` are in `ComponentRegistry.json` under `componentType: "crt.NumberInput"`. This guide covers
only the assembly mechanics — what to put where in `viewConfigDiff` /
`viewModelConfigDiff` / `modelConfigDiff`.

---

## 4. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — currency input
{
  "operation": "insert",
  "name": "AmountInput",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.NumberInput",
    "label": "#ResourceString(AmountInput_Label)#",
    "control": "$Opportunity_Amount",
    "format": { "decimalPrecision": 2 },
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

```jsonc
// viewModelConfigDiff entry
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "Opportunity_Amount": { "modelConfig": { "path": "PDS.Amount" } }
    }
  }
}
```

---

## 5. Common pitfalls

(In addition to the generic `crt.Input` pitfalls — see § 7 of crt.Input guide.)

1. **`decimalPrecision` mismatched with the column** — e.g. `decimalPrecision: 0` against a `Float` column with 2 stored fractional digits truncates the visible value but the underlying number stays unrounded. Match `format.decimalPrecision` to the column type.
2. **Using `crt.Input` for a numeric column** — `crt.Input.value` is a string. Reading it back produces strings like `"42.0"` rather than numbers, breaking arithmetic in handlers. Switch to `crt.NumberInput` for numeric columns.
3. **Binding to a `string` column** — the input enforces numeric semantics; non-numeric typing is blocked, so a text column behind `crt.NumberInput` loses data on save.
4. **Locale-dependent separators** — the input respects the user's locale (`.` vs `,`). Don't write tests that assert on a specific separator.

---

## 6. Quick checklist

- [ ] `insert` op with `type: "crt.NumberInput"`, unique `name`, `propertyName: "items"`.
- [ ] `control: "$<attr>"` references an attribute bound to a numeric column (Integer / Float / Money / granular variant).
- [ ] `format.decimalPrecision` matches the column's precision.
- [ ] `label` (or `ariaLabel`) provided.
- [ ] `layoutConfig` provided.
