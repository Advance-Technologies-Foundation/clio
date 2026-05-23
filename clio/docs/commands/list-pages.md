# list-pages

## Command Type

    Development commands

## Name

list-pages - List Freedom UI page schemas in a Creatio environment

**Aliases:** `page-list`

## Description

The list-pages command queries SysSchema for Freedom UI client schemas and
returns a JSON envelope with the matching page names, schema UIds,
package names, and parent schema names. Use this command to discover
candidate schema names before calling get-page.

## Synopsis

```bash
clio list-pages [options]
```

## Options

```bash
--package-name                     Optional package name filter

--search-pattern                  Optional schema name contains filter

--limit                           Maximum number of results. Default: 50

--uri                    -u       Application uri

--Password               -p       User password

--Login                  -l       User login (administrator permission required)

--Environment            -e       Environment name

--Maintainer             -m       Maintainer name
```

## Example

```bash
clio list-pages -e dev
list Freedom UI pages from the registered dev environment

clio list-pages --search-pattern FormPage --limit 20 -e dev
list up to 20 Freedom UI pages whose schema names contain FormPage

clio list-pages --package-name UsrApp -u https://my-creatio -l Supervisor -p Supervisor
list Freedom UI pages from the UsrApp package using direct connection arguments
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#list-pages)
