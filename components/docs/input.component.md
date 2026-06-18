# How to Add a Text Input (`crt.Input`) to a Freedom UI Page

> Audience: code agent inserting a `crt.Input` into a Creatio Freedom UI page schema.
>
> A Freedom UI page schema is a single JS file with these sections:
> `viewConfigDiff`, `viewModelConfigDiff`, `modelConfigDiff`, `handlers`. A bound text input
> requires coordinated changes in the first **two** (and a `modelConfigDiff` attribute when
> the input writes to an entity column).

## Metadata

- **Category**: fields
- **Container**: no
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`
- **Typical children**: `crt.Button` (in the `tools` slot)

---

## 1. Mental model — the 2-3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.Input"`, `label`, `control: "$<attr>"`. |
| 2 | `viewModelConfigDiff` | A `merge` op declaring the page attribute the input is bound to (`modelConfig.path` → entity column). |
| 3 | `modelConfigDiff` (only if the input writes to an entity column on the page's primary record) | A `merge` op registering the page's `attributes` block — usually already present in the base page. |

`crt.Input` is a **form control**. It exposes `formControlConfig: { relatesTo: 'control' }`, so the `control` field in `values` must point at a page attribute via `$`-prefix.

### 1.1 How diff operations work

All three sections are **arrays of diff operations**:

- `path` is **always `Array<string>`**, never a bare string. `[]` for the section root, `["attributes"]` to descend one level.
- `merge` blends `values` into the existing tree at `path`.
- `insert` adds a new entry into a parent's slot (`propertyName: "items"`, `index: N`).

(`JsonDiffOperation` shape: `{ operation: "merge" | "insert" | "remove", path: string[], values?: unknown, name?: string, parentName?: string, propertyName?: string, index?: number }`.)

### 1.2 Naming convention

```
Input_<id>              // view element name; <id> is any short unique slug
Input_<id>_value        // page attribute carrying the value
```

A common shorthand is to name the attribute after the entity column it writes to (e.g. `Contact_Name`). Either convention works; just keep `control: "$<attrName>"` aligned with the attribute name.

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

`PDS` is the conventional name of the page's primary data source (`crt.PageDataSource` / `crt.EntityDataSource`). Use whatever DS key your page uses. If the value lives entirely on the page (not persisted), drop `modelConfig` and use `value` instead:

```jsonc
"attributes": { "MyTransientText": { "value": "" } }
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "Input_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Input",
    "label": "#ResourceString(Input_xkp4r_Label)#",
    "control": "$Contact_Name",
    "placeholder": "#ResourceString(Input_xkp4r_Placeholder)#",
    "tooltip": "#ResourceString(Input_xkp4r_Tooltip)#",
    "labelPosition": "auto",
    "multiline": false,
    "readonly": false,
    "appearance": "outline",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 2, "rowSpan": 1 }
  }
}
```

### 2.3 (Optional) Register an entity column in `modelConfigDiff`

If the bound path (`PDS.Name`) is **not** already declared in the page's primary data source, register the column:

```jsonc
{
  "operation": "merge",
  "path": ["dataSources", "PDS", "config", "attributes"],
  "values": {
    "Name": { "path": "Name" }
  }
}
```

For brand-new pages, you typically add the column once during page authoring and don't need to repeat this in every input recipe.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.Input` are in `ComponentRegistry.json` under `componentType: "crt.Input"`. This guide covers
only the assembly mechanics — what to put where in `viewConfigDiff` /
`viewModelConfigDiff` / `modelConfigDiff`.

---

## 4. `tools` slot (advanced)

`crt.Input` declares `contentSlots: ['tools']`. You can insert auxiliary view elements (icons, action buttons) that float at the end of the input:

```jsonc
{
  "operation": "insert",
  "name": "Input_xkp4r_clearBtn",
  "parentName": "Input_xkp4r",
  "propertyName": "tools",
  "index": 0,
  "values": {
    "type": "crt.Button",
    "icon": "close",
    "iconPosition": "only-icon",
    "displayType": "text",
    "clicked": { "request": "crt.ClearInputRequest" }
  }
}
```

The control sets `displayTools` automatically when this slot has content and the input is editable.

---

## 5. Outputs (wire to handlers via the binding object)

To wire an output in:

```jsonc
"blurred": { "request": "crt.ValidateInputRequest" }
```

Then add a matching `handlers` entry. See `ComponentRegistry.json` for the full output list and payload types.

---

## 6. Copy-paste minimal example

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "NameInput",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Input",
    "label": "#ResourceString(NameInput_Label)#",
    "control": "$Contact_Name",
    "placeholder": "#ResourceString(NameInput_Placeholder)#",
    "labelPosition": "auto",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 2, "rowSpan": 1 }
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
      "Contact_Name": { "modelConfig": { "path": "PDS.Name" } }
    }
  }
}
```

```jsonc
// modelConfigDiff entry — only if the column isn't already loaded
{
  "operation": "merge",
  "path": ["dataSources", "PDS", "config", "attributes"],
  "values": {
    "Name": { "path": "Name" }
  }
}
```

---

## 7. Common pitfalls

1. **`control` value missing the `$`-prefix** — `"Contact_Name"` is treated as a literal string and the input is unbound; `"$Contact_Name"` references the attribute.
2. **Binding an attribute that doesn't exist** — `$Missing` evaluates to `undefined`. The input shows empty and silently swallows user input on commit. The platform does not warn.
3. **`multiline: true` plus `mask`** — masks target `<input>` semantics; results are unpredictable on `<textarea>`. Pick one.
4. **Wrong `dataValueType` on the entity column** — e.g. binding a `Lookup`-typed column (10) to `crt.Input`. The input will render the lookup's `{ value, displayValue }` object as `[object Object]`. Use `crt.ComboBox` for lookups.
5. **`labelPosition: "above"` with `appearance: "legacy"`** — the floating-label animation collides with the static "above" position; switch to `appearance: "outline"`.
6. **`required: true` without an actual validator** — the asterisk appears, but the form will still submit empty. Add a validator on the attribute in `viewModelConfigDiff`:
   ```jsonc
   "Contact_Name": {
     "modelConfig": { "path": "PDS.Name" },
     "validators": { "required": { "type": "required" } }
   }
   ```
7. **Setting both `value` and `control`** — `value` writes once via `control.setValue`, but the form control is what's bound. If `control` is bound to an attribute, set the initial value via the attribute's `"value"` instead.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.Input"`, unique `name`, valid `parentName`, `propertyName: "items"`, `index`.
- [ ] `control` set to `"$<attr>"` — `<attr>` declared in `viewModelConfigDiff`.
- [ ] `label` (or `ariaLabel` when there's no visible label) provided.
- [ ] Bound attribute has `modelConfig.path` pointing at the right `<DS>.<Column>`, or has a `value` for transient state.
- [ ] If `required: true`, the attribute has a `validators.required` entry too.
- [ ] `layoutConfig` provided.
