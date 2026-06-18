# How to Add a Rich Text Editor (`crt.RichTextEditor`) to a Freedom UI Page

> Audience: code agent inserting a `crt.RichTextEditor` into a Creatio Freedom UI page schema.
>
> `crt.RichTextEditor` is a CKEditor-4 wrapper that stores HTML markup. Bind it to a column
> whose `dataValueType` is `RICH_TEXT (43)`.

For the underlying contract, see crt.Input guide. This document highlights only the rich-text-specific differences.

## Metadata

- **Category**: fields
- **Container**: no
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`
- **Typical children**: none

---

## 1. Mental model

A rich-text editor is three coordinated pieces:

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | `insert` op with `type: "crt.RichTextEditor"` and `control: "$<attr>"`. |
| 2 | `viewModelConfigDiff` | A page attribute bound to a `RICH_TEXT` column (or a transient HTML state). |
| 3 | `modelConfigDiff` | Register the `RICH_TEXT` column on the page data source if missing. |

### 1.1 Naming convention

| Use case | View element name | Attribute name | Notes |
|---|---|---|---|
| Bound to a column (default) | `RichTextEditor_<id>` | Match the column, e.g. `Body` | Cleaner schema readback; the attribute reads the column directly. |
| Ephemeral HTML state (no column) | `RichTextEditor_<id>` | `RichTextEditor_<id>_value` | Use when the body is computed/derived and never persisted. |

---

## 2. Step-by-step recipe

### 2.1 Declare / bind the page attribute (`viewModelConfigDiff`)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "Activity_Body": {
        "modelConfig": { "path": "PDS.Body" }
      }
    }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff`)

```jsonc
{
  "operation": "insert",
  "name": "RichTextEditor_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.RichTextEditor",
    "label": "#ResourceString(RichTextEditor_xkp4r_Label)#",
    "control": "$Activity_Body",
    "placeholder": "#ResourceString(RichTextEditor_xkp4r_Placeholder)#",
    "tooltip": "#ResourceString(RichTextEditor_xkp4r_Tooltip)#",
    "labelPosition": "auto",
    "editorType": "inline",
    "alwaysShowToolbar": false,
    "loadAttachments": true,
    "useBodyFilesLinks": true,
    "needHandleSave": true,
    "maxContentHeight": "320px",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 4 }
  }
}
```

### 2.3 (Optional) Register the column in `modelConfigDiff`

Skip if the entity column is already exposed on the data source.

```jsonc
{
  "operation": "merge",
  "path": ["dataSources", "PDS", "config", "attributes"],
  "values": {
    "Body": { "path": "Body" }
  }
}
```

---

## 3. Property reference

### 3.1 `values` fields accepted at `insert`

Field semantics live in JSDoc on each `@CrtInput` in `rich-text-editor.component.ts`. The
table below only states **type**, **default**, and **how the field is consumed at the
schema level** (literal vs bindable, etc.).

| Field | Type | Default | Schema-level notes |
|---|---|---|---|
| `type` | string | — | Must be `"crt.RichTextEditor"`. |
| `control` | `"$<Attr>"` | `""` | Two-way binding. Required — without it the editor never persists. |
| `label`, `placeholder`, `tooltip` | `#ResourceString(...)#` | `""` | Always wrap in `#ResourceString(Key)#`; register `Key` in schema Resources. |
| `ariaLabel` | string | `""` | Required when `labelPosition: "hidden"`. |
| `labelPosition` | `"auto" \| "left" \| "above" \| "hidden"` | `"auto"` | `"right"` is **not** supported (switch-specific). |
| `readonly`, `visible` | `boolean \| "$Attr"` | `false` / `true` | Bindable. |
| `disabled` | boolean | `false` | Literal only — not bindable. |
| `editorType` | `"inline" \| "classic"` | `"inline"` | Literal. |
| `alwaysShowToolbar`, `loadAttachments`, `needHandleSave`, `useBodyFilesLinks`, `useTemplates`, `disableSelectionOptions` | boolean | see JSDoc | Literal flags. |
| `maxContentHeight` | string (CSS length) | — | Literal, e.g. `"320px"`, `"50vh"`. |
| `extraPlugins` | string[] | `[]` | Literal array of CKEditor 4 plugin names. |
| `editorConfig` | partial `CKEditor4.Config` | `{}` | Shallow-merged over platform defaults. Pass only overridden keys. |
| `mentionsConfig` | partial mentions config | `{}` | Shallow-merged over defaults. |
| `filesStorage` | object | platform `SysFile` defaults | Literal `{ masterRecordColumnValue, recordEntitySchemaName, recordColumnName, entitySchemaName }`. |
| `plainText` | `"$<Attr>"` | — | One-way mirror — bind to a `Text` column. |
| `layoutConfig` | object | — | `{ column, row, colSpan, rowSpan }`. Rich text needs generous spans. |

> See also `ComponentRegistry.json` under `componentType: "crt.RichTextEditor"` for the full
> machine-readable contract.

### 3.2 Title (`label`, `ariaLabel`)

- Always pass `label` through `#ResourceString(Key)#`; the resource key must exist in the schema's `Resources` block, otherwise the macro renders as a literal string.
- Set `ariaLabel` only when `labelPosition: "hidden"` (the visible label is missing and a11y needs a name).

### 3.3 Title position (`labelPosition`)

Allowed: `"auto"` (default) · `"left"` · `"above"` · `"hidden"`.

> `"right"` is **switch-specific** and falls back to `"auto"` here.

### 3.4 Data source — 3-layer wiring

For the editor to load and persist body content, three layers must agree:

```
modelConfigDiff (entity column on PDS)
        ↓ exposes the column
viewModelConfigDiff.attributes.<X>.modelConfig.path
        ↓ creates a page attribute
viewConfigDiff.values.control: "$<X>"
        ↓ binds the editor to the attribute
```

Rich-text constraints:

1. The entity column must have `dataValueType: RICH_TEXT (43)`. `Text (1)` stores raw HTML as a plain string and breaks downstream consumers.
2. Page attribute name should match the column; use a `_value` suffix only for ephemeral non-persisted HTML state.

Optional plain-text mirror (for search/preview):

```jsonc
"values": {
  "control":   "$Activity_Body",          // RICH_TEXT — read+write
  "plainText": "$Activity_BodyPlainText"  // Text — read-only mirror
}
```

### 3.5 Editor-mode quick guide (assembly combos)

| Scenario | Recommended setting |
|---|---|
| Compact field inside a form row | `editorType: "inline"`, `alwaysShowToolbar: false`, cell ≥ 3 row spans. |
| Dedicated body editor (email, case description) | `editorType: "classic"`, `alwaysShowToolbar: true`, `maxContentHeight: "400px"`. |
| Body inside a scrollable details panel | `editorType: "inline"`, `maxContentHeight: "320px"` to enable internal scrolling. |

---

## 4. Copy-paste minimal example

Classic-mode body editor on an Activity page, bound to `Activity.Body`:

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "ActivityBodyEditor",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.RichTextEditor",
    "label": "#ResourceString(ActivityBodyEditor_Label)#",
    "control": "$Activity_Body",
    "editorType": "classic",
    "loadAttachments": true,
    "maxContentHeight": "400px",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 6 }
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
      "Activity_Body": { "modelConfig": { "path": "PDS.Body" } }
    }
  }
}
```

---

## 5. Common pitfalls

1. **Binding to a non-`RICH_TEXT` column** — `Text (1)` stores raw HTML as plain string; downstream consumers that expect plain text break. Use `RICH_TEXT (43)` and expose `plainText` for search/preview.
2. **`useTemplates: true` without a `selectTemplate` handler** — the button renders but clicking it has no visible effect.
3. **`maxContentHeight` smaller than the toolbar height** — the toolbar still renders fully and pushes the content area to zero. Set `maxContentHeight` ≥ `120px`.
4. **Removing platform buttons via `editorConfig.removeButtons` while keeping the underlying feature on** — e.g. hiding the file button while `loadAttachments: true`. Disable the feature flag instead.

---

## 6. Quick checklist

- [ ] `insert` op with `type: "crt.RichTextEditor"`, unique `name`, `propertyName: "items"`.
- [ ] `control: "$<attr>"` references an attribute bound to a `RICH_TEXT (43)` column.
- [ ] `editorType` matches the design (`"inline"` for compact rows, `"classic"` for full editor cards).
- [ ] `maxContentHeight` set when the layout cell is bounded.
- [ ] `layoutConfig` provided with generous `colSpan` / `rowSpan`.
- [ ] If `labelPosition: "hidden"`, `ariaLabel` is provided.
- [ ] Resource keys for `label`, `placeholder`, `tooltip` are registered in the schema's `Resources`.
