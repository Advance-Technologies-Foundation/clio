# How to Add a Switch (`crt.Switch`) to a Freedom UI Page

> Audience: code agent inserting a `crt.Switch` into a Creatio Freedom UI page schema.
>
> `crt.Switch` is a Material slide toggle bound to a boolean value. It is the visual
> alternative to `crt.Checkbox` when the field expresses a binary "on/off" setting rather
> than a "yes/no" choice.

For the underlying contract, see crt.Input guide. This document highlights only the switch-specific differences.

## Metadata

- **Category**: fields
- **Container**: no
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`
- **Typical children**: none

---

## 1. Mental model — the 2-3 places you must edit

Identical to `crt.Checkbox`:
- `viewConfigDiff` with `type: "crt.Switch"`.
- `viewModelConfigDiff` with a boolean attribute.
- `modelConfigDiff` with the `Boolean` column (when not already declared).

### 1.1 Naming convention

```
Switch_<id>            // view element name
Switch_<id>_value      // page attribute
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
      "User_NotificationsEnabled": {
        "modelConfig": { "path": "PDS.NotificationsEnabled" }
      }
    }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "Switch_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Switch",
    "label": "#ResourceString(Switch_xkp4r_Label)#",
    "control": "$User_NotificationsEnabled",
    "checked": true,
    "labelPosition": "auto",
    "tooltip": "#ResourceString(Switch_xkp4r_Tooltip)#",
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
    "NotificationsEnabled": { "path": "NotificationsEnabled" }
  }
}
```

---

## 3. Property reference

The full property catalog (inputs, outputs, defaults, deprecated flags, designer defaults) lives in
`ComponentRegistry.json` under `componentType: "crt.Switch"`. Below is the practical subset a
code agent needs to author a correct `viewConfigDiff` insert payload.

### 3.1 `values` field reference

| Field | Type | Required | Default | Notes |
|---|---|---|---|---|
| `type` | string | yes | — | Always `"crt.Switch"`. |
| `control` | string | yes (for bound data) | — | Reference to a Boolean page attribute, format `"$<AttributeName>"`. Source of truth at runtime. |
| `checked` | boolean | no | `true` | Initial state pushed into `control` once. Omit when `control` is bound to a persisted attribute — the column value wins anyway. |
| `label` | string | yes (or `ariaLabel`) | `""` | The Title shown next to the toggle. Use `"#ResourceString(<Key>)#"` for localized text. |
| `ariaLabel` | string | yes (or `label`) | `""` | Title announced by screen readers when `label` is empty. Use `"#ResourceString(<Key>)#"`. |
| `labelPosition` | `"auto" \| "right" \| "left" \| "above" \| "hidden"` | no | `"auto"` | See §3.3. |
| `tooltip` | string | no | — | Help icon next to the title. Localize with `"#ResourceString(<Key>)#"`. |
| `readonly` | boolean \| `"$<Attr>"` | no | `false` | Bindable. Blocks clicks but keeps the toggle visible and a11y-readable. |
| `disabled` | boolean | no | `false` | Greys out and excludes from form submission. Not bindable. |
| `visible` | boolean \| `"$<Attr>"` | no | `true` | Standard platform property. |
| `layoutConfig` | object | yes (inside grid) | — | `{ "column": N, "row": N, "colSpan": N, "rowSpan": N }`. Required when the parent is `crt.GridContainer`. |

### 3.2 Title (`label` / `ariaLabel`)

- The visible Title of the switch is `label`. Always pass it as a `ResourceString` macro:
  `"label": "#ResourceString(<SchemaName>_<ViewElementName>_Label)#"`.
- When the design requires a switch without a visible title (icon-only layouts), set
  `label` to an empty string and provide `ariaLabel` instead so screen readers still get context.
- Resource strings referenced in `label` / `ariaLabel` / `tooltip` must exist in the schema's
  `Resources` (Russian/English/etc. localization files); the agent must register them when
  generating new keys.

### 3.3 Title position (`labelPosition`)

| Value | Meaning |
|---|---|
| `"auto"` | Position is decided by the container width at runtime (`above` in narrow columns, `left` in wide ones). Recommended default for responsive forms. |
| `"right"` | Title rendered to the right of the toggle. Switch-specific value (not available on other inputs); the canonical layout for settings rows like "Notifications". |
| `"left"` | Title rendered to the left of the toggle. |
| `"above"` | Title rendered on a separate line above the toggle. Use only in wide cells. |
| `"hidden"` | Title not rendered. Requires `ariaLabel` for accessibility. |

### 3.4 Data source (`control` ↔ attribute ↔ column)

Connecting a switch to data is a **3-layer wiring** — the agent must touch all three for a persisted value:

1. **Model column** (`modelConfigDiff` → `dataSources.PDS.config.attributes`) — declares the
   Boolean column on the entity data source. Skip if the column is already part of the
   inherited PDS configuration.
2. **Page attribute** (`viewModelConfigDiff` → `attributes`) — the in-page variable the view
   element actually binds to. Its `modelConfig.path` points at `PDS.<ColumnName>`.
3. **View binding** (`viewConfigDiff.values.control`) — string `"$<PageAttributeName>"` that
   links the switch to the page attribute.

Naming rule for the trio:
```
Column:         UsrNotificationsEnabled        (entity Boolean column)
Page attribute: User_NotificationsEnabled      (camel + underscore prefix)
control value:  "$User_NotificationsEnabled"
```

If the switch must stay ephemeral (UI-only flag, never saved), skip step 1 and declare the
attribute in step 2 without `modelConfig.path`:

```jsonc
"attributes": {
  "Switch_xkp4r_value": { "defaultValue": false }
}
```

---

## 4. Copy-paste minimal example

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "NotificationsToggle",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Switch",
    "label": "#ResourceString(NotificationsToggle_Label)#",
    "control": "$User_NotificationsEnabled",
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
      "User_NotificationsEnabled": { "modelConfig": { "path": "PDS.NotificationsEnabled" } }
    }
  }
}
```

---

## 5. Common pitfalls

1. **`crt.Switch` vs `crt.Checkbox`** — semantically identical at the data layer (both boolean), but visually distinct. Use `crt.Switch` for settings/preferences ("Notifications", "Dark mode"); use `crt.Checkbox` for explicit consent or list selection.
2. **Setting both `checked` and `control`** — `checked` writes once via `control.setValue`. If `control` is bound to an attribute, set the initial value via the attribute's `"value"` instead.
3. **Empty `label` + missing `ariaLabel`** — screen readers announce only "switch" without context. Always provide one of the two.
4. **`labelPosition: "above"` in a tight column** — the label may wrap awkwardly. Use `"above"` only when the column is wide enough.
5. **`readonly: true` combined with `checkedChange` handler** — the handler never fires (clicks are prevented).
6. **Inversion not supported** — unlike `crt.Checkbox`, `crt.Switch` has no `inversed` property. If you need negative polarity, rename the column or compute the value in a getter attribute.

---

## 6. Quick checklist

- [ ] `insert` op with `type: "crt.Switch"`, unique `name`, `propertyName: "items"`.
- [ ] `control: "$<attr>"` references a Boolean page attribute (see §3.4 for the 3-layer wiring).
- [ ] `label` (or `ariaLabel` when title is hidden) provided as `#ResourceString(...)#`.
- [ ] Resource string keys for `label` / `ariaLabel` / `tooltip` registered in the schema's `Resources`.
- [ ] `labelPosition` chosen: `"right"` for settings rows, `"auto"` for responsive forms.
- [ ] `layoutConfig` provided when parent is a `crt.GridContainer`.
- [ ] If data is persisted: page attribute exists in `viewModelConfigDiff`, and the Boolean column exists in `modelConfigDiff` (or in the inherited PDS).
