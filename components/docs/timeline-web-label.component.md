# How to Add a Timeline Web Label (`crt.TimelineWebLabel`) to a Freedom UI Page

> Audience: code agent inserting `crt.TimelineWebLabel` into a Creatio Freedom UI page schema.
> `crt.TimelineWebLabel` is an **internal display cell** used by `crt.TimelineTile` to render a
> web URL column value as a clickable external link; it is **not** inserted directly into the page
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
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.TimelineTile"` that includes web-type columns in its `data.columns` array. |

`crt.TimelineWebLabel` is **not** a standalone page element. The `crt.TimelineTile` component selects
it automatically for columns whose data type matches a web/URL value.

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
  "name": "TimelineTile_Web",
  "parentName": "Timeline",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TimelineTile",
    "linkedColumn": "Contact",
    "sortedByColumn": "CreatedOn",
    "data": {
      "schemaName": "Contact",
      "columns": [
        {
          "columnName": "LinkedIn",
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

Full `inputs` for `crt.TimelineWebLabel` are in `ComponentRegistry.json` under
`componentType: "crt.TimelineWebLabel"`. Inputs are `caption` (column header label) and `value`
(the URL string to render as an external link).

---

## 4. Shape of types not in `references.typeDefinitions`

Not applicable for this component.

---

## 5. Copy-paste minimal example

```jsonc
// crt.TimelineWebLabel is selected automatically for web/URL-typed columns inside crt.TimelineTile.
{
  "operation": "insert",
  "name": "TimelineTile_Web",
  "parentName": "Timeline",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TimelineTile",
    "linkedColumn": "Contact",
    "sortedByColumn": "CreatedOn",
    "data": {
      "schemaName": "Contact"
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

1. **Do not insert `crt.TimelineWebLabel` directly.** It is an internal renderer chosen automatically for web-typed columns.
2. **`value` must be a valid URL string.** Partial URLs or non-URL strings may render as broken links.
3. **Do not confuse with `crt.TimelineEmailLabel` or `crt.TimelinePhoneLabel`.** Each label targets a specific data kind.
4. **`caption` should be localizable.** Use `#ResourceString(<key>)#` for proper i18n support.
5. **No outputs or handlers.** This component has no `@CrtOutput` events to wire in `handlers`.

---

## 8. Quick checklist

- [ ] Use `crt.TimelineTile` as the direct page insert; `crt.TimelineWebLabel` is selected automatically for web/URL columns.
- [ ] `caption` is a non-empty localizable string.
- [ ] `value` is a valid URL string for the external link to work.
- [ ] Do not wire `crt.TimelineWebLabel` to `handlers` — it has no outputs.
