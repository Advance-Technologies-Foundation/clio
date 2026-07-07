# create-oauth-technical-user

## Command Type

Installation and Deployment commands

## Name

create-oauth-technical-user - Create a Creatio technical user for a server-to-server OAuth app via OAuthConfigService REST

## Description

Creates a Creatio technical user through the platform `OAuthConfigService/CreateTechnicalUser`
endpoint over REST and returns its `systemUserId`, for binding a subsequent server-to-server OAuth
app.

### Role grant is deferred

This REST-only path does **not** assign any Creatio role to the new user. The
[`deploy-identity`](deploy-identity.md) role grant is database-direct (it writes `SysUserInRole` /
`SysAdminUnitInRole` with Npgsql/SqlClient) and cannot run against a remote environment, and no
clean REST endpoint for granting a role to a user is available today. The reported `roleGranted`
flag is therefore always `false` and `roleGrantNotice` explains the follow-up.

If the server-to-server app requires elevated permissions, grant the role manually in Creatio, or
use the local `deploy-identity --create-tech-user` path against an environment with database access.
Prefer [`resolve-oauth-system-user`](resolve-oauth-system-user.md) (binding to an existing user such
as `Supervisor`) unless a dedicated technical user is required.

> Open question: whether `OAuthConfigService/CreateTechnicalUser` already provisions the necessary
> roles server-side is not confirmed; verify in your environment before relying on the new user for
> privileged operations.

## Synopsis

```
clio create-oauth-technical-user -e ENVIRONMENT [OPTIONS]
```

## Options

| Option | Required | Default | Description |
|---|---|---|---|
| `-e, --environment` | Yes | | Registered Creatio environment. |
| `--name` | No | `clio_oauth_technical_user` | Technical user name to create. |

## Examples

```
clio create-oauth-technical-user -e c-dev
clio create-oauth-technical-user -e c-dev --name svc_clio_s2s
```

## Notes

This command is experimental and hidden by default. Enable it before use:

```
clio experimental --name deploy-identity --enable
```

This command mutates Creatio (creates a user).
