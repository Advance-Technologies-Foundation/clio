# restart-web-app

## Command Type

    CI/CD commands

## Name

restart-web-app - restart web application(website)

## Description

restart-web-app command can be used in CI/CD pipeline or in development
when you need forcible restart a web application (website). Be attentive,
the command restart only web application and doesn't clear application
cache.

## Synopsis

```bash
restart-web-app [Name] [options]
```

## Options

```bash
Name (pos. 0)	Application name

--uri                   -u          Application uri

--Password              -p          User password

--Login                 -l          User login (administrator permission required)

--Environment           -e          Environment name

--Maintainer            -m          Maintainer name
```

## Example

```bash
clio restart-web-app
restarts current web application(website)

clio restart-web-app myapp
restarts web application(website) that registered as a myapp
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#restart-web-app)
