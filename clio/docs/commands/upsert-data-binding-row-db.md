# upsert-data-binding-row-db

## Description

Upserts a single row in a DB-first package data binding.

## Usage

```bash
clio upsert-data-binding-row-db -e <ENVIRONMENT_NAME> --package <PACKAGE_NAME>
--binding-name <BINDING_NAME> --values <JSON>
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

## Behavior

Calls SchemaDataDesignerService.svc/SaveSchema to upsert the given row in the remote DB.
To sync the result to a local workspace, use restore-workspace separately.

## Example

```bash
clio upsert-data-binding-row-db -e dev --package Custom --binding-name SysSettings \
--values "{\"Name\":\"Updated name\",\"Code\":\"UsrSetting\"}"
```

- [Clio Command Reference](../../Commands.md#upsert-data-binding-row-db)
