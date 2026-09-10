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
clio verify-oauth-app -e ENVIRONMENT [OPTIONS]
```

## Options

| Option | Required | Description |
|---|---|---|
| `-e, --environment` | Yes | Registered Creatio environment. |
| `--client-id` | No | OAuth client id override; supply together with `--client-secret`. Defaults to registered credentials. |
| `--client-secret` | No | OAuth client secret override; supply together with `--client-id`. Never returned or logged. |
| `--identity-server-url` | No | Explicit IdentityService base URL. Defaults to registered `AuthAppUri`, then the `OAuth20IdentityServerUrl` setting, then a derived `-is` host. |

## Examples

```
clio verify-oauth-app -e DEV
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

Verification reads existing credentials and does not change settings or create an OAuth app.
A nonempty bearer token and a successful DataService JSON response are required; HTTP 200 alone is insufficient.
The CRM request uses only the newly issued token, without falling back to username/password. Exit code is 0 on success and 1 on failure.
