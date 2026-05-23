# generate-source-code

## Command Type

    Development commands

## Name

generate-source-code - generate source code for schemas in Creatio

## Aliases

gsc

## Description

The generate-source-code command triggers source code generation for schemas
in the Creatio configuration (equivalent to the "Generate source code" button
in the Configuration section). By default, it generates source code for all
schemas. You can limit generation to modified schemas only or run the operation
in the background.

## Synopsis

```bash
generate-source-code [options]
```

## Options

```bash
--modified              -m          Generate source code only for modified schemas
Default: false

--required              -r          Generate source code only for schemas that require it
Default: false

--background            -b          Run source code generation in background (matches UI behaviour for generate-all)
Default: false

--timeout                           Request timeout in milliseconds
Default: 100000

--uri                   -u          Application uri

--Password              -p          User password

--Login                 -l          User login (administrator permission required)

--Environment           -e          Environment name

--Maintainer            -m          Maintainer name

--clientId                          OAuth client id

--clientSecret                      OAuth client secret

--authAppUri                        OAuth app URI
```

## Examples

```bash
clio generate-source-code -e development
Generate source code for all schemas in the development environment

clio gsc -e development
Same as above using the short alias

clio generate-source-code --modified -e development
Generate source code only for schemas that were modified

clio generate-source-code --background -e production
Run generation in background (returns immediately, generation continues on server)

clio generate-source-code -u "https://myapp.creatio.com" -l "admin" -p "password"
Generate source code using direct connection parameters
```

## Output

    0       Source code generation completed successfully
    1       Source code generation failed or an error occurred

## Prerequisites

- Valid Creatio environment with accessible web services
- Appropriate credentials (admin permission)
- Network connectivity to the target Creatio instance

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#generate-source-code)
