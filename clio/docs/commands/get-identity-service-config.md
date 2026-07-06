# get-identity-service-config

## Command Type

Installation and Deployment commands

## Name

get-identity-service-config - Read (or derive) the OAuth IdentityService configuration of a Creatio environment over REST

## Description

Reads the OAuth IdentityService configuration of a Creatio environment over REST without any
filesystem or database access, so it works against remote/cloud environments.

It reads the `OAuth20IdentityServerUrl` and `OAuth20IdentityServerClientId` system settings. When
`OAuth20IdentityServerUrl` is empty it derives the IdentityService host by inserting `-is` into the
first label of the Creatio host (for example `186843-crm-bundle.creatio.com` becomes
`186843-crm-bundle-is.creatio.com`).

It reports:

- `identityServerUrl` - the resolved IdentityService base URL
- `source` - `setting`, `derived`, or `none`
- `clientId` - the `OAuth20IdentityServerClientId` value
- `tokenEndpoint` - `{base}/connect/token`
- `discoveryEndpoint` - `{base}/.well-known/openid-configuration`
- `reachable` - whether the discovery document responded with a success status

Use it first when configuring server-to-server OAuth on a remote Creatio.

## Synopsis

```
clio get-identity-service-config -e ENVIRONMENT
```

## Options

| Option | Required | Description |
|---|---|---|
| `-e, --environment` | Yes | Registered Creatio environment to inspect. |

## Examples

```
clio get-identity-service-config -e c-dev
```

## Notes

This command is experimental and hidden by default. Enable it before use:

```
clio experimental --name deploy-identity --enable
```

This command is read-only; it does not modify Creatio or clio settings.
