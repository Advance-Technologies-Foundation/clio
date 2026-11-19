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

By default the command returns as soon as the restart request is sent,
without waiting for the application to come back up. Pass `--wait-ready`
to wait, after the restart, until the application answers an authenticated
application-layer round-trip — not merely the liveness health-check ping —
and return only once it is genuinely serving (or exit non-zero on timeout).
This is the signal to rely on before verifying a fix, instead of hand-rolled
polling. If the application refuses the environment credentials while waiting,
the wait stops early with an authentication error instead of burning the whole
`--ready-timeout` on a failure that waiting cannot fix.

If the application refuses the restart request itself with a 4xx status (no rights, an unknown route), the
command fails with a non-zero exit code and says so; it no longer reports "Done" for a restart the
application refused. With `--wait-ready` it then does not wait. Over MCP, the failed
`restart-by-environment-name` / `restart-by-credentials` result carries the application's answer as
`site-response`, and beside it a `diagnostic` whose `side-effect` is `none` (nothing was restarted) only when the
request went out once and was refused with 401, 403, 405, 413, 414, 415 or 431. A 5xx answer, which a restart in progress behind a proxy can produce, is reported as a
warning that the restart is not confirmed; with `--wait-ready`, readiness decides.

The readiness round-trip does not perform an explicit login: the client establishes
its session on demand, so OAuth and bearer-token environments (which carry a token
instead of a login/password) are supported by the same path.

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

--wait-ready                        After requesting the restart, wait until the application
                                     answers an authenticated application-layer round-trip
                                     (a passing liveness ping alone is not treated as ready)
                                     before returning. Exits non-zero if it does not become
                                     ready in time.

--ready-timeout                     Max seconds to wait for readiness when --wait-ready is
                                     set (default: 600).
```

## Example

```bash
clio restart-web-app
restarts current web application(website)

clio restart-web-app myapp
restarts web application(website) that registered as a myapp

clio restart-web-app -e myapp --wait-ready
restarts myapp and waits (up to the default 600s) until it is genuinely
serving an authenticated application-layer request before returning

clio restart-web-app -e myapp --wait-ready --ready-timeout 900
same as above, with a 900s readiness budget for a slow-starting instance
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#restart-web-app)
