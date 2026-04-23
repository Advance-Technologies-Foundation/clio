# remove-data-binding-row-db

## Description

Removes a row from a DB-first package data binding. Deletes the binding schema data record when no rows remain.

## Usage

```bash
clio remove-data-binding-row-db -e <ENVIRONMENT_NAME> --package <PACKAGE_NAME>
--binding-name <BINDING_NAME> --key-value <PRIMARY_KEY_VALUE>
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

## Behavior

Looks up the entity schema name from the remote SysPackageSchemaData table.
Fetches bound rows via GetBoundSchemaData to locate the target row.
Deletes the entity record via DeleteQuery.
When rows remain, rebuilds SaveSchema metadata from the primary key plus the columns
present in the remaining bound rows.
When no rows remain, deletes the binding schema data record via DeletePackageSchemaDataRequest.
After remote mutation, read back from Creatio instead of treating the request payload
or install log as proof.

## Example

```bash
clio remove-data-binding-row-db -e dev --package Custom --binding-name SysSettings \
--key-value 4f41bcc2-7ed0-45e8-a1fd-474918966d15
```

- [Clio Command Reference](../../Commands.md#remove-data-binding-row-db)
