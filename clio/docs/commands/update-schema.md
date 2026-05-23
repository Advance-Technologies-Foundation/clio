# update-schema

## Command Type

    Development commands

## Name

update-schema - Update the body of a C# source-code schema on a remote Creatio environment

**Aliases:** `schema-update`

## Description

The update-schema command replaces the body of an existing C# source-code schema on a remote
Creatio environment via SourceCodeSchemaDesignerService. The schema is resolved by name
(ManagerName=SourceCodeSchemaManager) and the body is patched in-place. No local workspace
files are created or modified.

Pass the new body directly via --body or point to a file via --body-file.
When both are provided, --body-file takes precedence.

Use --dry-run to validate and resolve the schema without saving.

## Synopsis

```bash
clio update-schema [options]
```

## Options

```bash
--schema-name                      C# source-code schema name (required)

--body                             New C# body to save

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
clio update-schema --schema-name UsrMyHelper --body-file ./UsrMyHelper.cs -e dev
# Replace the body of UsrMyHelper with the contents of UsrMyHelper.cs

clio update-schema --schema-name UsrMyHelper --body-file ./UsrMyHelper.cs --dry-run -e dev
# Validate that the schema can be resolved without saving

clio update-schema --schema-name UsrMyHelper --body "namespace Terrasoft {...}" -e dev
# Save an inline body for the schema
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#update-schema)
