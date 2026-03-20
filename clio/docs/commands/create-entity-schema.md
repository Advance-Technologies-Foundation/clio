# create-entity-schema

## Purpose

Creates a remote entity schema inside an existing Creatio package by calling `EntitySchemaDesignerService`. This command is intended for environment-side schema creation from `clio`, not for generating local package files.

Current `clio` entity-schema commands are also the supported ADAC integration surface. Keep using `create-entity-schema` and `modify-entity-schema-column`; frontend-only aliases such as `entity.create` or `entity.update` are conceptual only and are not the direct `clio` API.

## Usage

```bash
clio create-entity-schema [options]
```

## Arguments

### Required Arguments

| Argument | Description | Example |
|----------|-------------|---------|
| `--package` | Target package name | `--package Custom` |
| `--name` | Entity schema name, maximum 22 characters | `--name UsrVehicle` |
| `--title` | Entity schema title/caption | `--title "Vehicle"` |

### Optional Arguments

| Argument | Description | Example |
|----------|-------------|---------|
| `--parent` | Parent schema name | `--parent BaseEntity` |
| `--extend-parent` | Create a replacement schema. Requires `--parent` | `--extend-parent` |
| `--column` | Column definition in format `<name>:<type>[:<title>[:<refSchema>]]` or JSON with `name`, `type`, `title`/`caption`, `reference-schema-name`, `required`, `default-value-source`, `default-value`. Repeat the option for multiple columns. | `--column "Name:Text:Name"` |

### Environment Configuration

| Argument | Short | Description | Example |
|----------|-------|-------------|---------|
| `--environment` | `-e` | Environment name from configuration | `-e dev` |
| `--uri` | `-u` | Creatio application URI | `--uri http://localhost:8083` |
| `--login` | `-l` | Username for authentication | `--login Supervisor` |
| `--password` | `-p` | Password for authentication | `--password Supervisor` |

## Supported Column Types

- `Guid`
- `Text`
- `ShortText`
- `MediumText`
- `LongText`
- `MaxSizeText`
- `Integer`
- `Float`
- `Boolean`
- `Date`
- `DateTime`
- `Time`
- `Lookup` with required reference schema name

The command also accepts designer-native text and decimal variants such as `Text50`, `Text250`, `Text500`, `TextUnlimited`, `PhoneNumber`, `WebLink`, `Email`, `RichText`, `Decimal0`, `Decimal1`, `Decimal2`, `Decimal3`, `Decimal4`, `Decimal8`, `Currency0`, `Currency1`, `Currency2`, and `Currency3`.

## Examples

### Create a Bare Entity Schema

```bash
clio create-entity-schema -e dev --package Custom --name UsrVehicle --title "Vehicle"
```

### Create an Entity Schema with Initial Columns

```bash
clio create-entity-schema -e dev --package Custom --name UsrVehicle --title "Vehicle" \
  --column "Name:Text:Name" \
  --column "CreatedOn:DateTime:Created on" \
  --column "IsActive:Boolean:Active"
```

### Create a Lookup Column

```bash
clio create-entity-schema -e dev --package Custom --name UsrVehicle --title "Vehicle" \
  --column "Owner:Lookup:Owner:Contact"
```

### Create a Column from Structured JSON Metadata

```bash
clio create-entity-schema -e dev --package Custom --name UsrVehicle --title "Vehicle" \
  --column "{\"name\":\"Status\",\"type\":\"ShortText\",\"title\":\"Status\",\"required\":true,\"default-value-source\":\"Const\",\"default-value\":\"Draft\"}"
```

### Create a Schema with Inheritance

```bash
clio create-entity-schema -e dev --package Custom --name UsrVehicle --title "Vehicle" --parent BaseEntity
```

### Create a Replacement Schema

```bash
clio create-entity-schema -e dev --package Custom --name UsrAccount --title "Account" --parent Account --extend-parent
```

## Behavior Notes

- The command uses the server-side entity designer flow:
  1. `CreateNewSchema`
  2. optional `AssignParentSchema`
  3. `SaveSchema`
- Package resolution relies on the package list API, so `cliogate` must be installed on the target environment.
- For schemas without a parent, `Id:Guid` is created automatically if no Guid column is supplied.
- If no primary display column is defined, the first text-like column is used.
- The command accepts frontend-style aliases such as `ShortText`, `Float`, `Date`, and `Time`, and maps them to the closest supported designer types.
- Repeat `--column` for multiple entries; semicolons inside JSON payloads are treated as content, not separators.
- After `SaveSchema`, the schema is reloaded immediately. The command treats save as failed if the schema cannot be read back.
- Schema names longer than 22 characters are rejected locally before the server call.

## Output

### Success

```text
[INF] - Entity schema 'UsrVehicle' created in package 'Custom'.
[INF] - Done
```

### Example Validation Error

```text
[ERR] - Schema name must not exceed 22 characters.
```

## Requirements

- Target environment must be reachable by `clio`
- `cliogate` must be installed for package lookup
- User must have permission to modify the target package and create schemas

## Related Commands

- [`install-gate`](../../Commands.md#install-gate) - Install cliogate on the target environment
- [`get-pkg-list`](../../Commands.md#get-pkg-list) - List available packages
- [`add-schema`](../../Commands.md#add-schema) - Generate local schema files in a package
