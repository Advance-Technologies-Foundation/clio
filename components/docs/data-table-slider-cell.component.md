# How to Add a Slider Table Cell (`crt.TableSliderCell`) to a Freedom UI Page

> Audience: code agent inserting `crt.TableSliderCell` into a Creatio Freedom UI page schema.
> A `crt.TableSliderCell` is a read-only display cell used inside a `crt.DataGrid` column to render a numeric value as a progress/slider bar with optional color styling. It is wired as the `cellView` property of a column definition.

## Metadata
- **Category**: display
- **Container**: no
- **Parent types**: `crt.DataGrid` column definition (as `cellView` value)
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit
| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A column in a `crt.DataGrid` with `"cellView": { "type": "crt.TableSliderCell", "value": "...", "percentage": "...", "color": "..." }`. |

`crt.TableSliderCell` is **not** inserted as an independent element with `parentName`/`propertyName: "items"`. It lives inside the `cellView` slot of a column definition.

### 1.1 Naming convention
```
// cellView object has no `name` field.
// Bind the slider fill percentage via "percentage" and the bar color via "color".
```

---

## 2. Step-by-step recipe

### 2.1 Wire `crt.TableSliderCell` as `cellView` inside a `crt.DataGrid` column

```jsonc
{
  "id": "d4e5f6a7-0000-0000-0000-000000000001",
  "code": "MyDetail_Probability",
  "caption": "#ResourceString(MyDetail_Probability)#",
  "dataValueType": 7,
  "cellView": {
    "type": "crt.TableSliderCell",
    "value": "$MyDetail.MyDetail_Probability",
    "percentage": "$MyDetail.MyDetail_Probability",
    "color": "#4CAF50"
  }
}
```

`percentage` drives the visual fill of the bar (0–100). `color` is a CSS color string for the filled portion.

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.TableSliderCell` are in `ComponentRegistry.json` under `componentType: "crt.TableSliderCell"`. This guide covers
only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

Cell `inputs` available at runtime (from the registry):
```ts
interface TableSliderCellInputs {
  column: BaseColumnDefinition;  // injected by crt.DataGrid
  record: DataItem;              // injected by crt.DataGrid
  value: TValue;                 // bound via "value": "$..." in cellView
  color: string;                 // CSS color for the slider bar fill
  percentage: number;            // 0–100, controls the visual fill width
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — DataGrid with a slider column
{
  "operation": "insert",
  "name": "OpportunitiesGrid",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.DataGrid",
    "items": "$OpportunitiesDetail",
    "primaryColumnName": "OpportunitiesDetail_Id",
    "columns": [
      {
        "id": "d4e5f6a7-0000-0000-0000-000000000001",
        "code": "OpportunitiesDetail_Probability",
        "caption": "#ResourceString(OpportunitiesDetail_Probability)#",
        "dataValueType": 7,
        "cellView": {
          "type": "crt.TableSliderCell",
          "value": "$OpportunitiesDetail.OpportunitiesDetail_Probability",
          "percentage": "$OpportunitiesDetail.OpportunitiesDetail_Probability",
          "color": "#4CAF50"
        }
      }
    ],
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 12, "rowSpan": 6 }
  }
}
```

---

## 7. Common pitfalls

- **Trying to `insert` as a standalone view element.** `crt.TableSliderCell` is only valid inside a `crt.DataGrid` column's `cellView`.
- **`percentage` outside 0–100.** Values outside this range cause the bar to over- or under-fill. Clamp or validate the data server-side before binding.
- **Omitting `color`.** Without `color` the bar renders with the default theme color. Always provide `color` when you want a specific visual indicator (e.g. green for high probability, red for low).
- **Using a non-CSS color string.** `color` is passed directly to the element's style; use hex `#RRGGBB`, `rgb(...)`, or CSS variable references that the platform resolves.
- **Binding `value` and `percentage` to different columns.** Mismatching them causes the label and bar fill to disagree visually. Bind both to the same numeric attribute unless you intentionally decouple them.

---

## 8. Quick checklist

- [ ] `crt.TableSliderCell` placed in `cellView` of a `crt.DataGrid` column, not as a top-level view element.
- [ ] `value` binding is `"$<collectionAttr>.<columnCode>"`.
- [ ] `percentage` binding is the same numeric attribute (0–100 range).
- [ ] `color` is a valid CSS color string.
- [ ] `id` on the column definition is a unique GUID.
