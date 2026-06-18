# How to Add a Timeline Email Label (`crt.TimelineEmailLabel`) to a Freedom UI Page

> Audience: code agent inserting `crt.TimelineEmailLabel` into a Creatio Freedom UI page schema.
> `crt.TimelineEmailLabel` is an **internal display cell** used by `crt.TimelineTile` to render an email-type
> column value in the timeline; it is **not** inserted directly into the page `viewConfigDiff`.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.TimelineTile` (column renderer — not a top-level page insert)
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.TimelineTile"` that references this label type through its `data.columns` array. |

`crt.TimelineEmailLabel` is **not** a standalone page element. The `crt.TimelineTile` component resolves which
label component to use based on the column data type and schema. You configure the timeline tile's column list;
the runtime selects and renders `crt.TimelineEmailLabel` automatically for email-typed columns.

### 1.1 Naming convention

```
TimelineTile_<id>     // the parent tile view element name
```

---

## 2. Step-by-step recipe

### 2.1 Insert the timeline tile (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "TimelineTile_Email",
  "parentName": "Timeline",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TimelineTile",
    "linkedColumn": "Contact",
    "sortedByColumn": "SendDate",
    "ownerColumn": "SenderContact",
    "data": {
      "uId": "c449d832-a4cc-4b01-b9d5-8a12c42a9f89",
      "schemaName": "Activity",
      "schemaType": "Email",
      "filter": {
        "columnName": "Type",
        "columnValue": "e2831dec-cfc0-df11-b00f-001d60e938c6"
      },
      "columns": [
        {
          "columnName": "Title",
          "columnLayout": { "column": 1, "row": 1, "colSpan": 12, "rowSpan": 1 }
        }
      ]
    },
    "iconPosition": "only-icon",
    "icon": "star-tab-icon",
    "visible": true
  }
}
```

The runtime renders `crt.TimelineEmailLabel` for columns whose data matches an email value type.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, and `values` for `crt.TimelineEmailLabel` are in `ComponentRegistry.json`
under `componentType: "crt.TimelineEmailLabel"`. Inputs are `caption` (column header text) and `value`
(the email address string to display with a mailto link).

---

## 4. Shape of types not in `references.typeDefinitions`

Not applicable for this component.

---

## 5. Copy-paste minimal example

```jsonc
// This component is rendered internally by crt.TimelineTile.
// To get email labels in a timeline, insert a crt.Timeline + crt.TimelineTile with email-type columns.
{
  "operation": "insert",
  "name": "TimelineTile_Email",
  "parentName": "Timeline",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TimelineTile",
    "linkedColumn": "Contact",
    "sortedByColumn": "SendDate",
    "data": {
      "schemaName": "Activity",
      "schemaType": "Email"
    },
    "visible": true
  }
}
```

---

## 6. Driving from page state

Not applicable — this component is rendered internally by `crt.TimelineTile` and is not directly bound
to page attributes.

---

## 7. Common pitfalls

1. **Do not insert `crt.TimelineEmailLabel` directly.** It is an internal renderer component; inserting it as a top-level page element will produce no visible output.
2. **Email display depends on the column type.** The tile runtime picks `crt.TimelineEmailLabel` when the column data matches an email schema type.
3. **`caption` must not be empty.** An empty caption causes the column header area to render as a blank label.
4. **`value` is expected to be a plain email string.** Non-email strings render without a `mailto:` link.
5. **Do not confuse with `crt.TimelineLabel`.** `crt.TimelineLabel` is for generic text; `crt.TimelineEmailLabel` wraps the value in a clickable email link.

---

## 8. Quick checklist

- [ ] Use `crt.TimelineTile` as the direct page insert; `crt.TimelineEmailLabel` is selected automatically.
- [ ] Set `data.schemaType` appropriately on the parent tile to trigger email-label rendering.
- [ ] Confirm `caption` is a non-empty localizable string for accessibility.
- [ ] Do not wire `crt.TimelineEmailLabel` to `handlers` — it has no outputs.
