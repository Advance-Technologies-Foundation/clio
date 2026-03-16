# modify-entity-schema-column

## Purpose

Adds, modifies, or removes one own column in a remote Creatio entity schema by loading the current design item, mutating it locally, and saving it back through `EntitySchemaDesignerService`.

## Usage

```bash
clio modify-entity-schema-column [options]
```

## Required Arguments

| Argument | Description | Example |
|----------|-------------|---------|
| `--package` | Target package name | `--package Custom` |
| `--schema-name` | Entity schema name | `--schema-name UsrVehicle` |
| `--action` | Column action: `add`, `modify`, `remove` | `--action modify` |
| `--column-name` | Target column name | `--column-name Name` |

## Optional Arguments

| Argument | Description | Example |
|----------|-------------|---------|
| `--new-name` | Rename the column | `--new-name PrimaryOwner` |
| `--type` | Column type for add/modify | `--type Lookup` |
| `--title` | Column title/caption | `--title "Vehicle name"` |
| `--description` | Column description | `--description "Displayed in lists"` |
| `--reference-schema` | Lookup reference schema name | `--reference-schema Contact` |
| `--required` | Set required flag | `--required true` |
| `--indexed` | Set indexed flag | `--indexed true` |
| `--cloneable` | Set cloneable flag | `--cloneable true` |
| `--track-changes` | Set track changes flag | `--track-changes true` |
| `--default-value` | Set a constant default value | `--default-value "New"` |
| `--multiline-text` | Text-only flag | `--multiline-text true` |
| `--localizable-text` | Text-only flag | `--localizable-text true` |
| `--accent-insensitive` | Text-only flag | `--accent-insensitive true` |
| `--masked` | Text-only flag | `--masked true` |
| `--format-validated` | Text-only flag | `--format-validated true` |
| `--use-seconds` | DateTime-only flag | `--use-seconds true` |
| `--simple-lookup` | Lookup-only flag | `--simple-lookup true` |
| `--cascade` | Lookup-only flag | `--cascade true` |
| `--do-not-control-integrity` | Lookup-only flag | `--do-not-control-integrity true` |

## Supported Types

- `Guid`
- `Text`
- `Integer`
- `Boolean`
- `DateTime`
- `Lookup`

## Examples

### Add a Text Column

```bash
clio modify-entity-schema-column -e dev --package Custom --schema-name UsrVehicle --action add --column-name Name --type Text --title "Vehicle name"
```

### Add a Lookup Column

```bash
clio modify-entity-schema-column -e dev --package Custom --schema-name UsrVehicle --action add --column-name Owner --type Lookup --reference-schema Contact --title "Owner"
```

### Modify a Column

```bash
clio modify-entity-schema-column -e dev --package Custom --schema-name UsrVehicle --action modify --column-name Owner --new-name PrimaryOwner --title "Primary owner" --reference-schema Contact
```

### Remove a Column

```bash
clio modify-entity-schema-column -e dev --package Custom --schema-name UsrVehicle --action remove --column-name LegacyCode
```

## Behavior Notes

- `modify` updates only explicitly supplied options and preserves the rest of the column payload.
- `remove` works for own columns only and clears direct schema-level references to the removed column.
- inherited columns are readable but not mutable in v1.

## Related Commands

- [`get-entity-schema-column-properties`](../../Commands.md#get-entity-schema-column-properties)
- [`get-entity-schema-properties`](../../Commands.md#get-entity-schema-properties)
- [`create-entity-schema`](../../Commands.md#create-entity-schema)
