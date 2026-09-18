# create-sql-schema

## Command Type

Development commands

## Description

Creates an empty package SQL script on a remote Creatio environment using the native
`SqlScriptSchemaDesignerService/SaveSchema` operation. No local workspace files are created.
SQL scripts belong to `VwSysSqlScriptInPackage`, not `SysSchema` or a script schema manager.

**Alias:** `sql-schema-create`.

## Options

| Option | Meaning |
|---|---|
| `--schema-name` | Required unique name, starting with a letter and containing letters, digits or underscores. |
| `--package-name` | Required editable destination package. |
| `--db-engine-type` | Optional: 0 MSSql, 1 Oracle, 2 PostgreSql. Omit to detect the target engine. If detection is unavailable on an older host, supply it explicitly. |
| `--install-type` | 0 before package, 1 after package (default), 2 after schema data, 3 uninstall app. |
| `-e`, `--environment` | Registered environment. Direct `--uri`, `--login`, `--password` are also supported. |

The legacy `--caption`, `--description`, and `--caption-culture` options remain recognized but
nonempty values are rejected: native package SQL scripts do not persist these fields.
Use `--schema-name` as the script display name.

## Example

```bash
clio create-sql-schema --schema-name UsrCleanupStaleRows --package-name Custom -e dev
clio update-sql-schema --schema-name UsrCleanupStaleRows --body-file /work/cleanup.sql -e dev
clio get-sql-schema --schema-name UsrCleanupStaleRows -e dev
clio install-sql-schema --schema-name UsrCleanupStaleRows -e dev
```

Creation saves an empty body without executing SQL. Updating saves the body while preserving the
script identity, engine, installation phase and dependencies. Installation executes the saved SQL.
Normal package export/deployment carries the script; use the existing workspace/package workflow
when maintaining it in source control.

## Failure handling

Invalid names are reported together. Duplicate or ambiguous names are rejected without saving.
Missing native services produce a named endpoint diagnostic. The synchronous transport does not
expose HTTP status, so an empty response is not falsely classified as a proven 404.
An unusable save response triggers a readback: success requires this attempt's generated UId.
Automatic authentication replay is disabled for SQL saves and execution.

[Command index](../../Commands.md#create-sql-schema)
