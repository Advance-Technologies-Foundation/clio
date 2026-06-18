# How to Add a Text Input (`crt.Input`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.Input` into a Creatio mobile page schema.
>
> A single-line text input field bound to a page attribute. On mobile the input renders with
> a touch-friendly style; multi-line mode is available via `multiline: true`.

## Metadata

- **Category**: fields
- **Container**: no
- **Parent types**: `crt.GridContainer` items, `crt.FlexContainer` items, `crt.TabContainer` items
- **Typical children**: `crt.Button` (in the `tools` slot)

---

## 1. Mental model — the 2-3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.Input"`, `label`, `control: "$<attr>"`. |
| 2 | `viewModelConfigDiff` | A `merge` op declaring the page attribute the input is bound to (`modelConfig.path` → entity column). |
| 3 | `modelConfigDiff` (only if the input writes to an entity column not yet loaded) | A `merge` op registering the column in the data source. |

`crt.Input` is a **form control** (`formControlConfig: { relatesTo: 'control' }`). The `control` field in `values` must reference a page attribute via `$`-prefix.

> **Note**: For Boolean data-type fields use `crt.Toggle`, not `crt.Input`.

### 1.1 Naming convention

```
Input_<id>              // view element name
Input_<id>_value        // page attribute (or use the entity column name directly)
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
      "Contact_Name": {
        "modelConfig": { "path": "PDS.Name" }
      }
    }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "NameInput",
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Input",
    "label": "#ResourceString(NameInput_label)#",
    "control": "$Contact_Name",
    "placeholder": "#ResourceString(NameInput_placeholder)#",
    "labelPosition": "auto",
    "readonly": false,
    "multiline": false
  }
}
```

### 2.3 (Optional) Register an entity column in `modelConfigDiff`

```jsonc
{
  "operation": "merge",
  "path": ["dataSources", "PDS", "config", "attributes"],
  "values": {
    "Name": { "path": "Name" }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.Input` are in
`ComponentRegistry.json` under `componentType: "crt.Input"`. This guide covers the assembly
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
  "name": "NameInput",
  "values": {
    "type": "crt.Input",
    "label": "#ResourceString(NameInput_label)#",
    "control": "$PDS_Name"
  },
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0
}
```

---

## 5. Common pitfalls

1. **Missing `$`-prefix on `control`** — `"Contact_Name"` is a literal string and the field is unbound; `"$Contact_Name"` references the page attribute.
2. **Using `crt.Input` for Boolean columns** — a Boolean attribute renders `true`/`false` as text. Use `crt.Toggle` instead.
3. **`multiline: true` combined with `mask`** — masks target `<input>` semantics; behavior on `<textarea>` is unpredictable. Pick one.

---

## 6. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.Input"`, unique `name`, valid `parentName`, `propertyName: "items"`, `index`.
- [ ] `control` set to `"$<attr>"` — attribute declared in `viewModelConfigDiff`.
- [ ] `label` (or `ariaLabel`) provided.
- [ ] Bound attribute has `modelConfig.path` pointing at the right `<DS>.<Column>`, or `value` for transient state.
