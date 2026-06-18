# Timeline Tile (`crt.TimelineTile`)

> This is an internal sub-component of `crt.Timeline`. Do not insert it directly into a page schema — the timeline renders tiles automatically from its `items` configuration.

## Metadata
- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.Timeline` (internal)
- **Typical children**: none

## Property reference
`data` input is set by the parent `crt.Timeline`. See `crt.Timeline` for usage.

Additional runtime properties:

| Property | Type | Description |
|---|---|---|
| `sortedByColumn` | string | Column name used to sort tiles chronologically within the timeline. |
| `data` | object | Tile data configuration object with `schemaType` and column mappings. |
