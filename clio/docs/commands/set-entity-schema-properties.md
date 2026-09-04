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
Entity Schema Designer service, then publishes the change like the other entity-schema
commands, so the update is effective without a compile. The primary-display column is
a designer-level property; it does not appear in the OData contract, so setting it
never triggers an OData entities rebuild.

The command is an extensible property setter — each settable schema-level property is
its own optional flag, and only the flags you supply are applied.

Supported properties:

- **`--primary-display-column`** — the column shown as the record's display value in
  lookups and links. The target may be an **own** or an **inherited** column and is
  resolved by name to its column UId before saving. clio uses the modern designer
  contract, which matches the primary-display column by its column object (not a raw
  `primaryDisplayColumnUId` GUID field).
- **`--title` / `--title-localizations`** — the **schema** caption. This is the only
  command that can rename an existing entity schema's caption: `update-entity-schema`
  applies **column** operations, and its `title-localizations` is a per-column property.
  The caption is merged per culture, so cultures you do not list keep the caption they
  already have.

Why the caption matters: a business-process lookup macro
`[#Lookup.<Caption>.<Value>#]` resolves a schema **by its caption**. When two entity
schemas share one caption (for example a custom `labLanguage` alongside the platform's
`SysLanguage`, both captioned `Language`), the macro cannot resolve and the process
refuses to save. Renaming one of the captions is the fix.

After saving, the command reads the schema back and verifies each property was
persisted. If the target environment did not persist it, the command fails with a
clear error rather than reporting a silent success.

## Options

```bash
--package                  Target package name (required; writes are package-scoped)
--schema-name              Entity schema name (required)
--primary-display-column   Column name (own or inherited) to set as the
                           primary-display column (optional)
--title                    New schema caption for the effective caption culture (optional)
--title-localizations      New schema caption per culture as JSON, for example
                           '{"en-US":"Mention language"}' (optional)
--caption-culture          Culture used for a scalar --title (e.g. en-US). Precedence:
                           this override > the connected user's profile culture > en-US

At least one settable property (--primary-display-column, --title or
--title-localizations) is required.
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

# Rename the schema caption so a process lookup macro stops colliding with another schema
clio set-entity-schema-properties -e dev --package Custom --schema-name labLanguage --title-localizations '{"en-US":"Mention language"}'
```

## Notes

- Read the set values back with `get-entity-schema-properties`.
- The change is published automatically; no compile needed. Neither the primary-display column
  nor the schema caption changes the OData contract, so setting them does not trigger an OData
  entities rebuild.
- At least one settable property must be supplied, otherwise the command reports an error.
- A caption change is merged per culture; captions for cultures you do not list are preserved.
- Naming a column that does not exist on the schema fails with a clear error.

## See Also

- [get-entity-schema-properties](get-entity-schema-properties.md)
- [modify-entity-schema-column](modify-entity-schema-column.md)
- [create-entity-schema](create-entity-schema.md)
