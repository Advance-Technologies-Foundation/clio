# healthcheck

## Command Type

    CI/CD commands

## Name

healthcheck - Healthcheck monitoring

## Description

The healthcheck command performs health monitoring of Creatio web
applications by checking the availability and responsiveness of WebHost
and/or WebAppLoader endpoints. This command is useful for monitoring
application status in CI/CD pipelines or during development.

## Synopsis

```bash
healthcheck [options]
```

## Aliases

hc

## Options

```bash
--WebHost               -h          Check web-host endpoint
(/0/api/HealthCheck/Ping)

--WebApp                -a          Check web-app endpoint
(/api/HealthCheck/Ping)

--uri                   -u          Application uri

--Password              -p          User password

--Login                 -l          User login (administrator permission required)

--Environment           -e          Environment name
```

## Example

```bash
clio healthcheck -a true
checks WebAppLoader health status

clio healthcheck -h true
checks WebHost health status

clio healthcheck -a true -h true
checks both WebAppLoader and WebHost health status

clio healthcheck -a true -e myenv
checks WebAppLoader health status for environment named myenv
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#healthcheck)
