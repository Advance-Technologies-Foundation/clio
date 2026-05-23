# create-data-binding-db

## Description

Creates a DB-first package data binding by persisting row data directly to the remote Creatio database.

## Usage

```bash
clio create-data-binding-db -e <ENVIRONMENT_NAME> --package <PACKAGE_NAME> --schema <SCHEMA_NAME>
[--binding-name <BINDING_NAME>] [--rows <JSON_ARRAY>]
```

## Options

```bash
-e, --environment          Creatio environment name (required when --uri is omitted)
--uri                      Creatio application URI (alternative to --environment)
--package                  Target package name (required)
--schema                   Entity schema name (required)
--binding-name             Binding folder name (defaults to <schema>)
--rows                     JSON array of row objects, each with a 'values' key:
[{"values":{"Col":"Value"}}]
-H, --help                 Show this help
```

## Behavior

Resolves the package UId from the remote environment, fetches the entity schema column
list from Creatio, and calls SchemaDataDesignerService.svc/SaveSchema to create or
update the binding schema data record in the DB.
SaveSchema metadata is projected to the primary key plus the columns referenced by
currently bound or requested rows, so unrelated runtime-only columns do not block
Account-like schemas. Explicitly requested unsupported runtime columns still fail.
After remote mutation, read back from Creatio instead of treating the request payload
or install log as proof.
To sync the result to a local workspace, use restore-workspace separately.

## Examples

```bash
clio create-data-binding-db -e dev --package Custom --schema SysSettings

clio create-data-binding-db -e dev --package Custom --schema SysSettings \
--binding-name UsrMyBinding \
--rows "[{\"values\":{\"Name\":\"Row\",\"Code\":\"UsrRow\"}}]"
```

- [Clio Command Reference](../../Commands.md#create-data-binding-db)
