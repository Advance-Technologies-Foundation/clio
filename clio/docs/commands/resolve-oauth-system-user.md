# resolve-oauth-system-user

## Command Type

Installation and Deployment commands

## Name

resolve-oauth-system-user - Resolve a Creatio system user (SysAdminUnit) by name or id over DataService REST

## Description

Resolves a Creatio system user (`SysAdminUnit`) by name (default `Supervisor`) or by id using a
DataService `SelectQuery` over REST. No database access is required, so it works against remote
Creatio environments.

It reports `systemUserId`, `name`, and `found`. Use this to obtain the `systemUserId` that
[`create-server-to-server-oauth-app`](create-server-to-server-oauth-app.md) binds the OAuth app to.
This is the REST-only replacement for the database-direct user resolution used by
[`deploy-identity`](deploy-identity.md).

## Synopsis

```
clio resolve-oauth-system-user -e ENVIRONMENT [OPTIONS]
```

## Options

| Option | Required | Default | Description |
|---|---|---|---|
| `-e, --environment` | Yes | | Registered Creatio environment to query. |
| `--name` | No | `Supervisor` | System user (SysAdminUnit) name to resolve. |
| `--id` | No | | System user (SysAdminUnit) id to resolve. Takes precedence over `--name`. |

## Examples

```
clio resolve-oauth-system-user -e c-dev
clio resolve-oauth-system-user -e c-dev --name "Integration User"
clio resolve-oauth-system-user -e c-dev --id 410006e1-ca4e-4502-a9ec-e54d922d2c00
```

## Notes

This command is experimental and hidden by default. Enable it before use:

```
clio experimental --name deploy-identity --enable
```

This command is read-only; it does not modify Creatio.
