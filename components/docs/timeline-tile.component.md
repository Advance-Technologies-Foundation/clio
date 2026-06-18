# How to Add a Timeline Tile (`crt.TimelineTile`) to a Freedom UI Page

> Audience: code agent inserting a `crt.TimelineTile` into a Creatio Freedom UI page schema.
>
> `crt.TimelineTile` describes a per-entity tile inside a `crt.Timeline`. Each child tile
> tells the timeline which entity it can display and how to render its head/body columns.
> Tiles are **never** stand-alone elements; they are always children of a `crt.Timeline`.

For the outer container, see crt.Timeline guide.

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.Timeline`
- **Typical children**: none — tile columns are declared via `values.data.columns[]`, not as child elements

---

## 1. Mental model

```
crt.Timeline                <-- parent container
├── crt.TimelineTile        <-- describes "Email" tiles      (this doc)
├── crt.TimelineTile        <-- describes "Task" tiles
└── crt.TimelineTile        <-- describes "Feed" tiles
```

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.TimelineTile"`, `parentName: "<Timeline_id>"`, top-level metadata, and a `data` block describing the entity. |

### 1.1 `contentSlots`

```
contentSlots: ['items', 'filters']
compatibleAPIs: { Filtration: { enable: true, aggregation: true } }
```

The `items` slot is internally used by the platform to project the tile head template; the
`filters` slot is for tile-specific filter chips. Both are rarely populated by hand at
MMP scale.

### 1.2 Naming convention

```
TimelineTile_<Kind>          // e.g. TimelineTile_Email, TimelineTile_Task, TimelineTile_File
```

---

## 2. Step-by-step recipe

### 2.1 Insert a tile (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "TimelineTile_Email",
  "parentName": "Timeline",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TimelineTile",
    "classes": ["view-element"],

    // -- top-level metadata (canonical placement in real schemas) --
    "linkedColumn":   "Contact",
    "sortedByColumn": "SendDate",
    "ownerColumn":    "SenderContact",
    "iconId":         "1d6e6c9a-0000-0000-0000-000000000001",
    "visible":        true,

    // -- entity / projection metadata --
    "data": {
      "uId":        "c449d832-a4cc-4b01-b9d5-8a12c42a9f89",
      "schemaName": "Activity",
      "schemaType": "Email",
      "filter": {
        "columnName":  "Type",
        "columnValue": "e2831dec-cfc0-df11-b00f-001d60e938c6"
      },
      "columns": [
        { "columnName": "Title", "columnLayout": { "column": 1, "row": 1, "colSpan": 12, "rowSpan": 1 } },
        { "columnName": "Body",  "columnLayout": { "column": 1, "row": 2, "colSpan": 12, "rowSpan": 2 } }
      ]
    }
  }
}
```

Repeat for every entity kind you want to show in the timeline.

> **Two locations, with asymmetric fallback.** Every PackageStore tile places `linkedColumn`, `sortedByColumn`, `ownerColumn`, `iconId`, `visible` at top-level `values`, and `schemaName`, `schemaType`, `filter`, `columns`, `uId` inside `data`. The runtime `_initTilesConfigs` (`CrtTimelineComponent`) reads the `viewItem.X || viewItem.data.X` fallback **only for `linkedColumn` and `sortedByColumn`** — `ownerColumn` and `iconId` are read top-level only (no fallback). Always emit `linkedColumn` / `sortedByColumn` / `ownerColumn` / `iconId` at top-level.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.TimelineTile` are in `ComponentRegistry.json` under `componentType: "crt.TimelineTile"`.
This guide covers only the assembly mechanics — what to put where in `viewConfigDiff` /
`viewModelConfigDiff` / `modelConfigDiff`.

---

## 4. Copy-paste minimal example — Task tile

```jsonc
{
  "operation": "insert",
  "name": "TimelineTile_Task",
  "parentName": "Timeline",
  "propertyName": "items",
  "index": 1,
  "values": {
    "type": "crt.TimelineTile",
    "classes": ["view-element"],
    "linkedColumn":   "Contact",
    "sortedByColumn": "StartDate",
    "ownerColumn":    "Owner",
    "iconId":         "1d6e6c9a-0000-0000-0000-000000000002",
    "visible":        true,
    "data": {
      "uId":        "a1b2c3d4-0000-0000-0000-000000000001",
      "schemaName": "Activity",
      "schemaType": "Task",
      "filter": {
        "columnName":  "Type",
        "columnValue": "fbe0acdc-cfc0-df11-b00f-001d60e938c6"
      },
      "columns": [
        { "columnName": "Title",     "columnLayout": { "column": 1, "row": 1, "colSpan": 12, "rowSpan": 1 } },
        { "columnName": "StartDate", "columnLayout": null }
      ]
    }
  }
}
```

---

## 5. Common pitfalls

1. **`crt.TimelineTile` at the page root** — must be a child of `crt.Timeline`. A stand-alone tile renders nothing.
2. **Missing `schemaName` (under `data`) or top-level `linkedColumn`** — the timeline service can't query records of this kind and silently drops the tile.
3. **`data.filter` as `FilterGroup[]`** — the real shape is a single `{columnName, columnValue, comparisonType?}` object. Use this shape to scope `Activity` tiles to a specific `Type`.
4. **Placing tile metadata under `data` instead of top-level** — historically supported (runtime fallback) only for `linkedColumn` and `sortedByColumn`; `ownerColumn` and `iconId` are read top-level only. Always emit `linkedColumn`, `sortedByColumn`, `ownerColumn`, `iconId`, `visible` at top-level (note: `iconId`, not `icon` — runtime ignores `icon`/`iconPosition`).
5. **Setting runtime-only properties in schema** — `entity`, `tileColumnsConfig`, `tileViewConfig`, etc. are populated by the parent timeline at render time.
6. **Custom head component naming clash** — the platform looks up custom head components by `schemaType`. If two tiles map to the same custom head, only the first registration wins.
7. **`columnLayout` overflowing the tile** — the tile grid is typically 12 columns wide in real schemas; using `column > 12` silently clips the rendered cell. A `null` `columnLayout` keeps the column queried but unrendered.
8. **Forgetting `ownerColumn` on tiles that should respect "filter by owner"** — the quick filter still applies but uses the default `"CreatedBy"` column, which may not match your entity's ownership semantics.
9. **Same `schemaType` inserted twice** — the timeline indexes tile configs by `schemaType`; the second insert silently overrides the first.

---

## 6. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.TimelineTile"`, unique `name`, `parentName: "<Timeline_id>"`, `propertyName: "items"`.
- [ ] Top-level `linkedColumn` set (FK from the entity to the master record).
- [ ] Top-level `sortedByColumn`, `ownerColumn`, `iconId` (GUID), `visible` set.
- [ ] `data.schemaName` matches a real entity in the project.
- [ ] `data.uId` is a unique GUID (designer convention).
- [ ] `data.schemaType` set (often equals `schemaName`, distinct when filtering a shared entity by `Type`).
- [ ] `data.filter` (if used) follows `{columnName, columnValue, comparisonType?}` shape — not `FilterGroup[]`.
- [ ] `data.columns` projects only existing entity columns; each `columnLayout` fits the tile grid or is `null`.
- [ ] No runtime-only properties (`entity`, `tileColumnsConfig`, etc.) are present in `values`.
