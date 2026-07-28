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
  sourced from the runtime schema. Most schema-level metadata (title, extend
  parent, db view, track changes, virtual, show in advanced mode, the
  administration flags) and every column's `indexed` flag are authoritative in
  this mode; a few fields are not exposed by the by-name runtime endpoint and are
  reported as `null` so absence is machine-distinguishable (see Notes).
- **Single package layer (`--package` supplied)** — returns only that package
  layer's slice plus **every** schema-level field (parent schema name, indexes,
  SSP availability, and the other schema flags).

This is the canonical verification path after create-entity-schema.
Structured output includes `virtual`, which is `true` when the schema has no physical database table.

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
- in the merged (no `--package`) view, the `package-name` field reports
  `(merged: all packages)`. Most schema-level metadata and every column's
  `indexed` flag are authoritative, but the following fields are not exposed by
  the by-name runtime endpoint and are emitted as `null` in this mode (so a
  programmatic consumer can distinguish "unavailable" from a genuine value rather
  than reading a misleading `false`/`0`): `parent-schema-name`, `indexes-count`,
  `ssp-available`, `use-record-deactivation`, `use-deny-record-rights`, and
  `use-live-editing`.
  Supply `--package` to read those authoritative values from a single package layer

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See Also

get-entity-schema-column-properties, modify-entity-schema-column

- [Clio Command Reference](../../Commands.md#get-entity-schema-properties)
