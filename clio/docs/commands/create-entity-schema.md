# create-entity-schema

## Purpose

Creates a remote entity schema inside an existing Creatio package by calling `EntitySchemaDesignerService`. This command is intended for environment-side schema creation from `clio`, not for generating local package files.

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
| `--column` | Column definition in format `<name>:<type>[:<title>[:<refSchema>]]` | `--column "Name:Text:Name"` |

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
- `Integer`
- `Boolean`
- `DateTime`
- `Lookup` with required reference schema name

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
- If no primary display column is defined, the first `Text` column is used.
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
