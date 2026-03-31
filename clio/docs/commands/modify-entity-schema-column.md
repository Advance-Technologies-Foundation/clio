# modify-entity-schema-column

## Purpose

Adds, modifies, or removes one own column in a remote Creatio entity schema by loading the current design item, mutating it locally, and saving it back through `EntitySchemaDesignerService`.

This command is part of the canonical `clio` MCP mutation surface for explicit single-column edits. Batch-style frontend update plans should be decomposed into repeated `modify-entity-schema-column` calls or moved into `schema-sync` rather than translated into a different `clio` tool name.

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
| `--default-value-source` | Default value source: `Const` or `None` | `--default-value-source Const` |
| `--default-value` | Set a constant default value | `--default-value "New"` |
| `--multiline-text` | Text-only flag | `--multiline-text true` |
| `--localizable-text` | Text-only flag | `--localizable-text true` |
| `--accent-insensitive` | Text-only flag | `--accent-insensitive true` |
| `--masked` | Text/SecureText flag | `--masked true` |
| `--format-validated` | Text-only flag | `--format-validated true` |
| `--use-seconds` | DateTime-only flag | `--use-seconds true` |
| `--simple-lookup` | Lookup-only flag | `--simple-lookup true` |
| `--cascade` | Lookup-only flag | `--cascade true` |
| `--do-not-control-integrity` | Lookup-only flag | `--do-not-control-integrity true` |

## Supported Types

- `Guid`
- `Text`
- `ShortText`
- `MediumText`
- `LongText`
- `MaxSizeText`
- `Binary`
- `Image`
- `File`
- `SecureText`
- `Integer`
- `Float`
- `Boolean`
- `Date`
- `DateTime`
- `Time`
- `Lookup`

The command also accepts `Blob` as an alias for `Binary`, `Encrypted` and `Password` as aliases for `SecureText`, plus designer-native text and decimal variants such as `Text50`, `Text250`, `Text500`, `TextUnlimited`, `PhoneNumber`, `WebLink`, `Email`, `RichText`, `Decimal0`, `Decimal1`, `Decimal2`, `Decimal3`, `Decimal4`, `Decimal8`, `Currency0`, `Currency1`, `Currency2`, and `Currency3`.

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

### Clear a Default Value

```bash
clio modify-entity-schema-column -e dev --package Custom --schema-name UsrVehicle --action modify --column-name Status --default-value-source None
```

### Remove a Column

```bash
clio modify-entity-schema-column -e dev --package Custom --schema-name UsrVehicle --action remove --column-name LegacyCode
```

## Behavior Notes

- `modify` updates only explicitly supplied options and preserves the rest of the column payload.
- `remove` works for own columns only and clears direct schema-level references to the removed column.
- inherited columns are readable but not mutable in v1.
- frontend-style type aliases such as `ShortText`, `Float`, `Date`, and `Time` are accepted and mapped to the closest supported designer types.
- `--default-value-source None` clears the stored default value; `Const` requires `--default-value`.
- MCP callers can also send structured `default-value-config` with `source` set to `None`, `Const`, `Settings`, `SystemValue`, or `Sequence`; the direct CLI flags remain shorthand for `Const` and `None`.
- `Binary`, `Image`, and `File` columns do not support `--default-value` or `--default-value-source Const`.
- `--masked` is accepted for `Text` and `SecureText` columns and maps to schema-level `isValueMasked`.
- After `SaveSchema`, the schema is reloaded immediately. The command treats save as failed if the mutated column cannot be read back.

## Related Commands

- [`get-entity-schema-column-properties`](../../Commands.md#get-entity-schema-column-properties)
- [`get-entity-schema-properties`](../../Commands.md#get-entity-schema-properties)
- [`create-entity-schema`](../../Commands.md#create-entity-schema)
