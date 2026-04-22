# create-schema

## Command Type

    Development commands

## Name

create-schema - Create a new C# source-code schema on a remote Creatio environment

**Aliases:** `schema-create`

## Description

The create-schema command creates a new C# source-code schema on a remote Creatio environment
via SourceCodeSchemaDesignerService. The schema is saved directly to the server; no local
workspace files are created.

The schema-name must start with a letter and contain only letters, digits, or underscores.
The name must be unique within the environment.

## Synopsis

```bash
clio create-schema [options]
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
clio create-schema --schema-name UsrMyHelper --package-name Custom -e dev
# Create UsrMyHelper in the Custom package on the dev environment

clio create-schema --schema-name UsrMyHelper --package-name Custom --caption "My Helper" -e dev
# Create with a display caption
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#create-schema)
