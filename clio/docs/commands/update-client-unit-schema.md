# update-client-unit-schema

## Command Type

    Development commands

## Name

update-client-unit-schema - Update the raw body of a client unit schema

**Aliases:** `client-unit-schema-update`

## Description

The update-client-unit-schema command saves the raw JavaScript body of any
client unit schema (classic 7x mixins, utilities, modules, or Freedom UI pages)
through the ClientUnitSchemaDesignerService. Unlike update-page, it bypasses
Freedom UI-specific marker validation, field-binding checks, and bundle merging,
making it suitable for classic 7x schemas and low-level Freedom UI edits.

Pass the new body directly via --body or point to a file via --body-file.
When both are provided, --body-file takes precedence.

## Synopsis

```bash
clio update-client-unit-schema [options]
```

## Options

```bash
--schema-name                      Client unit schema name (required)

--body                             New raw JavaScript body to save

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
clio update-client-unit-schema --schema-name UsrMyModule --body-file ./my-module.js -e dev
save the contents of my-module.js as the schema body in the registered dev environment

clio update-client-unit-schema --schema-name UsrMyModule --body-file ./my-module.js --dry-run -e dev
validate the schema can be resolved without saving it

clio update-client-unit-schema --schema-name UsrMyModule --body "define(..." -e dev
save an inline body for the schema
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#update-client-unit-schema)
