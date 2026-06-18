# How to Use a DCM Stage Cell (`crt.TableDcmStageCell`) in a DataGrid Column

> Audience: code agent configuring a `crt.DataGrid` column to render DCM (Dynamic Case Management) stage values.
>
> `crt.TableDcmStageCell` renders a DCM stage identifier as a colored badge by matching the cell `value`
> against a `dcmStages` lookup list; it looks up `displayValue` and `primaryColorValue` from the stage
> list. It is embedded in a DataGrid column's `cellView` via `DcmStageCellViewElementConfigCreator`.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: embedded inside a `crt.DataGrid` column `cellView` — not a standalone view element
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` (DataGrid `columns`) | A column with `cellView: { type: "crt.TableDcmStageCell", ... }`. |

The platform's `DcmStageCellViewElementConfigCreator` sets `value`, `bindTo`, and
`dcmStages: '$DcmStages'` automatically when it creates the `cellView`.

---

## 2. Step-by-step recipe

### 2.1 Add a DCM stage column to `crt.DataGrid`

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
        "code": "GridDS_Stage",
        "path": "Stage",
        "caption": "#ResourceString(GridDS_Stage)#",
        "dataValueType": 10,
        "referenceSchemaName": "DcmCaseStage",
        "cellView": {
          "type": "crt.TableDcmStageCell",
          "value": "$GridItems.GridDS_Stage",
          "bindTo": "$GridItems.GridDS_Stage",
          "dcmStages": "$DcmStages"
        }
      }
    ]
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.TableDcmStageCell` are in `ComponentRegistry.json` under `componentType: "crt.TableDcmStageCell"`.
This guide covers only the assembly mechanics.

---

## 5. Copy-paste minimal example

```jsonc
// cellView inside a DataGrid column definition
{
  "type": "crt.TableDcmStageCell",
  "value": "$GridItems.GridDS_Stage",
  "bindTo": "$GridItems.GridDS_Stage",
  "dcmStages": "$DcmStages"
}
```

The component matches `value` against `dcmStages[].value` to extract `displayValue` and
`primaryColorValue` for the badge.

---

## 7. Common pitfalls

1. **`dcmStages` not bound to a valid `$Attribute`.** The cell renders blank if `dcmStages` is `null` or not an array of `LookupValue` objects with `value`, `displayValue`, and `primaryColorValue`.
2. **`value` not a recognized stage identifier.** If `value` does not match any entry in `dcmStages`, both the badge label and color remain empty.
3. **Missing `$DcmStages` attribute in `viewModelConfigDiff`.** The `$DcmStages` attribute must be declared and populated by the page model; without it the stage list is empty.

---

## 8. Quick checklist

- [ ] Column `cellView.type` set to `"crt.TableDcmStageCell"`.
- [ ] `value` bound to the datasource attribute holding the stage ID.
- [ ] `dcmStages` bound to `"$DcmStages"` (the page-level DCM stage list attribute).
- [ ] `$DcmStages` attribute declared and loaded in `viewModelConfigDiff`.
