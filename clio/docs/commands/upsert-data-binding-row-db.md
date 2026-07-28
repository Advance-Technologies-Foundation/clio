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
Insert vs update is decided by the row primary key: when a row with that Id already exists
in the table it is UPDATED; a row present in neither the binding nor the table is INSERTED
(so it must carry every required column). The binding must already exist — create it with
create-data-binding-db first.
SaveSchema metadata is rebuilt from the primary key plus the columns present in the
existing bound rows and the requested upsert payload.
After remote mutation, read back from Creatio instead of treating the request payload
or install log as proof.
To sync the result to a local workspace, use restore-workspace separately.

## Example

```bash
clio upsert-data-binding-row-db -e dev --package Custom --binding-name SysSettings \
--values "{\"Name\":\"Updated name\",\"Code\":\"UsrSetting\"}"
```

- [Clio Command Reference](../../Commands.md#upsert-data-binding-row-db)
