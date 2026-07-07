# create-server-to-server-oauth-app

## Command Type

Installation and Deployment commands

## Name

create-server-to-server-oauth-app - Create a server-to-server (client_credentials) OAuth app in Creatio via OAuthConfigService REST

## Description

Creates a server-to-server (`client_credentials`) OAuth app in Creatio through the platform
`OAuthConfigService/AddClient` endpoint over REST, binding it to a system user. Supply
`--system-user-id` (from [`resolve-oauth-system-user`](resolve-oauth-system-user.md) or
[`create-oauth-technical-user`](create-oauth-technical-user.md)) or `--system-user` by name
(defaults to `Supervisor`).

The returned client id and client secret are surfaced **only** in the structured command result. On
the CLI this result is printed as a single JSON object (`clientId`, `clientSecret`, `systemUserId`,
`name`) to STDOUT, with a one-time-capture warning on STDERR (so `... | jq` keeps STDOUT clean); via
MCP it is the structured tool record. The secret is never written to logs and is **not** persisted to
clio settings by this command (that is the responsibility of the higher-level
[`deploy-identity`](deploy-identity.md) flow). Capture the secret immediately; it cannot be retrieved
again.

## Synopsis

```
clio create-server-to-server-oauth-app -e ENVIRONMENT [OPTIONS]
```

## Options

| Option | Required | Default | Description |
|---|---|---|---|
| `-e, --environment` | Yes | | Registered Creatio environment. |
| `--system-user-id` | No | | System user id to bind the OAuth app to. |
| `--system-user` | No | `Supervisor` | System user name to bind the OAuth app to when `--system-user-id` is omitted. |
| `--client-name` | No | `clio s2s` | OAuth client display name. |
| `--client-application-url` | No | `https://github.com/Advance-Technologies-Foundation/clio.git` | OAuth client application URL. |
| `--client-description` | No | `server-to-server integration for clio cli` | OAuth client description. |

## Examples

```
clio create-server-to-server-oauth-app -e c-dev
clio create-server-to-server-oauth-app -e c-dev --system-user-id 410006e1-ca4e-4502-a9ec-e54d922d2c00
```

## Notes

This command is experimental and hidden by default. Enable it before use:

```
clio experimental --name deploy-identity --enable
```

This command mutates Creatio (creates an OAuth client). The generated client secret is returned only
in the structured result and is never logged.
