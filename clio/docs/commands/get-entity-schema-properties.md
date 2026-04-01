# get-entity-schema-properties

## Command Type

    Development commands

## Name

get-entity-schema-properties - Get properties from a remote Creatio entity schema

## Synopsis

```bash
clio get-entity-schema-properties [OPTIONS]
```

## Description

Loads the full entity schema design item from the remote Creatio environment
and prints a human-readable schema summary with package, parent schema,
primary columns, column counts, indexes, major schema flags, and grouped
own and inherited column listings.
This is the canonical verification path after create-entity-schema.

## Options

```bash
--package              Target package name
--schema-name          Entity schema name

Environment options are also available:
-e, --Environment      Environment name from the registered configuration
-u, --uri              Application URI
-l, --Login            User login
-p, --Password         User password
```

## Examples

```bash
# Read entity schema properties
clio get-entity-schema-properties -e dev --package Custom --schema-name UsrVehicle
```

## Notes

- output is human-readable text, not JSON
- the report includes own and inherited column counts plus grouped column lists
- structured and MCP consumers should read the nested data.columns collection
from the schema summary object
- nested column entries expose normalized type names such as Binary, Image, File, and ImageLookup
- schema mutations are expected to be readable here immediately after save

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See Also

get-entity-schema-column-properties, modify-entity-schema-column

- [Clio Command Reference](../../Commands.md#get-entity-schema-properties)
