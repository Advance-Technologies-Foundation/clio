# create-data-binding-db

Create a DB-first package data binding by saving data directly to the remote Creatio database.


## Usage

```bash
clio create-data-binding-db -e <ENVIRONMENT_NAME> --package <PACKAGE_NAME> --schema <SCHEMA_NAME>
[--binding-name <BINDING_NAME>] [--rows <JSON_ARRAY>]
```

## Description

Creates a DB-first package data binding by persisting row data directly to the remote Creatio database.

## Examples

```bash
clio create-data-binding-db -e dev --package Custom --schema SysSettings

clio create-data-binding-db -e dev --package Custom --schema SysSettings \
--binding-name UsrMyBinding \
--rows "[{\"values\":{\"Name\":\"Row\",\"Code\":\"UsrRow\"}}]"
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

- [Clio Command Reference](../../Commands.md#create-data-binding-db)
