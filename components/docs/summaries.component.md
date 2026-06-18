# How to Add a Summaries (`crt.Summaries`) to a Freedom UI Page

> Audience: code agent inserting a `crt.Summaries` into a Creatio Freedom UI page schema.
>
> A `crt.Summaries` is a collapsible panel that displays a horizontal row of `crt.SummaryItem` children,
> each showing an aggregated value (count, sum, max, etc.) computed from a datasource; it also supports
> a title, an expand/collapse toggle, and an optional actions menu.

## Metadata

- **Category**: display
- **Container**: yes (children are `crt.SummaryItem` in the `items` slot)
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.HeaderContainer`, root page container
- **Typical children**: `crt.SummaryItem`

---

## 1. Mental model — the 3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.Summaries"` and an empty `items: []`. **Always present.** |
| 2 | `viewConfigDiff` | Separate `insert` ops for each `crt.SummaryItem` child, each pointing `parentName` at the summaries element. |
| 3 | `viewModelConfigDiff` | An attribute for the `expanded` flag if you want to bind collapse state. |

`crt.Summaries` owns no datasource of its own; each `crt.SummaryItem` child is wired separately to a
datasource attribute via designer options.

### 1.1 Naming convention

```
Summaries_<id>             // view element name
SummaryItem_<id>           // each child item
$Summaries_<id>_Expanded   // $-prefix attribute for collapse state (when bound)
```

---

## 2. Step-by-step recipe

### 2.1 Insert the summaries panel (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "Summaries_abc123",
  "parentName": "MainHeaderBottom",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Summaries",
    "items": [],
    "visible": true,
    "expanded": "$Summaries_abc123_Expanded"
  }
}
```

### 2.2 Declare the expanded attribute in `viewModelConfigDiff`

```jsonc
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "Summaries_abc123_Expanded": { "value": true }
  }
}
```

### 2.3 Insert each `crt.SummaryItem` child

See the `crt.SummaryItem` guide for the full child recipe. In brief:

```jsonc
{
  "operation": "insert",
  "name": "SummaryItem_abc123",
  "parentName": "Summaries_abc123",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.SummaryItem",
    "label": "#ResourceString(SummaryItem_abc123_label)#"
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.Summaries` are in `ComponentRegistry.json` under `componentType: "crt.Summaries"`. This guide
covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

`SummaryItemViewConfig` (for the `items` input) has this shape when set declaratively:

```ts
interface SummaryItemViewConfig {
  type: 'crt.SummaryItem';    // must be 'crt.SummaryItem'
  name: string;               // unique element name
  label?: string;             // visible label text
  value?: string | boolean | number | null; // display value
  readonly?: string | boolean;
  visible?: boolean | string;
  bindTo?: string;            // $-prefixed attribute for the computed value
}
```

In practice the `value`/`bindTo` fields are populated by the page runtime; you typically provide only
`type`, `name`, and `label` in the schema diff, leaving `value` empty for the runtime to fill.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff — summaries panel
{
  "operation": "insert",
  "name": "Summaries_e4t4p8w",
  "parentName": "MainHeaderBottom",
  "propertyName": "items",
  "index": 1,
  "values": {
    "type": "crt.Summaries",
    "items": [],
    "visible": true,
    "expanded": "$Summaries_e4t4p8w_Expanded"
  }
}
```

```jsonc
// viewConfigDiff — one summary item child
{
  "operation": "insert",
  "name": "SummaryItem_qbgok9u",
  "parentName": "Summaries_e4t4p8w",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.SummaryItem",
    "label": "#ResourceString(SummaryItem_qbgok9u_label)#"
  }
}
```

```jsonc
// viewModelConfigDiff — expanded flag
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "Summaries_e4t4p8w_Expanded": { "value": true }
  }
}
```

---

## 6. Driving from page state

```jsonc
// viewConfigDiff.values — bind the readonly flag
"readonly": "$Summaries_abc123_Readonly"

// viewModelConfigDiff
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "Summaries_abc123_Readonly": { "value": false }
  }
}
```

`readonly` is `propertyBindable` and prevents editing of editable summary items when `true`.

---

## 7. Common pitfalls

1. **Empty `items: []` omitted.** The runtime expects the `items` array to be present even when empty; omitting it causes child inserts to fail silently.
2. **`crt.SummaryItem` not nested under the correct parent.** Each child must use `parentName` pointing to the `crt.Summaries` element name; using the wrong parent name leaves items in an incorrect slot.
3. **`expanded` not declared in `viewModelConfigDiff`.** If `expanded` is set to `"$Attribute"` but the attribute is not declared, the panel always starts collapsed.
4. **Mixing `items` slot with non-`crt.SummaryItem` types.** The component filters the `items` array to `type === 'crt.SummaryItem'`; any other type is silently dropped.
5. **`title` not a `#ResourceString`.** Raw strings work but are not localized; prefer `#ResourceString(<key>)#` for multi-language deployments.
6. **`actions` items missing `type: "crt.MenuItem"`.** The actions menu renders `CrtMenuItemViewElementConfig` objects; each must have a `type` and a `clicked` binding.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.Summaries"`, unique `name`, valid `parentName`, `propertyName: "items"`.
- [ ] `items: []` present in `values`.
- [ ] Each `crt.SummaryItem` child is a separate `insert` op with `parentName` pointing to this element.
- [ ] `expanded` attribute declared in `viewModelConfigDiff` if bound.
- [ ] `title` uses `#ResourceString(<key>)#` for localized text.
