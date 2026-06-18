# How to Add a Summary Item (`crt.SummaryItem`) to a Freedom UI Page

> Audience: code agent inserting a `crt.SummaryItem` into a Creatio Freedom UI page schema.
>
> A `crt.SummaryItem` is a single labeled metric chip that lives inside a `crt.Summaries` panel; it
> displays a computed aggregate value (count, sum, max, etc.) with an optional actions menu and an
> error state.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.Summaries` only (always nested as a child of a summaries panel)
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.SummaryItem"` and a `label`. **Always present.** |

`crt.SummaryItem` is **view-only within the summaries slot** — it has no standalone datasource. The
runtime populates `value` from a computed expression configured through designer options; you do not
wire a `$Attribute` directly in the schema diff.

### 1.1 Naming convention

```
SummaryItem_<id>    // view element name; <id> = any short unique slug
```

The `parentName` must always reference a `crt.Summaries` element.

---

## 2. Step-by-step recipe

### 2.1 Insert the summary item (`viewConfigDiff` entry)

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
`crt.SummaryItem` are in `ComponentRegistry.json` under `componentType: "crt.SummaryItem"`. This
guide covers only the assembly mechanics.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff — summaries panel first
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
// viewConfigDiff — count summary item
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
// viewConfigDiff — another summary item (max date)
{
  "operation": "insert",
  "name": "SummaryItem_vmaqzsc",
  "parentName": "Summaries_e4t4p8w",
  "propertyName": "items",
  "index": 1,
  "values": {
    "type": "crt.SummaryItem",
    "label": "#ResourceString(SummaryItem_vmaqzsc_label)#"
  }
}
```

---

## 7. Common pitfalls

1. **Wrong `parentName`.** `crt.SummaryItem` must be a direct child of a `crt.Summaries` element; placing it under any other parent causes it to be invisible or misrendered.
2. **Omitting `label`.** Without a `label` the chip renders with no text, making it hard to identify. Always provide a localized label using `#ResourceString(<key>)#`.
3. **Manually setting `value`.** The `value` property is populated by the runtime from a computed aggregate; setting it statically in the diff overrides the runtime and produces a stale value.
4. **`error` without a corresponding validation.** Setting `error` directly in the diff makes the item always show as invalid; use `error` only when bound to a `$Attribute` that carries a runtime error message.
5. **`readonly: true` blocking expected edits.** When `readonly` is set the item's edit action (if any) is suppressed; bind it to an attribute if the read-only state should change at runtime.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.SummaryItem"`, unique `name`.
- [ ] `parentName` points to a `crt.Summaries` element.
- [ ] `propertyName: "items"`.
- [ ] `label` set (use `#ResourceString(<key>)#`).
- [ ] The parent `crt.Summaries` element's `insert` op comes before this one (lower index or earlier in the array).
