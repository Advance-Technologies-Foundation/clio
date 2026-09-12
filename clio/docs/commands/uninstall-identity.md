# uninstall-identity

## Name

uninstall-identity - Remove the local IdentityService attached to a Creatio environment.

## Synopsis

```bash
clio uninstall-identity -e ENVIRONMENT [--skip-crm-cleanup]
```

## Description

Removes the IdentityService recorded in the environment's `IdentityService` component
in clio appsettings. Creatio, its database, users and OAuth application records remain.
Enable this experimental command with `clio experimental --name deploy-identity --enable`.

The recorded folder, IIS target and application pool identify what to remove. Clio does
not infer ownership from credentials, a naming convention, or database connections.
It validates the recorded scope against current IIS configuration before removing the
target. Shared pools are preserved. Incomplete attachments, overlapping CRM folders,
replaced IIS targets and references from other registered environments prevent removal.

Before stopping IdentityService, the command clears matching Creatio identity system
settings while authentication is still available. It then removes the IIS target,
unused pool and deployment folder, clears the attachment to empty fields, and clears
clio OAuth credentials only when they still point to that identity. It retains the
environment registration. No database is dropped or modified by artifact cleanup.

An empty attachment succeeds without deleting anything. Older deployments with no
attachment are not discovered automatically: record their verified deployment details
in the environment before removing them. A partial failure retains the attachment so
the same command can retry missing artifacts safely.

## Options

| Option | Description |
| --- | --- |
| `-e`, `--environment` | Required explicit registered environment name. |
| `--skip-crm-cleanup` | Recovery only; defaults to false. Skip Creatio system-setting cleanup when authentication is unavailable. A warning identifies the references that must be repaired manually. |

## Environment component

The appsettings JSON schema describes this optional component. Environments without
IdentityService serialize empty fields:

```json
"IdentityService": {
  "EnvironmentPath": "",
  "IisTarget": "",
  "ApplicationPool": "",
  "Uri": "",
  "CrmReferencesCleared": false
}
```

`deploy-identity` records the resolved absolute folder, IIS name, pool and service URL
before creating artifacts, including with `--no-app`. `CrmReferencesCleared` is a
cleanup checkpoint managed by clio; leave it false when entering a verified existing
attachment. It lets an interrupted uninstall resume without authenticating through an
identity that has already stopped.

## Examples

```bash
clio uninstall-identity -e local-dev
clio uninstall-identity -e local-dev --skip-crm-cleanup
```

To remove Creatio and its attached identity together, use
`clio uninstall-creatio -e local-dev`. That command validates the attachment first,
removes identity before Creatio, and drops the Creatio database as usual.

## MCP

`uninstall-identity` accepts `environment-name` and optional `skip-crm-cleanup`.
It is destructive and resolves the requested environment for each invocation.

## Related commands

- [deploy-identity](deploy-identity.md)
- [uninstall-creatio](uninstall-creatio.md)
