# How to Add an Email Input (`crt.EmailInput`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.EmailInput` into a Creatio mobile page schema.
>
> An email-type text input with format validation, bound to a page attribute that holds an
> `EmailText` data-type column.

## Metadata

- **Category**: fields
- **Container**: no
- **Parent types**: `crt.GridContainer` items, `crt.FlexContainer` items, `crt.TabContainer` items
- **Typical children**: `crt.Button` (in the `tools` slot)

---

## 1. Mental model — the 2-3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.EmailInput"`, `label`, `control: "$<attr>"`. |
| 2 | `viewModelConfigDiff` | A `merge` op declaring the page attribute bound to an `EmailText` column. |
| 3 | `modelConfigDiff` (only if the column is not yet loaded) | A `merge` op registering the column in the data source. |

`crt.EmailInput` is a **form control** (`formControlConfig: { relatesTo: 'control' }`). The `control` field must reference a page attribute via `$`-prefix.

> **Note**: The input accepts only `DataValueType.EMAIL_TEXT` (value `27`) columns. Binding a plain text column produces no validation.

---

## 2. Step-by-step recipe

### 2.1 Declare / bind the page attribute (`viewModelConfigDiff` entry)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "Contact_Email": {
        "modelConfig": { "path": "PDS.Email" }
      }
    }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "EmailInput",
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.EmailInput",
    "label": "#ResourceString(EmailInput_label)#",
    "control": "$Contact_Email",
    "labelPosition": "auto",
    "readonly": false
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.EmailInput` are in
`ComponentRegistry.json` under `componentType: "crt.EmailInput"`. This guide covers the assembly
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
  "name": "EmailInput",
  "values": {
    "type": "crt.EmailInput",
    "label": "#ResourceString(EmailInput_label)#",
    "control": "$PDS_Email"
  },
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0
}
```

---

## 5. Common pitfalls

1. **Missing `$`-prefix on `control`** — `"Contact_Email"` is a literal string; `"$Contact_Email"` references the page attribute.
2. **Binding a plain `Text` column** — the email format validator won't fire because the component activates validation only for `EmailText` data type. Ensure the entity column type is `EmailText`.
3. **Using `crt.Input` instead of `crt.EmailInput`** — `crt.Input` doesn't apply email validation or provide the mailto tap-to-open affordance that `crt.EmailInput` does on mobile.
