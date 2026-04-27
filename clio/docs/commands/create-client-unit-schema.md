# create-client-unit-schema

## Command Type

    Development commands

## Name

create-client-unit-schema - Create a new JavaScript (ClientUnit) schema on a remote Creatio environment

**Aliases:** `client-unit-schema-create`

## Description

The create-client-unit-schema command creates a new JavaScript (ClientUnit) schema on a
remote Creatio environment via ClientUnitSchemaDesignerService. The schema is saved
directly to the server; no local workspace files are created.

The schema-name must start with a letter and contain only letters, digits, or underscores.
The name must be unique within the environment.

## Synopsis

```bash
clio create-client-unit-schema [options]
```

## Options

```bash
--schema-name                      New schema name (required)

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
clio create-client-unit-schema --schema-name UsrHelperModule --package-name Custom -e dev
# Create UsrHelperModule in the Custom package on the dev environment

clio create-client-unit-schema --schema-name UsrHelperModule --package-name Custom --caption "Helper Module" -e dev
# Create with a display caption
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#create-client-unit-schema)
