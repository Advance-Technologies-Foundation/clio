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

--timeout                           Per-probe request timeout in milliseconds. Each probe is
                                    bounded by this value, so a stalled endpoint (one that accepts
                                    the connection but never answers) is reported unhealthy within
                                    the timeout instead of pinning the default ~100s window.

--json                              Emit the unified command envelope
                                    {schemaVersion, ok, command, data, error}
```

## Probe classification

Each probe is issued as a bounded HTTP GET. Only a genuine `2xx` response is reported healthy;
a non-`2xx` status, a transport error, or a connect-but-never-answer stall is reported unhealthy
(and the probe is aborted at `--timeout` rather than the inherited ~100s default).

Redirects are not followed. `/api/HealthCheck/Ping` is anonymous and answers `200` directly, so a
`3xx` means the request was routed elsewhere — most often the login page, whose own `200` would
otherwise be counted as a healthy answer. A redirect is therefore reported unhealthy, with the
`Location` target in the error message.

The probe checks liveness only: it proves the web layer answers, not that the application can
authenticate a request or serve DataService. Use `restart-web-app --wait-ready` (or the
`restart-by-environment-name` MCP tool) when you need a readiness signal that exercises the
application layer.

## JSON output (`--json`)

With `--json`, healthcheck emits exactly one JSON envelope. On success, `data` carries the
per-check results; when any check fails, `ok=false` and `error.code` is `healthcheck-failed`
(exit code `1`). Human-readable progress lines are suppressed in this mode.

```json
{
  "schemaVersion": "1.0",
  "ok": true,
  "command": "healthcheck",
  "data": {
    "healthy": true,
    "checks": [ { "name": "WebAppLoader", "uri": "https://host/api/HealthCheck/Ping", "ok": true, "error": null } ]
  },
  "error": null
}
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

clio healthcheck -a true -h true --json -e myenv
returns a single JSON envelope with per-check results for automation
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#healthcheck)
