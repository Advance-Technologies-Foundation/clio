# How to Add an Icon Radio Button (`crt.IconRadioButton`) to a Freedom UI Page

> Audience: code agent inserting a `crt.IconRadioButton` into a Creatio Freedom UI page schema.
>
> `crt.IconRadioButton` is a group of radio-style toggle buttons — each button can optionally show an icon.
> It is a **FormControl component**: the `control` input receives a `FormControl` bound to a page attribute,
> and the `items` input supplies the button definitions. Selecting a button sets the control value.

## Metadata

- **Category**: fields
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: none

---

## 1. Mental model — the 2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.IconRadioButton"` and the `control`/`items` bindings. **Always present.** |
| 2 | `viewModelConfigDiff` | An attribute to hold the selected value; bound via `$`-prefix. **Always present.** |

`crt.IconRadioButton` is a **FormControl component** — `control` must be a `$`-prefixed viewModel attribute
reference. There is no separate datasource.

### 1.1 Naming convention

```
IconRadioButton_<id>        // view element name
$IconRadioButton_<id>       // $-prefix attribute in viewModelConfigDiff
```

---

## 2. Step-by-step recipe

### 2.1 Add a viewModel attribute (`viewModelConfigDiff` entry)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "IconRadioButton_abc123": {
        "value": "option1"
      }
    }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "IconRadioButton_abc123",
  "parentName": "SettingsContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.IconRadioButton",
    "control": "$IconRadioButton_abc123",
    "label": "#ResourceString(IconRadioButton_abc123_label)#",
    "direction": "row",
    "items": [
      { "value": "option1", "caption": "#ResourceString(Option1_caption)#", "icon": "list-icon" },
      { "value": "option2", "caption": "#ResourceString(Option2_caption)#", "icon": "board-icon" }
    ],
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.IconRadioButton` are in `ComponentRegistry.json` under `componentType: "crt.IconRadioButton"`. This guide
covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// RadioOption — one toggle button definition
interface RadioOption {
  value: unknown;      // the value set on the FormControl when this option is selected
  caption: string;     // visible label; use #ResourceString(...)# for localized text
  icon?: string;       // optional icon name; when set, renders the icon alongside the caption
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewModelConfigDiff — selected mode attribute
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "ViewMode": { "value": "list" }
    }
  }
}
```

```jsonc
// viewConfigDiff entry — list/tile view mode toggle
{
  "operation": "insert",
  "name": "ViewModeToggle",
  "parentName": "ToolbarContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.IconRadioButton",
    "control": "$ViewMode",
    "direction": "row",
    "items": [
      { "value": "list", "caption": "#ResourceString(ViewModeToggle_list)#", "icon": "list-icon" },
      { "value": "tile", "caption": "#ResourceString(ViewModeToggle_tile)#", "icon": "tile-icon" }
    ]
  }
}
```

---

## 7. Common pitfalls

1. **`control` not prefixed with `$`.** The FormControl binding requires a `$`-prefixed attribute reference; a plain string is treated as a literal value and the selection state never updates.
2. **`items` missing `value`.** Every `RadioOption` must have a `value` that matches the initial control value; mismatches leave all buttons visually un-selected.
3. **`direction: "column"` without enough vertical space.** In `column` layout each option stacks vertically; ensure the parent container provides enough height.
4. **No `label` resource string.** The label above the group is optional but omitting it removes the field label entirely — provide one unless this is an icon-only toggle with no visible caption.
5. **Setting `disabled: true` with `disabledStateTooltip` empty.** When disabled, hovering over buttons shows `disabledStateTooltip`; leave it informative so users understand why the control is read-only.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.IconRadioButton"`, unique `name`, valid `parentName`, `propertyName: "items"`, and a valid `index`.
- [ ] `control` is set to a `$`-prefixed attribute name.
- [ ] Matching attribute entry exists in `viewModelConfigDiff.attributes` with a default `value`.
- [ ] `items` array contains at least two options; each has `value` and `caption`.
- [ ] `direction` is `"row"` (default) or `"column"`.
- [ ] `layoutConfig` provided when inside a `crt.GridContainer`.
