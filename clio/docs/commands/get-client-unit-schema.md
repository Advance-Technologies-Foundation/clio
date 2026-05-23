# get-client-unit-schema

## Command Type

    Development commands

## Name

get-client-unit-schema - Read body and metadata of a client unit (JavaScript) schema on a remote Creatio environment

**Aliases:** `client-unit-schema-get`

## Description

The get-client-unit-schema command reads the body and metadata of an existing client unit
(JavaScript) schema on a remote Creatio environment via ClientUnitSchemaDesignerService.
The schema is resolved by name (ManagerName=ClientUnitSchemaManager) and the full schema
descriptor (UId, name, caption, package, body, etc.) is returned. No local workspace files
are created or modified.

When `--output-file` is set, the schema body is written to the specified file and the body
field is omitted from the response JSON printed to stdout. This keeps the terminal output
small and makes it easy to edit the body locally before piping it back through
`update-client-unit-schema`.

## Synopsis

```bash
clio get-client-unit-schema [options]
```

## Options

```bash
--schema-name                      Client unit schema name (required)

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
clio get-client-unit-schema --schema-name UsrMySection -e dev
# Print body and metadata of UsrMySection from the dev environment

clio get-client-unit-schema --schema-name UsrMySection --output-file /tmp/UsrMySection.js -e dev
# Save the body to /tmp/UsrMySection.js; response JSON omits the body

clio client-unit-schema-get --schema-name UsrMySection -e dev
# Same as the first example using the alias
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#get-client-unit-schema)
