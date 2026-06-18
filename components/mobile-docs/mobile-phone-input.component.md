# How to Add a Phone Input (`crt.PhoneInput`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.PhoneInput` into a Creatio mobile page schema.
>
> A phone number input with call intent support, bound to a page attribute that holds a
> `PhoneText` data-type column.

## Metadata

- **Category**: fields
- **Container**: no
- **Parent types**: `crt.GridContainer` items, `crt.FlexContainer` items, `crt.TabContainer` items
- **Typical children**: `crt.Button` (in the `tools` slot)

---

## 1. Mental model — the 2-3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.PhoneInput"`, `label`, `control: "$<attr>"`. |
| 2 | `viewModelConfigDiff` | A `merge` op declaring the page attribute bound to a `PhoneText` column. |
| 3 | `modelConfigDiff` (only if the column is not yet loaded) | A `merge` op registering the column in the data source. |

`crt.PhoneInput` is a **form control** (`formControlConfig: { relatesTo: 'control' }`). The `control` field must reference a page attribute via `$`-prefix.

> **Note**: The input accepts only `DataValueType.PHONE_TEXT` (value `29`) columns. On mobile it renders a tap-to-call affordance; binding a plain text column disables this.

---

## 2. Step-by-step recipe

### 2.1 Declare / bind the page attribute (`viewModelConfigDiff` entry)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "Contact_Phone": {
        "modelConfig": { "path": "PDS.Phone" }
      }
    }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "PhoneInput",
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.PhoneInput",
    "label": "#ResourceString(PhoneInput_label)#",
    "control": "$Contact_Phone",
    "labelPosition": "auto",
    "readonly": false
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.PhoneInput` are in
`ComponentRegistry.json` under `componentType: "crt.PhoneInput"`. This guide covers the assembly
mechanics — what to put where in Clio mobile page operations.

Additional runtime properties (not Angular `@Input` — applied via schema binding):

| Property | Type | Description |
|---|---|---|
| `visible` | `string` | Visibility binding. Bind to a boolean expression, e.g. `'$CardState \| crt.IsEqual : \'edit\''`, to show/hide the component conditionally. |
| `caption` | string | Alias for `label` used in some schema generators — resource string key for the field caption. |

---

## 4. Copy-paste minimal example

```jsonc
{
  "operation": "insert",
  "name": "PhoneInput",
  "values": {
    "type": "crt.PhoneInput",
    "label": "#ResourceString(PhoneInput_label)#",
    "control": "$PDS_Phone"
  },
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0
}
```

---

## 5. Common pitfalls

1. **Missing `$`-prefix on `control`** — `"Contact_Phone"` is a literal string; `"$Contact_Phone"` references the page attribute.
2. **Binding a plain `Text` column** — the tap-to-call intent and phone format validation won't activate. Ensure the entity column type is `PhoneText`.
3. **Using `crt.Input` instead of `crt.PhoneInput`** — `crt.Input` doesn't provide the call intent affordance or phone-number keyboard on mobile; use `crt.PhoneInput` for all phone columns.
