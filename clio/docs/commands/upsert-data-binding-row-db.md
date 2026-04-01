# upsert-data-binding-row-db

Upsert a row in a DB-first package data binding.


## Usage

```bash
clio upsert-data-binding-row-db -e <ENVIRONMENT_NAME> --package <PACKAGE_NAME>
--binding-name <BINDING_NAME> --values <JSON>
```

## Description

Upserts a single row in a DB-first package data binding.

## Examples

```bash
clio upsert-data-binding-row-db -e dev --package Custom --binding-name SysSettings \
--values "{\"Name\":\"Updated name\",\"Code\":\"UsrSetting\"}"
```

## Options

```bash
-e, --environment          Creatio environment name (required when --uri is omitted)
--uri                      Creatio application URI (alternative to --environment)
--package                  Target package name (required)
--binding-name             Binding folder name (required)
--values                   Row values as JSON object keyed by column name (required)
-H, --help                 Show this help
```

- [Clio Command Reference](../../Commands.md#upsert-data-binding-row-db)
