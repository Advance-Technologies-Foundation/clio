# How to Add a Checkbox (`crt.Checkbox`) to a Freedom UI Page

> Audience: code agent inserting a `crt.Checkbox` into a Creatio Freedom UI page schema.
>
> `crt.Checkbox` is a Material checkbox that binds to a boolean value (or, with
> `indeterminate`, a tri-state). Use it for `Boolean (12)` columns or transient flags.

For the underlying contract, see crt.Input guide. This document highlights only the checkbox-specific differences.

## Metadata

- **Category**: fields
- **Container**: no
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`
- **Typical children**: none

---

## 1. Mental model — the 2-3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.Checkbox"`, `label`, `control: "$<attr>"`. |
| 2 | `viewModelConfigDiff` | A page attribute bound to a Boolean column (or with a literal `value`). |
| 3 | `modelConfigDiff` | Register the Boolean column on the page data source if it's not already there. |

### 1.1 Naming convention

```
Checkbox_<id>           // view element name
Checkbox_<id>_value     // page attribute
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
      "Contact_DoNotEmail": {
        "modelConfig": { "path": "PDS.DoNotEmail" }
      }
    }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "Checkbox_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Checkbox",
    "label": "#ResourceString(Checkbox_xkp4r_Label)#",
    "control": "$Contact_DoNotEmail",
    "tooltip": "#ResourceString(Checkbox_xkp4r_Tooltip)#",
    "labelPosition": "right",
    "inversed": false,
    "indeterminate": false,
    "multilineLabel": false,
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
    "DoNotEmail": { "path": "DoNotEmail" }
  }
}
```

**Recommended `dataValueType` of the bound column:** `12 (Boolean)`.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.Checkbox` are in `ComponentRegistry.json` under `componentType: "crt.Checkbox"`. This guide covers
only the assembly mechanics — what to put where in `viewConfigDiff` /
`viewModelConfigDiff` / `modelConfigDiff`.

---

## 4. Copy-paste minimal example

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "ContactDoNotEmail",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Checkbox",
    "label": "#ResourceString(ContactDoNotEmail_Label)#",
    "control": "$Contact_DoNotEmail",
    "labelPosition": "right",
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
      "Contact_DoNotEmail": { "modelConfig": { "path": "PDS.DoNotEmail" } }
    }
  }
}
```

---

## 5. Common pitfalls

1. **`inversed` mismatched with the column's polarity** — if the column is `IsActive` (true = active) and you label the checkbox "Disabled", you'd set `inversed: true` *and* update the label. Pick one strategy and document it.
2. **`indeterminate` set to a static `true`** — the user can still click the box, after which the indeterminate visual is gone. `indeterminate` is meant for transient bulk-edit scenarios, not as a permanent default.
3. **Empty `label` + missing `ariaLabel`** — fails accessibility audits and screen readers announce nothing meaningful.
4. **Binding `crt.Checkbox` to a non-Boolean column** — `crt.Checkbox` writes `true`/`false`. A `Text` column receives the literal strings `"true"`/`"false"` (or `0`/`1` depending on serialization), surprising downstream code.
5. **Combining `readonly: true` with click-fired handlers** — `crt.Checkbox` disables click events when `readonly`, so a `valueChange` request never fires.

---

## 6. Quick checklist

- [ ] `insert` op with `type: "crt.Checkbox"`, unique `name`, `propertyName: "items"`.
- [ ] `control: "$<attr>"` references a Boolean attribute (entity column or transient).
- [ ] `label` (or `ariaLabel`) provided.
- [ ] `labelPosition: "right"` for natural checkbox layout (unless the design calls for `"left"`/`"above"`).
- [ ] `inversed` only when the visual semantics differ from the stored polarity.
- [ ] `layoutConfig` provided.
