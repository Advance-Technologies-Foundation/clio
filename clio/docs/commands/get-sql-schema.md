# get-sql-schema

## Command Type

    Development commands

## Name

get-sql-schema - Read body and metadata of a SQL script schema on a remote Creatio environment

**Aliases:** `sql-schema-get`

## Description

The get-sql-schema command reads the body and metadata of an existing SQL script schema on a
remote Creatio environment via ScriptSchemaDesignerService. The schema is resolved by name and
the full schema descriptor (UId, name, caption, package, body, etc.) is returned. No local
workspace files are created or modified.

When `--output-file` is set, the schema body is written to the specified file and the body
field is omitted from the response JSON printed to stdout. This keeps the terminal output
small and makes it easy to edit the body locally before piping it back through
`update-sql-schema`.

## Synopsis

```bash
clio get-sql-schema [options]
```

## Options

```bash
--schema-name                      SQL script schema name (required)

--output-file                      Optional absolute path. When set, the schema body is
                                   written to this file and the body field is omitted
                                   from the response JSON

--uri                    -u       Application uri

--Password               -p       User password

--Login                  -l       User login (administrator permission required)

--Environment            -e       Environment name

--Maintainer             -m       Maintainer name
```

## Example

```bash
clio get-sql-schema --schema-name UsrCleanupStaleRows -e dev
# Print body and metadata of UsrCleanupStaleRows from the dev environment

clio get-sql-schema --schema-name UsrCleanupStaleRows --output-file /tmp/UsrCleanupStaleRows.sql -e dev
# Save the body to /tmp/UsrCleanupStaleRows.sql; response JSON omits the body

clio sql-schema-get --schema-name UsrCleanupStaleRows -e dev
# Same as the first example using the alias
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#get-sql-schema)
