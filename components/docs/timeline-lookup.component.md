# How to Add a Timeline Lookup (`crt.TimelineLookup`) to a Freedom UI Page

> Audience: code agent inserting `crt.TimelineLookup` into a Creatio Freedom UI page schema.
> `crt.TimelineLookup` is an **internal display cell** used by `crt.TimelineTile` to render a lookup
> column value as a clickable link that opens the related record; it is **not** inserted directly into
> the page `viewConfigDiff`.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.TimelineTile` (column renderer — not a top-level page insert)
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.TimelineTile"` that references this lookup renderer through its `data.columns` array. |

`crt.TimelineLookup` is **not** a standalone page element. It renders a lookup field value as a link;
clicking it fires `crt.UpdateRecordRequest` to open the related entity's edit page. The component
automatically checks whether the related entity has an edit page before showing the link.

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
  "name": "TimelineTile_Task",
  "parentName": "Timeline",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TimelineTile",
    "linkedColumn": "Contact",
    "sortedByColumn": "StartDate",
    "ownerColumn": "Owner",
    "data": {
      "schemaName": "Activity",
      "schemaType": "Activity",
      "columns": [
        {
          "columnName": "Owner",
          "columnLayout": { "column": 1, "row": 2, "colSpan": 6, "rowSpan": 1 }
        }
      ]
    },
    "visible": true
  }
}
```

The runtime renders `crt.TimelineLookup` for columns whose value is a lookup (`{ entityName, recordId }`).

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, and `values` for `crt.TimelineLookup` are in `ComponentRegistry.json`
under `componentType: "crt.TimelineLookup"`. Inputs are `caption` (column header label) and `value`
(a `TimelineLookup` object with `entityName` and `recordId`).

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// TimelineLookup — the value shape expected by this component
interface TimelineLookup {
  entityName: string;   // schema name of the related entity (e.g. "Contact")
  recordId: string;     // GUID of the related record
  displayValue?: string; // optional pre-resolved display label
}
```

The component resolves whether the entity has an edit page at runtime via `NavigationUtils.hasEntityEditPage`.
When it does, clicking the label fires `crt.UpdateRecordRequest`.

---

## 5. Copy-paste minimal example

```jsonc
// crt.TimelineLookup is selected automatically for lookup-typed columns inside crt.TimelineTile.
{
  "operation": "insert",
  "name": "TimelineTile_Activity",
  "parentName": "Timeline",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TimelineTile",
    "linkedColumn": "Contact",
    "sortedByColumn": "StartDate",
    "data": {
      "schemaName": "Activity",
      "schemaType": "Activity",
      "columns": [
        {
          "columnName": "Owner",
          "columnLayout": { "column": 1, "row": 2, "colSpan": 6, "rowSpan": 1 }
        }
      ]
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

1. **Do not insert `crt.TimelineLookup` directly.** It is an internal renderer component chosen automatically by `crt.TimelineTile` for lookup columns.
2. **The link only appears when the entity has an edit page.** `NavigationUtils.hasEntityEditPage` is called at mount time; if the entity lacks a page, the label renders as plain text.
3. **`value` must be a `TimelineLookup` object.** Passing a raw string produces a non-clickable, empty label.
4. **`entityName` is case-sensitive.** Use the exact schema name (e.g. `"Contact"`, not `"contact"`).
5. **Navigation fires `crt.UpdateRecordRequest` — not a page router link.** The handler chain processes the request; ensure your page or its parent scope has an appropriate handler registered.

---

## 8. Quick checklist

- [ ] Use `crt.TimelineTile` as the direct page insert; `crt.TimelineLookup` is selected automatically for lookup columns.
- [ ] The lookup column value object has both `entityName` and `recordId` populated.
- [ ] `caption` is a non-empty localizable string for the column header.
- [ ] Do not wire `crt.TimelineLookup` to `handlers` — it has no `@CrtOutput` events.
