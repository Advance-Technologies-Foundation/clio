# get-entity-connections

## Command Type

Development commands

## Name

get-entity-connections - Read an object's Connections (the ordered lookup columns shown by the Freedom UI Connections component)

## Description

Reads the "Connections" of an object (entity schema): the ordered lookup columns of the Object designer
**Connections** node, which the Freedom UI `crt.Connections` page component shows as fields.

The effective list is read from `EntityConnectionsService` (CrtUIv2), which merges the `EntityConnection` rows with
the `EntityConnections` add-on of every package layer. For each connection the command reports the column name,
column `UId`, caption, referenced object and position. It also reports the `EntityConnections` add-on content read
for `--package-name` (`packageLayer.items` — columns placed first, `packageLayer.removed` — removed columns).

When `EntityConnectionsService` is not available, the list is read from the `EntityConnection` table ordered by
`Position`, and a warning says that the add-on items and removals are not reflected.

**Read-only** — it performs no save. Use it before [`update-entity-connections`](update-entity-connections.md),
which takes the full desired ordered list.

## Synopsis

```
clio get-entity-connections -e ENVIRONMENT --entity-schema-name NAME --package-name NAME
```

## Options

| Option | Required | Default | Description |
|---|---|---|---|
| `-e, --environment` | No | | Registered Creatio environment to target. |
| `--entity-schema-name` | Yes | | Object (entity schema) name, e.g. `Account`. |
| `--package-name` | Yes | | Package whose `EntityConnections` add-on layer is read, e.g. `Custom`. |
| `-u, --uri` / `-l, --login` / `-p, --password` | No | | Direct connection fallback when no registered environment is used. |

## Examples

```
clio get-entity-connections -e dev --entity-schema-name Account --package-name Custom
```

## Output

```json
{
  "success": true,
  "entitySchemaName": "Account",
  "entitySchemaUId": "25d7c1ab-1de0-4501-b402-02e0e5a72d6e",
  "packageName": "Custom",
  "connections": [
    { "columnName": "Owner", "columnUId": "…", "caption": "Owner", "referenceSchemaName": "Contact", "position": 0 }
  ],
  "packageLayer": { "items": ["Owner"], "removed": [] }
}
```

`warnings` is present when there is a non-fatal issue: the table fallback, or an effective list that does not
reflect the add-on (the `UseConnectionsComponent` feature may be off on the environment).

## Notes

- Aliases: `entity-connections-get`.
- Pairs with [`update-entity-connections`](update-entity-connections.md): read → change the column list → update with the full list.
- `entitySchemaUId` is the persisted object identity the add-on service resolved, not a temporary designer copy.
