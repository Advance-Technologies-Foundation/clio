# set-entity-schema-properties

## Command Type

    Development commands

## Name

set-entity-schema-properties - Set schema-level properties on a remote Creatio entity schema

## Synopsis

```bash
clio set-entity-schema-properties [OPTIONS]
```

## Description

Sets schema-level properties on an existing remote Creatio entity schema through the
Entity Schema Designer service, then publishes the change (and rebuilds the OData
entities) exactly like the other entity-schema commands, so the update is effective
without a compile.

The command is an extensible property setter — each settable schema-level property is
its own optional flag, and only the flags you supply are applied.

Currently supported property:

- **`--primary-display-column`** — the column shown as the record's display value in
  lookups and links. The target may be an **own** or an **inherited** column and is
  resolved by name to its column UId before saving. clio uses the modern designer
  contract, which matches the primary-display column by its column object (not a raw
  `primaryDisplayColumnUId` GUID field).

After saving, the command reads the schema back and verifies the primary-display column
was persisted. If the target environment did not persist it, the command fails with a
clear error rather than reporting a silent success.

## Options

```bash
--package                  Target package name (required; writes are package-scoped)
--schema-name              Entity schema name (required)
--primary-display-column   Column name (own or inherited) to set as the
                           primary-display column (optional; at least one settable
                           property is required)
```

Environment options are also available:

```bash
-e, --Environment          Environment name from the registered configuration
-u, --uri                  Application URI
-l, --Login                User login
-p, --Password             User password
```

## Examples

```bash
# Set an own text column as the primary-display column
clio set-entity-schema-properties -e dev --package Custom --schema-name UsrVehicle --primary-display-column UsrName

# Set an inherited column as the primary-display column
clio set-entity-schema-properties -e dev --package Custom --schema-name UsrTickets --primary-display-column Subject
```

## Notes

- Read the set value back with `get-entity-schema-properties` (`primary-display-column-name`).
- The change is published and the OData entities are rebuilt automatically; no compile needed.
- At least one settable property must be supplied, otherwise the command reports an error.
- Naming a column that does not exist on the schema fails with a clear error.

## See Also

- [get-entity-schema-properties](get-entity-schema-properties.md)
- [modify-entity-schema-column](modify-entity-schema-column.md)
- [create-entity-schema](create-entity-schema.md)
