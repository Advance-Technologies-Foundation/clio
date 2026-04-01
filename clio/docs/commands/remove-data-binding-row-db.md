# remove-data-binding-row-db

Remove a row from a DB-first package data binding.


## Usage

```bash
clio remove-data-binding-row-db -e <ENVIRONMENT_NAME> --package <PACKAGE_NAME>
--binding-name <BINDING_NAME> --key-value <PRIMARY_KEY_VALUE>
```

## Description

Removes a row from a DB-first package data binding. Deletes the binding schema data record when no rows remain.

## Examples

```bash
clio remove-data-binding-row-db -e dev --package Custom --binding-name SysSettings \
--key-value 4f41bcc2-7ed0-45e8-a1fd-474918966d15
```

## Options

```bash
-e, --environment          Creatio environment name (required when --uri is omitted)
--uri                      Creatio application URI (alternative to --environment)
--package                  Target package name (required)
--binding-name             Binding folder name (required)
--key-value                Primary-key value of the row to remove (required)
-H, --help                 Show this help
```

- [Clio Command Reference](../../Commands.md#remove-data-binding-row-db)
