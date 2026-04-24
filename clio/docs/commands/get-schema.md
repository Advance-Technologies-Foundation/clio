# get-schema

## Command Type

    Development commands

## Name

get-schema - Read body and metadata of a C# source-code schema on a remote Creatio environment

**Aliases:** `schema-get`

## Description

The get-schema command reads the body and metadata of an existing C# source-code schema on a
remote Creatio environment via SourceCodeSchemaDesignerService. The schema is resolved by name
(ManagerName=SourceCodeSchemaManager) and the full schema descriptor (UId, name, caption,
package, body, etc.) is returned. No local workspace files are created or modified.

When `--output-file` is set, the schema body is written to the specified file and the body
field is omitted from the response JSON printed to stdout. This keeps the terminal output
small and makes it easy to edit the body locally before piping it back through
`update-schema`.

## Synopsis

```bash
clio get-schema [options]
```

## Options

```bash
--schema-name                      C# source-code schema name (required)

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
clio get-schema --schema-name UsrMyHelper -e dev
# Print body and metadata of UsrMyHelper from the dev environment

clio get-schema --schema-name UsrMyHelper --output-file /tmp/UsrMyHelper.cs -e dev
# Save the body to /tmp/UsrMyHelper.cs; response JSON omits the body

clio schema-get --schema-name UsrMyHelper -e dev
# Same as the first example using the alias
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#get-schema)
