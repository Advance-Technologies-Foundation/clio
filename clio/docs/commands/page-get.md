# page-get

Get a Freedom UI page bundle and raw schema body.


## Usage

```bash
clio page-get [options]
```

## Description

The page-get command resolves the requested Freedom UI page, loads its full
designer hierarchy, builds the effective merged bundle, and returns a JSON
envelope with nested page metadata, bundle data, and raw.body. Use raw.body
as the editable payload for page-update.

## Examples

```bash
clio page-get --schema-name UsrTodo_FormPage -e dev
return the merged Freedom UI bundle and raw body for UsrTodo_FormPage

clio page-get --schema-name UsrTodo_FormPage -u https://my-creatio -l Supervisor -p Supervisor
read a Freedom UI page using direct connection arguments
```

## Options

```bash
--schema-name                      Page schema name to read

--uri                    -u       Application uri

--Password               -p       User password

--Login                  -l       User login (administrator permission required)

--Environment            -e       Environment name

--Maintainer             -m       Maintainer name
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#page-get)
