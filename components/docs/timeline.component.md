# How to Add a Timeline (`crt.Timeline`) to a Freedom UI Page

> Audience: code agent inserting a `crt.Timeline` into a Creatio Freedom UI page schema.
>
> `crt.Timeline` renders a chronological stream of related records (activities, emails,
> calls, feed posts, files, …) for a master record. It owns its built-in filtering UI
> (entity, date, owner, system-messages) plus optional `tools` and `customFilters` slots.
> Children are `crt.TimelineTile` entries that describe per-tile column layouts.

For the related Feed component (record-scoped activity stream), see
crt.Feed guide.

## Metadata

- **Category**: interactive
- **Container**: yes
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`, `crt.TabContainer`
- **Typical children**: `crt.TimelineTile` (body via `items`); filter chips via `customFilters` slot

---

## 1. Mental model — the 1-N places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.Timeline"`, `masterSchemaId: "$Id"` (or `masterEntity: "$attr"`). |
| 2+ | `viewConfigDiff` (more entries) | One `insert` op per `crt.TimelineTile` child, each with `parentName: "<Timeline_id>"` and a `schemaName`/`schemaType` describing the linked record kind. |
| 3 | `viewModelConfigDiff` (optional) | Page attributes if you bind `tools` / `customFilters` or `filterValues` dynamically. |

Unlike `crt.DataGrid`, the timeline does **not** require an explicit `EntityDataSource` in
`modelConfigDiff`. It resolves the configured tile types through the platform's timeline
service.

### 1.1 `contentSlots`

```
contentSlots: ['tools' (lazy/input), 'customFilters' (lazy/input)]
```

Both slots are lazy and project from the host page; use them only when you need to extend
the built-in toolbar / filter set.

### 1.2 Naming convention

```
Timeline_<id>            // outer timeline element
TimelineTile_<id>_<n>    // each TimelineTile child
```

---

## 2. Step-by-step recipe

### 2.1 Insert the timeline (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "Timeline_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Timeline",
    "masterSchemaId": "$Id",
    "isTileReadOnly": false,
    "hideTools": false,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 10 }
  }
}
```

For a timeline whose master record is **not** the page's primary record (e.g. a related
contact), bind `masterEntity` instead:

```jsonc
"masterEntity": "$Contact_Owner"
```

### 2.2 Insert child tiles (`viewConfigDiff` entries)

```jsonc
{
  "operation": "insert",
  "name": "TimelineTile_activity",
  "parentName": "Timeline_xkp4r",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TimelineTile",
    "data": {
      "schemaType": "Activity",
      "schemaName": "Activity",
      "linkedColumn": "Contact",
      "sortedByColumn": "CreatedOn",
      "columns": [
        { "columnName": "Title",     "columnLayout": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 1 } },
        { "columnName": "Author",    "columnLayout": { "column": 1, "row": 2, "colSpan": 2, "rowSpan": 1 } },
        { "columnName": "CreatedOn", "columnLayout": { "column": 3, "row": 2, "colSpan": 2, "rowSpan": 1 } }
      ]
    }
  }
}
```

Repeat for each entity type to display (e.g. `Email`, `Call`, `Feed`, `File`).

### 2.3 (Optional) Bind dynamic filter values

```jsonc
// viewModelConfigDiff
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "Timeline_xkp4r_filters": { "value": [] }
    }
  }
}

// viewConfigDiff (within Timeline's values)
"filterValues": "$Timeline_xkp4r_filters"
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.Timeline` are in `ComponentRegistry.json` under `componentType: "crt.Timeline"`. This
guide covers only the assembly mechanics — what to put where in `viewConfigDiff` /
`viewModelConfigDiff` / `modelConfigDiff`.

### Inserting children into the `customFilters` slot

```jsonc
{
  "operation": "insert",
  "name": "Timeline_xkp4r_StatusFilter",
  "parentName": "Timeline_xkp4r",
  "propertyName": "customFilters",
  "index": 0,
  "values": {
    "type": "crt.QuickFilter",
    "filterType": "lookup",
    "config": { "caption": "#ResourceString(Timeline_StatusFilter_Caption)#", "entitySchemaName": "ActivityStatus" }
    // ... plus _filterOptions per crt.QuickFilter recipe
  }
}
```

---

## 4. Copy-paste minimal example — record timeline with Activity + Email + Feed tiles

```jsonc
[
  {
    "operation": "insert",
    "name": "RecordTimeline",
    "parentName": "MainContainer",
    "propertyName": "items",
    "index": 0,
    "values": {
      "type": "crt.Timeline",
      "masterSchemaId": "$Id",
      "layoutConfig": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 10 }
    }
  },
  {
    "operation": "insert",
    "name": "RecordTimeline_Activity",
    "parentName": "RecordTimeline",
    "propertyName": "items",
    "index": 0,
    "values": {
      "type": "crt.TimelineTile",
      "data": {
        "schemaType": "Activity",
        "schemaName": "Activity",
        "linkedColumn": "Contact",
        "sortedByColumn": "CreatedOn",
        "columns": [
          { "columnName": "Title",     "columnLayout": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 1 } },
          { "columnName": "Author",    "columnLayout": { "column": 1, "row": 2, "colSpan": 2, "rowSpan": 1 } },
          { "columnName": "CreatedOn", "columnLayout": { "column": 3, "row": 2, "colSpan": 2, "rowSpan": 1 } }
        ]
      }
    }
  },
  {
    "operation": "insert",
    "name": "RecordTimeline_Email",
    "parentName": "RecordTimeline",
    "propertyName": "items",
    "index": 1,
    "values": {
      "type": "crt.TimelineTile",
      "data": {
        "schemaType": "Email",
        "schemaName": "Email",
        "linkedColumn": "Contact",
        "sortedByColumn": "CreatedOn",
        "columns": [
          { "columnName": "Subject",   "columnLayout": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 1 } },
          { "columnName": "Body",      "columnLayout": { "column": 1, "row": 2, "colSpan": 4, "rowSpan": 3 } }
        ]
      }
    }
  }
]
```

---

## 5. Common pitfalls

1. **`masterSchemaId` missing or unresolved** — timeline calls `canLoadFeed`-like guards internally and silently shows the empty placeholder. On edit pages always set `masterSchemaId: "$Id"`.
2. **No `crt.TimelineTile` children** — the timeline renders only the toolbar and "no data" placeholder. At least one tile is required.
3. **`linkedColumn` pointing at a non-existent FK** — the timeline service can't filter records and returns an empty list. Match `linkedColumn` to the FK column on the entity that references the master.
4. **Two children with the same `schemaType`** — the second silently overrides the first. Pick unique `schemaType` per child.
5. **Overriding `items` directly** — the field is computed from children; setting it manually corrupts the internal entity-config map.
6. **Manually toggling `hideTools` to remove a single button** — there is no per-button hide. Either use the built-in toolbar wholesale or set `hideTools: true` and project a custom `tools` slot.
7. **Putting `crt.Timeline` inside a `crt.TabContainer` not currently visible** — the timeline initializes on mount and may make API calls before the user opens the tab. Combine with lazy mounting if performance matters.

---

## 6. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.Timeline"`, unique `name`, `propertyName: "items"`.
- [ ] `masterSchemaId: "$Id"` set (or `masterEntity: "$attr"` for a foreign record scope).
- [ ] At least one `crt.TimelineTile` child inserted, each with unique `schemaType` and proper `linkedColumn`.
- [ ] Each child's `columns` reference real entity columns and the `columnLayout` fits within the tile grid.
- [ ] `hideTools` set intentionally.
- [ ] `layoutConfig` provides generous `rowSpan` (10+ on average).
