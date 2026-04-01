# reg-web-app

## Command Type

    CI/CD commands

## Name

reg-web-app - create/update a web application (website)

## Description

Register new web application settings or update existing ones.
When `--IsNetCore` is omitted for URL-based registration, clio auto-detects
whether the target site uses `.NET Core / NET8` or `.NET Framework` and saves
the resolved `IsNetCore` value in local settings.
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

--IsNetCore            -i           Optional runtime override.
                                    Omit to auto-detect from the target URL.
                                    true = .NET Core / NET8
                                    false = .NET Framework
```

## Example

```bash
clio reg-web-app <ENVIRONMENT_NAME> -u http://mysite.creatio.com -l administrator -p password
creates new environment, named <ENVIRONMENT_NAME> or updates existing environment settings
```

```bash
clio reg-web-app <ENVIRONMENT_NAME> -u http://mysite.creatio.com -l administrator -p password -i false
overrides auto-detection and forces .NET Framework registration
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#reg-web-app)
