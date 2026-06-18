# How to Add a Web Input (`crt.WebInput`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.WebInput` into a Creatio mobile page schema.
>
> A URL/web address text input bound to a page attribute that holds a `WebText` data-type column.

## Metadata

- **Category**: fields
- **Container**: no
- **Parent types**: `crt.GridContainer` items, `crt.FlexContainer` items, `crt.TabContainer` items
- **Typical children**: `crt.Button` (in the `tools` slot)

---

## 1. Mental model — the 2-3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.WebInput"`, `label`, `control: "$<attr>"`. |
| 2 | `viewModelConfigDiff` | A `merge` op declaring the page attribute bound to a `WebText` column. |
| 3 | `modelConfigDiff` (only if the column is not yet loaded) | A `merge` op registering the column in the data source. |

`crt.WebInput` is a **form control** (`formControlConfig: { relatesTo: 'control' }`). The `control` field must reference a page attribute via `$`-prefix.

> **Note**: The input accepts only `DataValueType.WEB_TEXT` (value `28`) columns. Binding a plain text column produces no URL validation or tap-to-open behavior.

---

## 2. Step-by-step recipe

### 2.1 Declare / bind the page attribute (`viewModelConfigDiff` entry)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "Contact_Website": {
        "modelConfig": { "path": "PDS.Website" }
      }
    }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "WebsiteInput",
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.WebInput",
    "label": "#ResourceString(WebsiteInput_label)#",
    "control": "$Contact_Website",
    "labelPosition": "auto",
    "readonly": false
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.WebInput` are in
`ComponentRegistry.json` under `componentType: "crt.WebInput"`. This guide covers the assembly
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
  "name": "WebsiteInput",
  "values": {
    "type": "crt.WebInput",
    "label": "#ResourceString(WebsiteInput_label)#",
    "control": "$PDS_Website"
  },
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0
}
```

---

## 5. Common pitfalls

1. **Missing `$`-prefix on `control`** — `"Contact_Website"` is a literal string; `"$Contact_Website"` references the page attribute.
2. **Binding a plain `Text` column** — URL validation and the tap-to-open browser intent won't activate. Ensure the entity column type is `WebText`.
3. **Using `crt.Input` instead of `crt.WebInput`** — `crt.Input` doesn't apply URL validation or provide the tap-to-open web affordance that `crt.WebInput` offers on mobile.
