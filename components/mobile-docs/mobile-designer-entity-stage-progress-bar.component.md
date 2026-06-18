# How to Add an Entity Stage Progress Bar (`crt.EntityStageProgressBar`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.EntityStageProgressBar` into a mobile page schema.
> Displays a horizontal DCM stage pipeline (e.g. Lead stages, Case stages) that lets users move
> a record forward or backward through its configured process stages.

## Metadata
- **Category**: interactive
- **Container**: false
- **Parent types**: any layout container that accepts full-width items (e.g. `crt.Scaffold`)
- **Typical children**: none

---
## 1. Mental model
`crt.EntityStageProgressBar` renders a horizontal row of stage chips. Tapping a chip fires the
`stageChanged` output, which can trigger a silent save or a deferred save. The component needs
three inputs to function: `entityName` (the DCM-enabled entity), `recordId` (the record whose
pipeline to show), and `value` (the currently active stage lookup value). At design time the
designer shell auto-fills `entityName` from the primary data source if it is not already set.

---
## 2. Clio operation
```jsonc
{
  "operation": "insert",
  "name": "StageBar",
  "values": {
    "type": "crt.EntityStageProgressBar",
    "entityName": "Lead",
    "recordId": "$Id",
    "value": "$PDS_Stage"
  },
  "parentName": "Scaffold",
  "propertyName": "items",
  "index": 0
}
```

---
## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.EntityStageProgressBar` are in
`ComponentRegistry.json` under `componentType: "crt.EntityStageProgressBar"`.

Key inputs (all `@CrtInput` on `CrtEntityStageProgressBarComponent`):

| Property | Type | Description |
|---|---|---|
| `entityName` | `string` | Entity schema name of the DCM stage pipeline (e.g. `"Lead"`). Auto-set by the designer from the primary data source. |
| `recordId` | `Guid` | Record identifier; triggers a `loadData` event to fetch the pipeline for this record. |
| `value` | `LookupValue` | Current stage as `{ value: Guid, displayValue: string }`; drives the active chip highlight. |
| `stages` | `Step[]` | Array of stage objects; populated by the `loadData` handler. |
| `allowedStages` | `DcmStageInfo[]` | Stages the user may navigate to from the current stage; set by `setAllowedStages`. |
| `stageConnections` | `StageConnection[]` | DCM connection rules used to compute `allowedStages`. |
| `saveOnChange` | `boolean` | When `true`, silently saves the record on stage change (default `false`). |

---
## 4. Copy-paste minimal example
```jsonc
{
  "operation": "insert",
  "name": "StageBar",
  "values": {
    "type": "crt.EntityStageProgressBar",
    "entityName": "Lead",
    "recordId": "$Id",
    "value": "$PDS_Stage"
  },
  "parentName": "Scaffold",
  "propertyName": "items",
  "index": 0
}
```

---
## 5. Common pitfalls
- **Missing data source**: the component requires the page to have a primary data source whose
  entity is DCM-enabled. If no data source exists the designer shows an info dialog; Clio should
  add the data source first.
- **Wrong `entityName`**: must be the entity schema name (e.g. `"Lead"`), not the caption. The
  mobile designer auto-fills this from the primary data source — override only when you need a
  non-primary entity.
- **`value` not a lookup**: `value` must be bound to a `LookupValue` attribute (an object with
  `value` and `displayValue`), not a plain string GUID. Binding to a raw ID column breaks the
  active-stage highlight.
