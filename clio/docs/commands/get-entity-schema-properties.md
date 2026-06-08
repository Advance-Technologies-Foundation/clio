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

Loads entity schema properties from the remote Creatio environment and prints a
human-readable schema summary with package, parent schema, primary columns,
column counts, indexes, major schema flags, and grouped own and inherited
column listings.

There are two modes, selected by the presence of `--package`:

- **Merged / effective (no `--package`)** — returns columns from **all** package
  layers, including custom columns added in packages other than the one that
  defines the schema. This is the recommended mode for column discovery and is
  sourced from the runtime schema.
- **Single package layer (`--package` supplied)** — returns only that package
  layer's slice plus schema-level metadata (parent schema, indexes, SSP
  availability, and the other schema flags).

This is the canonical verification path after create-entity-schema.

## Options

```bash
--package              Optional target package name. Omit for the merged
                       all-packages view; supply only to inspect a single
                       package layer's slice.
--schema-name          Entity schema name (required)

Environment options are also available:
-e, --Environment      Environment name from the registered configuration
-u, --uri              Application URI
-l, --Login            User login
-p, --Password         User password
```

## Examples

```bash
# Read the merged/effective schema across all packages (recommended for column discovery)
clio get-entity-schema-properties -e dev --schema-name Account

# Read a single package layer's slice
clio get-entity-schema-properties -e dev --package Custom --schema-name UsrVehicle
```

## Notes

- output is human-readable text, not JSON
- the report includes own and inherited column counts plus grouped column lists
- structured and MCP consumers should read the nested data.columns collection
from the schema summary object
- nested column entries expose normalized type names such as Binary, Image, File, and ImageLookup
- schema mutations are expected to be readable here immediately after save
- **IMPORTANT:** an empty column list (or `own-column-count: 0`) from a
  single-package read does **not** prove a column is absent. Custom columns are
  frequently added in a package other than the one that defines the schema.
  Re-read without `--package` for the merged view, or use `find-entity-schema`
  to locate the customization package.
- in the merged (no `--package`) view, schema-level flags such as parent schema,
  indexes, and SSP availability are populated only in the single-package mode;
  the `package-name` field reports `(merged: all packages)`

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See Also

get-entity-schema-column-properties, modify-entity-schema-column

- [Clio Command Reference](../../Commands.md#get-entity-schema-properties)
