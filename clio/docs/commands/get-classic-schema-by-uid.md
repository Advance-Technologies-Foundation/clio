# get-classic-schema-by-uid

## Command Type

    Development commands

## Name

get-classic-schema-by-uid - Read the body and metadata of a specific Classic client unit schema by its SysSchema.UId

**Aliases:** `classic-schema-get`

## Description

The get-classic-schema-by-uid command reads the JavaScript body and metadata of ONE specific
client unit schema record identified by its `SysSchema.UId`. Unlike `get-client-unit-schema`,
which resolves the TOP schema by name, this loads the exact record whose UId you pass — so each
package's schema of a multi-package (base + replacing) schema can be read independently.

This is the per-schema read behind Classic→Freedom migration discovery: pair it with
`list-schema-hierarchy`, which lists every package schema of a name together with its UId, then
read the one you need by UId here.

When `--output-file` is set, the schema body is written to the specified file and the body field
is omitted from the response JSON printed to stdout. The output path must be absolute; relative
paths are rejected because MCP server working directories are not caller-obvious.

## Synopsis

```bash
clio get-classic-schema-by-uid [options]
```

## Options

```bash
--schema-uid                       Client unit schema UId — a specific schema's
                                   SysSchema.UId (required)

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
clio list-schema-hierarchy --schema-name ContactPageV2 -e dev
# First list the schemas to get each schema's UId

clio get-classic-schema-by-uid --schema-uid 8737e39c-ac08-4903-acd0-11570570691d -e dev
# Print the body and metadata of that specific schema

clio get-classic-schema-by-uid --schema-uid 8737e39c-ac08-4903-acd0-11570570691d --output-file /tmp/ContactPageV2.base.js -e dev
# Save the body to the file; response JSON omits the body

clio classic-schema-get --schema-uid 8737e39c-ac08-4903-acd0-11570570691d -e dev
# Same as the second example using the alias
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#get-classic-schema-by-uid)
