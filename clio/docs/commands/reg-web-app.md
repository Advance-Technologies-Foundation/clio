# reg-web-app

## Command Type

    CI/CD commands

## Name

reg-web-app - create/update a web application (website)

## Description

Register new web application settings or update existing ones.

Omit `--IsNetCore` and clio detects the runtime itself, in this order:

1. **Authenticated `SelectQuery` probe** on each route family, when login/password or
   OAuth credentials are available. This is the strongest signal.
2. **Login-page markers** — `/Login/Login.html` for .NET Core / NET8 and
   `/0/Login/NuiLogin.aspx` for .NET Framework. A page that answers, redirect included,
   names the runtime; a page that answers `404` proves that runtime is absent, which
   settles the case even when the other page never answered at all.
3. **Health endpoints**, last and only as a tiebreaker: `/api/HealthCheck/Ping` answers
   on a .NET Framework site as well, so it cannot tell the runtimes apart on its own.

When the probes stay inconclusive clio stops with a diagnostic naming every URL it tried, rather than guessing,
and **registers nothing** — an environment whose runtime was guessed would misdirect every later command. Pass
`--IsNetCore` to skip detection entirely. If no route was served at all, the diagnostic says the site is
unreachable or not serving requests instead of asking for a runtime.

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

--IsNetCore             -i          Override runtime auto-detection:
                                    true for .NET Core / NET8,
                                    false for .NET Framework
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
