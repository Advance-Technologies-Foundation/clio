# add-data-binding-row

## Purpose

Adds a new row to an existing package data binding or replaces the existing row that has the same primary-key value.

## Usage

```bash
clio add-data-binding-row [options]
```

## Arguments

### Required Arguments

| Argument | Description | Example |
|----------|-------------|---------|
| `--package` | Target package name | `--package Custom` |
| `--binding-name` | Binding folder name under package `Data` | `--binding-name SysSettings` |
| `--values` | JSON object keyed by column name for the row payload. If the GUID primary key column is omitted or null, it is generated automatically | `--values "{\"Name\":\"Value\"}"` |

### Optional Arguments

| Argument | Description | Example |
|----------|-------------|---------|
| `--workspace-path` | Workspace root path. Defaults to the current workspace | `--workspace-path C:\Work\MyWorkspace` |
| `--localizations` | JSON object keyed by culture and column name | `--localizations "{\"en-US\":{\"Name\":\"Localized name\"}}"` |

## Behavior

- Loads `descriptor.json` to resolve column names to `SchemaColumnUId` values
- Uses the primary key defined in the binding descriptor as the row identity
- Generates a GUID primary key when the binding primary key is GUID-based and the payload omits it or sets it to `null`
- Replaces an existing row when the same primary-key value already exists
- Writes localization rows into `Localization/data.<culture>.json` when `--localizations` is supplied
- Rejects unknown columns and invalid JSON payload shapes

## JSON Shapes

### Values

```json
{
  "Name": "New name"
}
```

### Localizations

```json
{
  "en-US": {
    "Name": "Localized name"
  }
}
```

## Examples

### Add a New Row

```bash
clio add-data-binding-row --package Custom --binding-name SysSettings \
  --values "{\"Name\":\"Setting name\"}"
```

### Replace an Existing Row and Update Localization

```bash
clio add-data-binding-row --package Custom --binding-name SysSettings \
  --workspace-path C:\Work\MyWorkspace \
  --values "{\"Name\":\"New name\"}" \
  --localizations "{\"en-US\":{\"Name\":\"Localized name\"}}"
```

## Validation and Requirements

- The workspace must be resolvable and must contain the target package
- The binding directory and its `descriptor.json` and `data.json` files must already exist
- The row payload may omit the binding primary key only when that key is Guid-based; in that case the command generates it automatically
- Localized columns must be string-compatible according to the binding descriptor

## Related Commands

- [`create-data-binding`](../../Commands.md#create-data-binding) - Create the binding folder and descriptor
- [`remove-data-binding-row`](../../Commands.md#remove-data-binding-row) - Remove an existing row from the binding
