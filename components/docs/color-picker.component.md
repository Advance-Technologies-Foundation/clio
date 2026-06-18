# How to Add a Color Picker (`crt.ColorPicker`) to a Freedom UI Page

> Audience: code agent inserting a `crt.ColorPicker` into a Creatio Freedom UI page schema.
> A `crt.ColorPicker` is a form-control input that lets the user pick a hex/RGBA color from a swatch palette or an extended color editor; it binds to a `DataValueType.Color` model column via `control`.

## Metadata

- **Category**: fields
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.TabContainer`, `crt.HeaderContainer`, root page container
- **Typical children**: none

---

## 1. Mental model — the 3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.ColorPicker"` and starting values. **Always present.** |
| 2 | `viewModelConfigDiff` | A `$-prefix` attribute bound to the `control` input (when driven from a datasource attribute). |
| 3 | `modelConfigDiff` | *(only if using a new datasource — most pages bind to the primary datasource column directly)* |

### 1.1 Naming convention

```
ColorPicker_<id>        // view element name; <id> = any short unique slug
$ColorPicker_<id>       // $-prefix attribute, only when viewModelConfigDiff is touched
```

---

## 2. Step-by-step recipe

### 2.1 Insert the color picker (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "ColorPicker_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ColorPicker",
    "label": "#ResourceString(ColorPicker_abc123_label)#",
    "labelPosition": "above",
    "control": "$Color",
    "pickerMode": "extended",
    "visible": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

Drop `layoutConfig` when the parent is a `crt.FlexContainer`.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ColorPicker` are in `ComponentRegistry.json` under `componentType: "crt.ColorPicker"`. This guide
covers only the assembly mechanics.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "ColorPicker_color",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ColorPicker",
    "label": "$Resources.Strings.Color",
    "labelPosition": "above",
    "control": "$Color",
    "pickerMode": "extended"
  }
}
```

---

## 6. Driving from page state

```jsonc
// viewConfigDiff.values — bind control to a datasource attribute
"control": "$Color"
```

`control` accepts a `$-prefix` attribute name that references a `DataValueType.Color` column on the datasource.
Use `"$Resources.Strings.Color"` for a localized label or `"#ResourceString(<key>)#"` for a custom string.

---

## 7. Common pitfalls

1. **Binding `control` to a non-Color column** — `crt.ColorPicker` validates the value as a hex/RGBA string; binding to a text or lookup column will cause validation errors.
2. **Setting `pickerMode` to an unlisted value** — only `"basic"` (swatch grid only) and `"extended"` (swatch + HEX/RGBA editor) are valid; invalid values fall back to `"extended"`.
3. **Omitting `label`** — while optional, omitting the label makes the field inaccessible to screen readers; always provide a localized label string.
4. **Using `selectedColor` instead of `control`** — `selectedColor` is a secondary input for programmatic color injection; for form-bound pages use `control` which owns the `FormControl` value.
5. **Providing `colors` as empty array** — an empty `colors` array disables the swatch palette entirely; the picker won't open at runtime.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.ColorPicker"`, unique `name`, valid `parentName`, `propertyName: "items"`, and a valid `index`.
- [ ] `control` bound to a `$-prefix` attribute for a `DataValueType.Color` column.
- [ ] `label` provided as `#ResourceString(...)#` or a `$Resources.Strings.*` reference.
- [ ] `labelPosition` set (`"above"` is the most common choice).
- [ ] `pickerMode` set to `"basic"` or `"extended"`.
- [ ] If parent is `crt.GridContainer`, `layoutConfig: { row, column, rowSpan, colSpan }` is present.
