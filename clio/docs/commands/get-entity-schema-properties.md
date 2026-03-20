# get-entity-schema-properties

## Purpose

Prints a human-readable summary of a remote Creatio entity schema.

## Usage

```bash
clio get-entity-schema-properties [options]
```

## Required Arguments

| Argument | Description | Example |
|----------|-------------|---------|
| `--package` | Target package name | `--package Custom` |
| `--schema-name` | Entity schema name | `--schema-name UsrVehicle` |

## Example

```bash
clio get-entity-schema-properties -e dev --package Custom --schema-name UsrVehicle
```

## Behavior Notes

- output is human-readable text and includes column counts, indexes, and key schema flags
- the summary includes primary and primary display columns when they are defined
- this command is the canonical readback path after `create-entity-schema`

## Related Commands

- [`get-entity-schema-column-properties`](../../Commands.md#get-entity-schema-column-properties)
- [`modify-entity-schema-column`](../../Commands.md#modify-entity-schema-column)
