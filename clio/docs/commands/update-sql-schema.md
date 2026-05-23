# update-sql-schema

## Command Type

    Development commands

## Name

update-sql-schema - Update the body of a SQL script schema on a remote Creatio environment

**Aliases:** `sql-schema-update`

## Description

The update-sql-schema command replaces the body of an existing SQL script schema on a remote
Creatio environment via ScriptSchemaDesignerService. The schema is resolved by name and the
body is patched in-place. No local workspace files are created or modified.

Provide the new body inline via `--body` or as an absolute file path via `--body-file`. When
both are provided, `--body-file` takes precedence.

Use `--dry-run` to resolve and validate the schema without saving.

## Synopsis

```bash
clio update-sql-schema [options]
```

## Options

```bash
--schema-name                      SQL script schema name (required)

--body                             New SQL body to save

--body-file                        Absolute path to a file whose contents are used as the
                                   new schema body. Takes precedence over --body

--dry-run                          Validate and resolve the schema without saving

--uri                    -u       Application uri

--Password               -p       User password

--Login                  -l       User login (administrator permission required)

--Environment            -e       Environment name

--Maintainer             -m       Maintainer name
```

## Example

```bash
clio update-sql-schema --schema-name UsrCleanupStaleRows --body-file ./UsrCleanupStaleRows.sql -e dev
# Replace the body of UsrCleanupStaleRows with the contents of the file

clio update-sql-schema --schema-name UsrCleanupStaleRows --body-file ./UsrCleanupStaleRows.sql --dry-run -e dev
# Validate that the schema can be resolved without saving

clio update-sql-schema --schema-name UsrCleanupStaleRows --body "DELETE FROM UsrLog WHERE CreatedOn < GETUTCDATE()-30" -e dev
# Save an inline SQL body for the schema
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#update-sql-schema)
