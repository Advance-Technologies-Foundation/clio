# get-entity-schema-column-properties

## Purpose

Prints a human-readable summary of a column from a remote Creatio entity schema.

## Usage

```bash
clio get-entity-schema-column-properties [options]
```

## Required Arguments

| Argument | Description | Example |
|----------|-------------|---------|
| `--package` | Target package name | `--package Custom` |
| `--schema-name` | Entity schema name | `--schema-name UsrVehicle` |
| `--column-name` | Column name | `--column-name Name` |

## Examples

### Read an Own Column

```bash
clio get-entity-schema-column-properties -e dev --package Custom --schema-name UsrVehicle --column-name Name
```

### Read an Inherited Column

```bash
clio get-entity-schema-column-properties -e dev --package Custom --schema-name UsrVehicle --column-name Owner
```

## Behavior Notes

- own columns are searched first, then inherited columns
- output is human-readable text and includes the column source
- the summary includes both `default-value-source` and `default-value`
- type names are normalized to readable values such as `Binary`, `Image`, `File`, and `ImageLookup`
- this command is the canonical readback path after `modify-entity-schema-column`

## Related Commands

- [`get-entity-schema-properties`](../../Commands.md#get-entity-schema-properties)
- [`modify-entity-schema-column`](../../Commands.md#modify-entity-schema-column)
