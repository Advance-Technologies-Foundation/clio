# create-sql-schema

## Command Type

    Development commands

## Name

create-sql-schema - Create a new SQL script schema on a remote Creatio environment

**Aliases:** `sql-schema-create`

## Description

The create-sql-schema command creates a new SQL script schema on a remote Creatio environment
via ScriptSchemaDesignerService. The schema is saved directly to the server; no local
workspace files are created.

The schema-name must start with a letter and contain only letters, digits, or underscores.
The name must be unique within the environment.

## Synopsis

```bash
clio create-sql-schema [options]
```

## Options

```bash
--schema-name                      New SQL schema name (required)

--package-name                     Target package name that will own the new schema (required)

--caption                          Optional display caption; defaults to schema-name

--description                      Optional schema description

--uri                    -u       Application uri

--Password               -p       User password

--Login                  -l       User login (administrator permission required)

--Environment            -e       Environment name

--Maintainer             -m       Maintainer name
```

## Example

```bash
clio create-sql-schema --schema-name UsrCleanupStaleRows --package-name Custom -e dev
# Create UsrCleanupStaleRows in the Custom package on the dev environment

clio create-sql-schema --schema-name UsrCleanupStaleRows --package-name Custom --caption "Cleanup stale rows" -e dev
# Create with a display caption

clio sql-schema-create --schema-name UsrCleanupStaleRows --package-name Custom --description "Nightly cleanup" -e dev
# Create with a description using the alias
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#create-sql-schema)
