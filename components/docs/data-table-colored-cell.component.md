# How to Use a Colored Cell (`crt.TableColoredCell`) in a DataGrid Column

> Audience: code agent configuring a `crt.DataGrid` column to render colored lookup values as color chips.
>
> `crt.TableColoredCell` renders a lookup value whose `primaryColorValue` is used to fill a colored badge
> chip; it optionally supports a link click action. It is embedded in a DataGrid column's `cellView`
> configuration via `DataGridColoredCellViewElementConfig`.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: embedded inside a `crt.DataGrid` column `cellView` — not a standalone view element
- **Typical children**: none

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` (DataGrid `columns`) | A column with `cellView: { type: "crt.TableColoredCell", ... }`. |
| 2 | `handlers` (optional) | Handler for the `clicked` request if the chip should navigate or trigger logic. |

---

## 2. Step-by-step recipe

### 2.1 Add a colored-cell column to `crt.DataGrid`

```jsonc
{
  "operation": "insert",
  "name": "DataGrid_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.DataGrid",
    "items": "$GridItems",
    "primaryColumnName": "GridDS_Id",
    "columns": [
      {
        "id": "col-guid-here",
        "code": "GridDS_Status",
        "path": "Status",
        "caption": "#ResourceString(GridDS_Status)#",
        "dataValueType": 10,
        "referenceSchemaName": "Status",
        "cellView": {
          "type": "crt.TableColoredCell",
          "displayValue": "$GridItems.GridDS_Status",
          "caption": "$GridItems.GridDS_Status_displayValue",
          "disableLink": false,
          "href": "$GridItems.GridDS_Status_href",
          "mode": "external"
        }
      }
    ]
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.TableColoredCell` are in `ComponentRegistry.json` under `componentType: "crt.TableColoredCell"`. This
guide covers only the assembly mechanics.

---

## 4. Shape of `DataGridColoredCellViewElementConfig`

```ts
interface DataGridColoredCellViewElementConfig {
  type: 'crt.TableColoredCell';
  displayValue?: LookupValue;  // { primaryColorValue: string; displayValue: string; value: string }
  caption: string;             // plain text label shown inside the chip
  disableLink?: boolean;       // when true, the chip is not a clickable link
  href: string;                // navigation URL for the link
  mode?: 'external' | 'internal'; // link open mode
  clicked?: RequestBindingConfig | `$${string}`; // action on chip click
  target?: AnchorTarget;       // '_blank' | '_self' | '_parent' | '_top'
}
```

---

## 5. Copy-paste minimal example

```jsonc
// cellView inside a DataGrid column definition
{
  "type": "crt.TableColoredCell",
  "displayValue": "$GridItems.GridDS_StatusId",
  "caption": "$GridItems.GridDS_StatusId_displayValue",
  "disableLink": true
}
```

---

## 7. Common pitfalls

1. **`displayValue` not a `LookupValue`.** The chip color is derived from `displayValue.primaryColorValue`; if `displayValue` is a plain string the background stays transparent.
2. **`disableLink: false` without `href`.** Without a valid `href` binding the chip renders as a dead link; set `disableLink: true` when no navigation is intended.
3. **`clicked` output without a `handlers` entry.** The `clicked` event fires silently if no handler is registered for its request.
4. **Using this cell for non-lookup columns.** `crt.TableColoredCell` is designed for lookup columns with a `primaryColorValue`; plain text/numeric columns should use `crt.TableTextCell` instead.

---

## 8. Quick checklist

- [ ] Column `cellView.type` set to `"crt.TableColoredCell"`.
- [ ] `displayValue` bound to the lookup attribute (must be a `LookupValue` with `primaryColorValue`).
- [ ] `caption` bound to the display text of the lookup.
- [ ] `disableLink: true` if no navigation link is needed.
- [ ] `href` and `clicked` configured consistently if the chip should be clickable.
