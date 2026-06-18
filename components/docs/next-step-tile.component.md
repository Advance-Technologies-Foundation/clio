# How to Add a Next Step Tile (`crt.NextStepTile`) to a Freedom UI Page

> Audience: code agent inserting a `crt.NextStepTile` into a Creatio Freedom UI page schema.
>
> `crt.NextStepTile` is the individual card rendered inside a `crt.NextSteps` widget for activity-type
> records. It is created automatically from the `nextSteps` collection via `tilesMap.tileClassName` —
> you do not insert it directly. It displays owner info, due date, a title link, and a complete action button.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.NextSteps` (internal, created from `tilesMap`)
- **Typical children**: none

---

## 1. Mental model

`crt.NextStepTile` is an internal tile component. You never insert it as a standalone `viewConfigDiff`
operation. The `crt.NextSteps` component selects it automatically for activity-type records (e.g. `Activity`).
Custom tile types can be set per entity via `crt.NextSteps.tilesMap`.

The registry exposes three inputs (`isSelected`, `record`, `tileSizeClasses`) that are all set by the parent
`crt.NextSteps` at runtime — do not set them manually.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.NextStepTile` are in `ComponentRegistry.json` under `componentType: "crt.NextStepTile"`.

---

## 5. Copy-paste minimal example

`crt.NextStepTile` has no standalone `viewConfigDiff` entry. Configure the parent instead:

```jsonc
// viewConfigDiff — configure crt.NextSteps; tiles are created automatically
{
  "operation": "insert",
  "name": "NextSteps",
  "values": {
    "type": "crt.NextSteps",
    "masterSchemaId": "$Id",
    "masterSchemaName": "Lead",
    "layoutConfig": {
      "colSpan": 2,
      "column": 1,
      "row": 1,
      "rowSpan": 1
    }
  },
  "parentName": "NextStepsTabContainer",
  "propertyName": "items",
  "index": 0
}
```

---

## 7. Common pitfalls

1. **Trying to insert `crt.NextStepTile` directly.** It is an internal component; only `crt.NextSteps` should reference it via `tilesMap`.
2. **Overriding `tilesMap.Activity` without keeping `tileClassName`.** The tile class is required; a missing `tileClassName` will render nothing for Activity records.
