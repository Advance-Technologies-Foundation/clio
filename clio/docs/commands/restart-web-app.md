# restart-web-app

Restart a web application.


## Usage

```bash
restart-web-app [Name] [options]
```

## Description

restart-web-app command can be used in CI/CD pipeline or in development
when you need forcible restart a web application (website). Be attentive,
the command restart only web application and doesn't clear application
cache.

## Aliases

`restart`

## Examples

```bash
clio restart-web-app
restarts current web application(website)

clio restart-web-app myapp
restarts web application(website) that registered as a myapp
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

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#restart-web-app)
