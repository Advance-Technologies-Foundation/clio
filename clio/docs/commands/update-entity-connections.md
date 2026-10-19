# update-entity-connections

## Command Type

Development commands

## Name

update-entity-connections - Set an object's Connections to a full ordered list of lookup columns (writes the EntityConnections add-on)

## Description

Sets the "Connections" of an object (entity schema) — the ordered lookup columns of the Object designer
**Connections** node, shown as fields by the Freedom UI `crt.Connections` page component — to the full desired
ordered list given in `--connections`.

- A column left out of the list is **removed**; an empty list removes every connection.
- Only **lookup** columns of the object (own or inherited) are accepted. An unknown column, a column that is not a
  lookup, or a column listed twice fails before anything is written.
- The command writes only the object's `EntityConnections` add-on (the Object designer **Connections** node) in
  `--package-name`, exactly as the Object designer does: only the difference from the parent package layers is
  stored. It never writes rows of the legacy `EntityConnection` table. The add-on drives the `crt.Connections`
  component and, when the `UseConnectionsComponent` feature is on, the server's merged connection list (CrtUIv2
  `EntityConnectionsService/GetConnections`); with the feature off the server ignores the add-on and returns the
  table rows only.
- It then resets the client script cache and reads the state back. When the list already matches, nothing is
  written and the response reports `changed: false`. If the save succeeded but the read-back fails, the response
  still reports `changed: true`, describes the written list, and carries a warning.
- A connection that `get-entity-connections` reports with `columnName: null` (a column not visible from
  `--package-name`) is kept as it is; do not include it in `--connections`.
- The write is refused when the effective list cannot be read with the add-on applied — when
  `EntityConnectionsService` is not available, or when its list does not reflect the add-on content (the
  `UseConnectionsComponent` feature may be off).
- A column removed by another package layer cannot be brought back from this package; the response then carries a
  warning.

## Synopsis

```
clio update-entity-connections -e ENVIRONMENT --entity-schema-name NAME --package-name NAME --connections COLUMN[,COLUMN...]
```

## Options

| Option | Required | Default | Description |
|---|---|---|---|
| `-e, --environment` | No | | Registered Creatio environment to target. |
| `--entity-schema-name` | Yes | | Object (entity schema) name, e.g. `Account`. |
| `--package-name` | Yes | | Package whose `EntityConnections` add-on layer is written, e.g. `Custom`. |
| `--connections` | Yes | | Full desired ordered list of lookup column names, comma-separated, e.g. `Owner,PrimaryContact`. An empty string (`""`) removes every connection. |
| `-u, --uri` / `-l, --login` / `-p, --password` | No | | Direct connection fallback when no registered environment is used. |

## Examples

```
# Put PrimaryContact first and keep Owner after it
clio update-entity-connections -e dev --entity-schema-name Account --package-name Custom --connections PrimaryContact,Owner

# Remove every connection of the object in this package
clio update-entity-connections -e dev --entity-schema-name Account --package-name Custom --connections ""
```

## Output

The same shape as [`get-entity-connections`](get-entity-connections.md), read back after the write, plus
`changed` (`true` when the add-on was saved) and `warnings` (for example a failed script-cache reset).

## Notes

- Aliases: `entity-connections-set`, `set-entity-connections`.
- Read the current list with [`get-entity-connections`](get-entity-connections.md) first: the list you pass
  replaces the current one, so a column you do not repeat is removed.
- Lookup-only is a clio rule that matches the Object designer; the platform itself accepts any column.
- Requires the `CanManageSolution` operation (the add-on save checks it).
- MCP: the `update-entity-connections` tool takes `connections` as a JSON array of column names.
