# get-entity-schema-column-properties

## Command Type

    Development commands

## Name

get-entity-schema-column-properties - Get column properties from a remote Creatio entity schema

## Synopsis

```bash
clio get-entity-schema-column-properties [OPTIONS]
```

## Description

Loads the full entity schema design item from the remote Creatio environment
and prints a human-readable summary for the requested column. Own columns are
searched first, then inherited columns. The summary includes the resolved
default value source and is the canonical verification path after
create-entity-schema / modify-entity-schema-column calls.

## Options

```bash
--package              Target package name
--schema-name          Entity schema name
--column-name          Column name

Environment options are also available:
-e, --Environment      Environment name from the registered configuration
-u, --uri              Application URI
-l, --Login            User login
-p, --Password         User password
```

## Examples

```bash
# Read properties of an own column
clio get-entity-schema-column-properties -e dev --package Custom --schema-name UsrVehicle --column-name Name

# Read properties of an inherited column
clio get-entity-schema-column-properties -e dev --package Custom --schema-name UsrVehicle --column-name Owner
```

## Notes

- output is human-readable text, not JSON
- the report includes whether the column is own or inherited
- the report includes default-value-source, default-value, and
structured default-value-config
- structured default-value-config includes `resolved-value-source` for canonical
  identifiers (`SystemValue` Guid, `Settings` code)
- column type names are normalized to readable values such as Binary, Image, File, and ImageLookup

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See Also

get-entity-schema-properties, modify-entity-schema-column

- [Clio Command Reference](../../Commands.md#get-entity-schema-column-properties)
