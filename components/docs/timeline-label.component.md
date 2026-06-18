# How to Add a Timeline Label (`crt.TimelineLabel`) to a Freedom UI Page

> Audience: code agent inserting `crt.TimelineLabel` into a Creatio Freedom UI page schema.
> `crt.TimelineLabel` is an **internal display cell** used by `crt.TimelineTile` to render a generic
> text or date column value in the timeline; it is **not** inserted directly into the page `viewConfigDiff`.

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

`crt.TimelineLabel` is **not** a standalone page element. The `crt.TimelineTile` component resolves which
label component to use based on the column data type. `crt.TimelineLabel` handles generic text and date/datetime
columns, formatting dates appropriately when `dataValueType` is `Date` or `DateTime`.

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
      "uId": "c449d832-a4cc-4b01-b9d5-8a12c42a9f89",
      "schemaName": "Activity",
      "schemaType": "Activity",
      "columns": [
        {
          "columnName": "Title",
          "columnLayout": { "column": 1, "row": 1, "colSpan": 12, "rowSpan": 1 }
        },
        {
          "columnName": "StartDate",
          "columnLayout": { "column": 1, "row": 2, "colSpan": 6, "rowSpan": 1 }
        }
      ]
    },
    "visible": true
  }
}
```

The runtime renders `crt.TimelineLabel` for text and date-typed columns. For date/datetime columns, the
component's `isDate` getter returns `true` and formats the value using the date pipe.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, and `values` for `crt.TimelineLabel` are in `ComponentRegistry.json`
under `componentType: "crt.TimelineLabel"`. Inputs are `caption` (column header text), `value` (display
string), and `dataValueType` (controls date formatting).

---

## 4. Shape of types not in `references.typeDefinitions`

`dataValueType` is the `DataValueType` enum from `@creatio-devkit/common`. Date formatting is triggered when
`dataValueType === DataValueType.Date || dataValueType === DataValueType.DateTime`.

---

## 5. Copy-paste minimal example

```jsonc
// crt.TimelineLabel is selected automatically for text/date columns inside crt.TimelineTile.
// Configure the parent tile with the desired column types.
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
        { "columnName": "Title", "columnLayout": { "column": 1, "row": 1, "colSpan": 12, "rowSpan": 1 } }
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

1. **Do not insert `crt.TimelineLabel` directly.** It is an internal renderer component chosen by `crt.TimelineTile`.
2. **Date formatting is `dataValueType`-driven.** Only `DataValueType.Date` and `DataValueType.DateTime` trigger date pipe formatting; other types display the raw string.
3. **Do not confuse with `crt.TimelineEmailLabel` or `crt.TimelinePhoneLabel`.** Use those for email and phone columns respectively; the tile selects the correct renderer automatically.
4. **`caption` should be localizable.** Pass `#ResourceString(<key>)#` for proper i18n support.
5. **`value` is expected to be a string.** Non-string values may not render correctly without the `dataValueType` hint.

---

## 8. Quick checklist

- [ ] Use `crt.TimelineTile` as the direct page insert; `crt.TimelineLabel` is selected automatically.
- [ ] For date columns, ensure `dataValueType` is set to `DataValueType.Date` or `DataValueType.DateTime` on the tile column config.
- [ ] `caption` is a non-empty localizable string.
- [ ] Do not wire `crt.TimelineLabel` to `handlers` — it has no outputs.
