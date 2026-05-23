# install-sql-schema

## Command Type

    Development commands

## Name

install-sql-schema - Execute a SQL script schema on a remote Creatio environment

**Aliases:** `sql-schema-install`, `execute-sql-schema`

## Description

The install-sql-schema command executes an existing SQL script schema on a remote Creatio
environment via `ScriptSchemaDesignerService.svc/ExecuteScript`. The schema is resolved by
name and its current body is executed directly against the configured database.

## Warning

**This command runs raw SQL against the live database behind the target Creatio environment.
The effects are IRREVERSIBLE.** Statements such as `DROP`, `DELETE`, `TRUNCATE`, `UPDATE`, or
schema changes will be applied immediately and cannot be rolled back by clio. There is no
transactional safety net, no dry-run, and no confirmation prompt. Always review the schema
body with `get-sql-schema` before running, and prefer pointing it at a disposable or
backed-up environment first. Production use requires explicit change-management approval and
a verified database backup.

## Synopsis

```bash
clio install-sql-schema [options]
```

## Options

```bash
--schema-name                      SQL script schema name to execute (required)

--uri                    -u       Application uri

--Password               -p       User password

--Login                  -l       User login (administrator permission required)

--Environment            -e       Environment name

--Maintainer             -m       Maintainer name
```

## Example

```bash
clio install-sql-schema --schema-name UsrCleanupStaleRows -e dev
# Execute the current body of UsrCleanupStaleRows on the dev environment

clio sql-schema-install --schema-name UsrCleanupStaleRows -e dev
# Same as above using the alias

clio execute-sql-schema --schema-name UsrCleanupStaleRows -e dev
# Same as above using the execute-sql-schema alias
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#install-sql-schema)
