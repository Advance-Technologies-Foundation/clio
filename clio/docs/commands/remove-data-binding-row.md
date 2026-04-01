# remove-data-binding-row

Remove a row from a package data binding.


## Usage

```bash
clio remove-data-binding-row [OPTIONS]
```

## Description

Removes a row from an existing local package data binding by matching the
supplied primary-key value. The matching row is deleted from data.json and
from every localization file under the binding Localization folder. Once a
binding exists locally, this command does not require Creatio access,
including bindings that were created from built-in offline templates.

## Examples

```bash
# Remove a row from the current workspace
clio remove-data-binding-row --package Custom --binding-name SysSettings --key-value 4f41bcc2-7ed0-45e8-a1fd-474918966d15

# Remove a row from an explicit workspace
clio remove-data-binding-row --package Custom --binding-name SysSettings --workspace-path C:\Work\MyWorkspace --key-value 4f41bcc2-7ed0-45e8-a1fd-474918966d15
```

## Options

```bash
--package              Target package name
--binding-name         Binding folder name under package Data
--workspace-path       Workspace root path. Defaults to the current workspace
--key-value            Primary-key value of the row to remove
```

## Notes

- The binding must already exist locally
- The command fails if the supplied key does not exist
- Localization rows that share the same primary key are removed together

## Command Type

    Development commands

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See also

- `add-item`

- [Clio Command Reference](../../Commands.md#remove-data-binding-row)
