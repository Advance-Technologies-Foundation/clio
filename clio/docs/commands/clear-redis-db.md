# clear-redis-db

Clear redis database.


## Usage

```bash
clear-redis-db [Name] [options]
```

## Description

clear-redis-db command can be used in CI/CD pipeline or in development
when you need forcible clear a web application (website) cache. Be
attentive, the command only clear  web application cache and doesn't
restart it.

## Aliases

`flushdb`

## Examples

```bash
clio clear-redis-db
clear current web application(website) cache

clio clear-redis-db myapp
clear web application(website) cache that registered as a myapp
```

## Options

```bash
Name (pos. 0)	Application name

--uri               -u          Application uri

--Password			-p          User password

--Login             -l          User login (administrator permission required)

--Environment       -e          Environment name

--Maintainer        -m          Maintainer name
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#clear-redis-db)
