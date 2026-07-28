# register-sso-provider

## Command Type

    Identity commands

## Name

register-sso-provider - register a new OIDC SSO provider in Creatio

## Aliases

sso-register-provider

## Description

The `register-sso-provider` command posts to the Creatio endpoint `api/SsoProvider/Register` to
register a new OIDC single sign-on provider. The operation is **create-only**: if a provider with
the given `--code` already exists, the server rejects the request and the command exits non-zero
with the server error message. Use the SSO settings UI in Creatio to update an existing provider.

The client secret is resolved in priority order:

1. `--oidc-client-secret-file`
2. `--oidc-client-secret`
3. `CLIO_OIDC_CLIENT_SECRET` environment variable

Prefer the file or environment variable to avoid leaking the secret into shell history.

> **Platform routing.** The command follows the standard Creatio convention: the request is sent to
> `0/api/SsoProvider/Register` on .NET Framework hosts and to `api/SsoProvider/Register` on .NET Core
> hosts. The `0/` workspace prefix is added automatically for .NET Framework, so make sure the
> environment's runtime flag (`--IsNetCore`) matches the target instance.

## Synopsis

```bash
register-sso-provider --code <code> --name <name> --issuer-url <url> --oidc-client-id <id> [options]
```

## Options

```bash
--code                              Unique provider code / lookup key (^[A-Za-z0-9._-]{1,64}$) (required)

--name                              Display name for the SSO provider (required)

--issuer-url                        OIDC issuer / authority URL, https:// (required)

--oidc-client-id                    OAuth client ID registered with the provider (required)

--oidc-client-secret                OAuth client secret (prefer the file or env var)

--oidc-client-secret-file           Path to a file containing the OIDC client secret
                                    Mutually exclusive with --oidc-client-secret

--discovery-url                     OIDC discovery endpoint
                                    Default: <issuer-url>/.well-known/openid-configuration

--logout-url                        End-session endpoint URL for single logout

--format                            Output format: text (default) or json
Default: text

--timeout                           Request timeout in milliseconds
Default: 100000

--uri                   -u          Application uri

--Password              -p          User password

--Login                 -l          User login (administrator permission required)

--Environment           -e          Environment name

--Maintainer            -m          Maintainer name

--clientId                          OAuth client id

--clientSecret                      OAuth client secret
```

## Examples

```bash
# Register a provider; the secret comes from the CLIO_OIDC_CLIENT_SECRET env var
export CLIO_OIDC_CLIENT_SECRET="my-provider-secret"
clio register-sso-provider -e development \
  --code azure-ad --name "Azure Active Directory" \
  --issuer-url https://login.microsoftonline.com/my-tenant-id/v2.0 \
  --oidc-client-id 00000000-0000-0000-0000-000000000000

# Register with a secret file and print the full JSON payload
clio sso-register-provider -e development \
  --code okta --name Okta --issuer-url https://my-org.okta.com \
  --oidc-client-id my-client-id \
  --oidc-client-secret-file ./okta.secret \
  --discovery-url https://my-org.okta.com/.well-known/openid-configuration \
  --logout-url https://my-org.okta.com/oauth2/v1/logout \
  --format json
```

## Output

    0       Provider registered successfully
    1       Registration failed (validation error, conflict, or server/network error)

## Notes

- `--code` is the stable identifier; the server validates it against `^[A-Za-z0-9._-]{1,64}$`.
- `--oidc-client-secret` and `--oidc-client-secret-file` are mutually exclusive.
- An empty or whitespace-only secret file is rejected before the request is sent.
- The server (not clio) probes the OIDC discovery endpoint before saving; an unreachable endpoint
  surfaces as a server-side validation error.
- The caller must have the `CanRegisterSsoProvider` system operation right in Creatio.
- Omit `--discovery-url` when the provider exposes the standard
  `/.well-known/openid-configuration` path under `--url`.

## Prerequisites

- Valid Creatio environment registered with clio (`clio reg-web-app ...`)
- The authenticated user has the `CanRegisterSsoProvider` system operation right

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#register-sso-provider)
- [Identity assertion & token exchange guide](../identity-assertion-token-exchange.md)
