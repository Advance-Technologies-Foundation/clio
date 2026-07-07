# verify-oauth-app

## Command Type

Installation and Deployment commands

## Name

verify-oauth-app - Verify a server-to-server OAuth app: acquire a client_credentials token and run a bearer DataService smoke test

## Description

Verifies a server-to-server OAuth app end to end over REST. It acquires a `client_credentials`
access token from the IdentityService token endpoint, then runs a minimal bearer-authenticated
Creatio DataService smoke request with that token.

It reports:

- `tokenAcquired` - whether a `client_credentials` access token was acquired
- `dataServiceStatus` - the HTTP status of the bearer DataService smoke request (`0` when skipped)
- `ok` - whether the token was acquired **and** `dataServiceStatus` is `200`
- `identityServerUrl` - the IdentityService base URL used for the token request

The access token text is never returned or logged.

## Synopsis

```
clio verify-oauth-app -e ENVIRONMENT --client-id ID --client-secret SECRET [OPTIONS]
```

## Options

| Option | Required | Description |
|---|---|---|
| `-e, --environment` | Yes | Registered Creatio environment. |
| `--client-id` | Yes | OAuth client id to verify. |
| `--client-secret` | Yes | OAuth client secret to verify. Never returned or logged. |
| `--identity-server-url` | No | Explicit IdentityService base URL. Defaults to the `OAuth20IdentityServerUrl` setting, then a derived `-is` host. |

## Examples

```
clio verify-oauth-app -e c-dev --client-id my-client --client-secret my-secret
clio verify-oauth-app -e c-dev --client-id my-client --client-secret my-secret \
    --identity-server-url https://c-dev-is.creatio.com
```

## Notes

This command is experimental and hidden by default. Enable it before use:

```
clio experimental --name deploy-identity --enable
```

This command is read-only; it does not modify Creatio. The access token is never logged.
