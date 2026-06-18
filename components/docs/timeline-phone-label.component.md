# How to Add a Timeline Phone Label (`crt.TimelinePhoneLabel`) to a Freedom UI Page

> Audience: code agent inserting `crt.TimelinePhoneLabel` into a Creatio Freedom UI page schema.
> `crt.TimelinePhoneLabel` is an **internal display cell** used by `crt.TimelineTile` to render a
> phone-type column value as a clickable `tel:` link; it is **not** inserted directly into the page
> `viewConfigDiff`.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.TimelineTile` (column renderer — not a top-level page insert)
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.TimelineTile"` that includes phone-type columns in its `data.columns` array. |

`crt.TimelinePhoneLabel` is **not** a standalone page element. The `crt.TimelineTile` component selects
it automatically for columns whose data type matches a phone value.

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
  "name": "TimelineTile_Call",
  "parentName": "Timeline",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TimelineTile",
    "linkedColumn": "Contact",
    "sortedByColumn": "StartDate",
    "data": {
      "schemaName": "Activity",
      "schemaType": "Call",
      "columns": [
        {
          "columnName": "Phone",
          "columnLayout": { "column": 1, "row": 2, "colSpan": 6, "rowSpan": 1 }
        }
      ]
    },
    "visible": true
  }
}
```

---

## 3. Property reference

Full `inputs` for `crt.TimelinePhoneLabel` are in `ComponentRegistry.json` under
`componentType: "crt.TimelinePhoneLabel"`. Inputs are `caption` (column header label) and `value`
(phone number string shown as a `tel:` link).

---

## 4. Shape of types not in `references.typeDefinitions`

Not applicable for this component.

---

## 5. Copy-paste minimal example

```jsonc
// crt.TimelinePhoneLabel is selected automatically for phone-typed columns inside crt.TimelineTile.
{
  "operation": "insert",
  "name": "TimelineTile_Call",
  "parentName": "Timeline",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TimelineTile",
    "linkedColumn": "Contact",
    "sortedByColumn": "StartDate",
    "data": {
      "schemaName": "Activity",
      "schemaType": "Call"
    },
    "visible": true
  }
}
```

---

## 6. Driving from page state

Not applicable — rendered internally by `crt.TimelineTile`, not directly bound to page attributes.

---

## 7. Common pitfalls

1. **Do not insert `crt.TimelinePhoneLabel` directly.** It is an internal renderer component chosen automatically by `crt.TimelineTile` for phone columns.
2. **`value` must be a phone string.** Non-phone strings render without a `tel:` link.
3. **Do not confuse with `crt.TimelineEmailLabel`.** Each label type targets a specific data kind.
4. **`caption` should be localizable.** Use `#ResourceString(<key>)#` for proper i18n support.
5. **No outputs or handlers.** This component has no `@CrtOutput` events to wire in `handlers`.

---

## 8. Quick checklist

- [ ] Use `crt.TimelineTile` as the direct page insert; `crt.TimelinePhoneLabel` is selected automatically for phone columns.
- [ ] `caption` is a non-empty localizable string.
- [ ] `value` is a valid phone number string for the `tel:` link to work.
- [ ] Do not wire `crt.TimelinePhoneLabel` to `handlers` — it has no outputs.
