# remove-data-binding-row

## Purpose

Removes a row from an existing package data binding by primary-key value. Once the binding exists locally, the command works entirely from the binding files and does not require Creatio access, including bindings created from built-in offline templates such as `SysSettings`.

## Usage

```bash
clio remove-data-binding-row [options]
```

## Arguments

### Required Arguments

| Argument | Description | Example |
|----------|-------------|---------|
| `--package` | Target package name | `--package Custom` |
| `--binding-name` | Binding folder name under package `Data` | `--binding-name SysSettings` |
| `--key-value` | Primary-key value of the row to remove | `--key-value 4f41bcc2-7ed0-45e8-a1fd-474918966d15` |

### Optional Arguments

| Argument | Description | Example |
|----------|-------------|---------|
| `--workspace-path` | Workspace root path. Defaults to the current workspace | `--workspace-path C:\Work\MyWorkspace` |

## Behavior

- Loads the target binding descriptor and data files from the local workspace
- Finds the row by the binding primary key
- Removes the matching row from `data.json`
- Removes matching localized rows from every `Localization/data.<culture>.json` file
- Fails when the supplied key does not exist in the binding

## Examples

### Remove a Row from the Current Workspace

```bash
clio remove-data-binding-row --package Custom --binding-name SysSettings \
  --key-value 4f41bcc2-7ed0-45e8-a1fd-474918966d15
```

### Remove a Row from an Explicit Workspace

```bash
clio remove-data-binding-row --package Custom --binding-name SysSettings \
  --workspace-path C:\Work\MyWorkspace \
  --key-value 4f41bcc2-7ed0-45e8-a1fd-474918966d15
```

## Validation and Requirements

- The workspace must be resolvable and must contain the target package
- The binding directory and its `descriptor.json` and `data.json` files must already exist
- The supplied key must match an existing row

## Related Commands

- [`create-data-binding`](../../Commands.md#create-data-binding) - Create the binding folder and descriptor
- [`add-data-binding-row`](../../Commands.md#add-data-binding-row) - Add or replace rows in the binding
