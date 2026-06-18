# How to Use a DCM Stage Editing Cell (`crt.TableDcmStageEditingCell`) in a DataGrid Column

> Audience: code agent configuring a `crt.DataGrid` column to render an editable DCM stage dropdown.
>
> `crt.TableDcmStageEditingCell` is the **edit-mode** counterpart of `crt.TableDcmStageCell`; it renders
> a lookup dropdown pre-populated with DCM stages and writes the selected stage ID back via a `FormControl`.
> It is embedded in a DataGrid column's edit-mode `cellView` via `DcmStageEditingCellViewElementConfigCreator`.

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: embedded inside a `crt.DataGrid` column edit-mode `cellView` — not a standalone view element
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` (DataGrid `columns`) | A column whose edit-mode `cellView` is set to `crt.TableDcmStageEditingCell` by the platform via `DcmStageEditingCellViewElementConfigCreator`. |

The platform's `DcmStageEditingCellViewElementConfigCreator` constructs the edit-mode `cellView` with
`control`, `dcmStages: '$DcmStages'`, and an empty `column` automatically when editing is enabled.

---

## 2. Step-by-step recipe

### 2.1 Enable editing on a DCM stage column

Configure `features.editable.enable: true` on the DataGrid and declare the stage column. The platform
selects `crt.TableDcmStageEditingCell` as the editing renderer automatically.

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
        "referenceSchemaName": "DcmCaseStage"
      }
    ],
    "features": {
      "editable": { "enable": true }
    }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.TableDcmStageEditingCell` are in `ComponentRegistry.json` under
`componentType: "crt.TableDcmStageEditingCell"`. This guide covers only the assembly mechanics.

---

## 5. Copy-paste minimal example

```jsonc
// Edit-mode cellView (constructed by the platform — shown for reference only)
{
  "type": "crt.TableDcmStageEditingCell",
  "control": "$GridItems.GridDS_Stage",
  "dcmStages": "$DcmStages",
  "column": {}
}
```

---

## 7. Common pitfalls

1. **`dcmStages` not populated.** Without a valid `$DcmStages` attribute, the editing dropdown is empty and the user cannot select a stage.
2. **`control` not a `FormControl` instance.** The component checks `value instanceof FormControl`; if `control` is not a real `FormControl`, value changes are not written back.
3. **Using without `features.editable.enable: true`.** The editing cell is only activated in edit mode; without the feature flag the read-only `crt.TableDcmStageCell` is shown instead.

---

## 8. Quick checklist

- [ ] DataGrid `features.editable.enable` is `true`.
- [ ] `$DcmStages` attribute is declared and populated in `viewModelConfigDiff`.
- [ ] The column's `referenceSchemaName` matches the DCM stage entity.
- [ ] No direct `cellView` configuration needed — the platform handles editing-mode dispatch.
