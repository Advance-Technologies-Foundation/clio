# reg-web-app

## Command Type

    CI/CD commands

## Name

reg-web-app - create/update a web application (website)

## Description

Register new web application settings or update existing ones.
When credentials are available, clio also validates the chosen route with an
authenticated `SelectQuery` probe. Without credentials, clio falls back to
unauthenticated health and login-marker probes and stops if the result remains
ambiguous.

## Synopsis

```bash
clio reg-web-app <ENVIRONMENT_NAME> -u http://mysite.creatio.com -l administrator -p password
```

## Options

```bash
Name (pos. 0)	Environment(web application) name

--ActiveEnvironment     -a          Set a web application by default

--Safe                  -s          Safe action in this environment

--uri                   -u          Application uri

--Password              -p          User password

--Login                 -l          User login (administrator permission required)

--Maintainer            -m          Maintainer name
```

## Example

```bash
clio reg-web-app <ENVIRONMENT_NAME> -u http://mysite.creatio.com -l administrator -p password
creates new environment, named <ENVIRONMENT_NAME> or updates existing environment settings
```
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#reg-web-app)
