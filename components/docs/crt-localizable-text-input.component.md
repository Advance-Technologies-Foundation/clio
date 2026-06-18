# How to Add a Localizable Text Input (`crt.LocalizableTextInput`) to a Freedom UI Page

> Audience: code agent inserting a `crt.LocalizableTextInput` into a Creatio Freedom UI page schema.
>
> A `crt.LocalizableTextInput` is a text field that stores a JSON-encoded array of
> `{ cultureName, value }` pairs so one attribute can hold translations for multiple languages.
> It extends the standard `crt.Input` with locale-aware value serialization.

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: none

---

## 1. Mental model — the 2-3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewModelConfigDiff` | An attribute that stores the JSON-serialized localizable value. |
| 2 | `viewConfigDiff` | An `insert` op with `type: "crt.LocalizableTextInput"` bound to the attribute. |
| 3 | `handlers` (optional) | A handler if `valueChange` needs custom logic. |

### 1.1 Naming convention

```
LocalizableTextInput_<id>        // view element name
$LocalizableTextInput_<id>       // viewModel attribute
```

---

## 2. Step-by-step recipe

### 2.1 Declare the attribute (`viewModelConfigDiff`)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "LocalizableTextInput_abc": {
        "value": ""
      }
    }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "LocalizableTextInput_abc",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.LocalizableTextInput",
    "label": "#ResourceString(LocalizableTextInput_abc_label)#",
    "value": "$LocalizableTextInput_abc",
    "placeholder": "#ResourceString(LocalizableTextInput_abc_placeholder)#",
    "labelPosition": "auto",
    "readonly": false,
    "visible": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

### 2.3 (Optional) Handle value changes

```jsonc
// viewConfigDiff.values
"valueChange": { "request": "crt.LocalizableTextInputChangeRequest" }

// handlers entry
{
  "request": "crt.LocalizableTextInputChangeRequest",
  "handler": async (request, next) => {
    // request.parameters.changedValues holds the new serialized JSON
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.LocalizableTextInput` are in `ComponentRegistry.json` under
`componentType: "crt.LocalizableTextInput"`. This guide covers only the assembly mechanics.

---

## 4. Value encoding

The `value` attribute holds a JSON string: an array of `{ cultureName, value }` objects.

```jsonc
// Example stored value for English + French
"[{\"cultureName\":\"en-US\",\"value\":\"Hello\"},{\"cultureName\":\"fr-FR\",\"value\":\"Bonjour\"}]"
```

On first use with a plain string (backward compat), the component upgrades the string into a
single-culture entry using the current UI language. When binding through a `FormControl`, pass
the control directly — the component subscribes internally to `valueChanges`.

---

## 5. Copy-paste minimal example

```jsonc
// viewModelConfigDiff entry
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "SysName_localized": { "value": "" }
    }
  }
}
```

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "SysName_LocalizableInput",
  "parentName": "GeneralInfoTab_container",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.LocalizableTextInput",
    "label": "#ResourceString(SysName_LocalizableInput_label)#",
    "value": "$SysName_localized",
    "placeholder": "",
    "labelPosition": "auto",
    "readonly": false,
    "visible": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 2, "rowSpan": 1 }
  }
}
```

---

## 6. Driving from page state

```jsonc
// viewModelConfigDiff — bind disabled state
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "SysName_disabled": { "value": false }
    }
  }
}

// viewConfigDiff.values
"disabled": "$SysName_disabled"
```

---

## 7. Common pitfalls

1. **Treating `value` as a plain string attribute** — the stored value is JSON-encoded;
   reading it directly as a string gives the raw JSON, not the display text. Use
   the component API or `LocalizableStringArray` to parse.
2. **Omitting `label`** — the input renders without a label; always provide either a
   `#ResourceString(...)#` key or a plain string.
3. **Passing a `FormControl` instance via binding** — when `value` is bound to a datasource
   attribute backed by a `FormControl`, the component detects it and subscribes to
   `valueChanges` internally; do not also wire `valueChange` output to avoid double-emit.
4. **`labelPosition: "hidden"` hides the label visually** but keeps it for screen readers via
   ARIA; this is still accessible.
5. **Backward compat**: if the stored JSON is not valid, the component falls back to treating
   the whole string as the value for the current culture.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.LocalizableTextInput"`, unique `name`,
  valid `parentName`.
- [ ] `value` bound to a viewModel attribute (e.g. `"$SysName_localized"`).
- [ ] `label` set via `#ResourceString(...)#`.
- [ ] `labelPosition` chosen (default `"auto"` adapts to container width).
- [ ] `readonly` and `disabled` set appropriately.
- [ ] If custom logic is needed on change, `valueChange` is wired to a handler.
