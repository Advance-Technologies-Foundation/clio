# How to Add a Password Input (`crt.PasswordInput`) to a Freedom UI Page

> Audience: code agent inserting `crt.PasswordInput` into a Creatio Freedom UI page schema.
>
> `crt.PasswordInput` is a single-line text field that masks its value with asterisks and exposes a
> toggle button to reveal/hide the password. It extends `crt.Input` with a `validations` input for
> rule-based validation hints and a built-in reveal-mask switch. Bind a `FormControl` via `control` to
> connect to a data source attribute.

## Metadata

- **Category**: fields
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.TabContainer`
- **Typical children**: none

---

## 1. Mental model — the 2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.PasswordInput"` and a `control` binding to a `$`-prefixed attribute. **Always present.** |
| 2 | `viewModelConfigDiff` | An attribute declaration for the password value (or reuse a datasource-bound attribute). |

No `modelConfigDiff` or `handlers` entries are needed for basic usage.

### 1.1 Naming convention

```
PasswordInput_<id>         // view element name
$PasswordInput_<id>        // viewModel attribute for standalone use (not datasource-bound)
```

---

## 2. Step-by-step recipe

### 2.1 Declare the viewModel attribute (`viewModelConfigDiff`)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "CurrentPassword": { "value": null }
    }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "PasswordInput_current",
  "parentName": "FlexContainer_PasswordSettings",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.PasswordInput",
    "label": "#ResourceString(PasswordInput_current_label)#",
    "control": "$CurrentPassword",
    "placeholder": "",
    "tooltip": "",
    "readonly": false,
    "labelPosition": "above",
    "visible": true,
    "layoutConfig": {}
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.PasswordInput` are in `ComponentRegistry.json` under `componentType: "crt.PasswordInput"`. This
guide covers only the assembly mechanics.

**Key inputs unique to `crt.PasswordInput` (beyond base input):**

| Input | Type | Description |
|---|---|---|
| `validations` | `RuleValidationResultItem[]` | Array of validation results for inline hints (see §4). |

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// RuleValidationResultItem — used by the validations input
interface RuleValidationResultItem {
  caption: string;   // validation hint text shown in the field
  valid: boolean;    // null = pending; true = valid; false = invalid
}
```

---

## 5. Copy-paste minimal example

Real PackageStore usage from `SysUserProfilePage`:

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "CurrentPasswordInput",
  "parentName": "FlexContainer_PasswordSettings",
  "propertyName": "items",
  "index": 1,
  "values": {
    "layoutConfig": {},
    "type": "crt.PasswordInput",
    "label": "#ResourceString(CurrentPasswordInput_label)#",
    "control": "$CurrentPassword",
    "placeholder": "",
    "tooltip": "",
    "readonly": false,
    "multiline": false,
    "labelPosition": "above",
    "visible": true
  }
}
```

```jsonc
// viewModelConfigDiff
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "CurrentPassword": { "value": null }
    }
  }
}
```

---

## 6. Driving from page state

```jsonc
// viewModelConfigDiff
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "PasswordInput_readonly": { "value": false }
    }
  }
}

// viewConfigDiff.values
"readonly": "$PasswordInput_readonly"
```

---

## 7. Common pitfalls

1. **`multiline: true` is silently rejected** — `crt.PasswordInput` inherits the textarea option from `crt.Input`, but a multiline password field makes no UX sense; the platform ignores the flag.
2. **Binding `control` to a datasource attribute path** — use the `$AttributeName` syntax; without the `$` prefix the runtime treats it as a literal string.
3. **`labelPosition: "auto"` vs explicit** — `"auto"` switches between `"left"` and `"above"` based on container width. Set `"above"` explicitly in narrow containers to avoid label jumping.
4. **`validations` with `valid: null`** — a tooltip icon appears only when `valid === null` and the control has been touched; this is the pending-state indicator, not an error.
5. **`disabled` vs `readonly`** — `disabled` dims the field and prevents interaction; `readonly` keeps normal styling but blocks editing. Choose `readonly` when you want the value to remain copyable.
6. **Missing `label` resource string** — if the `#ResourceString(...)#` key does not exist, the label renders as the raw key. Always register localization strings in the schema resources.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.PasswordInput"`, unique `name`, valid `parentName`, `propertyName: "items"`, and valid `index`.
- [ ] `control` set to `"$<AttributeName>"` pointing to a viewModel attribute or datasource attribute.
- [ ] `label` set (use `#ResourceString(...)#` for localized text).
- [ ] `labelPosition` chosen (`"above"` is the safest default for narrow containers).
- [ ] `validations` wired if the page has password-strength or matching rules.
- [ ] `layoutConfig` present (even `{}` to accept defaults) when parent is a `crt.GridContainer`.
