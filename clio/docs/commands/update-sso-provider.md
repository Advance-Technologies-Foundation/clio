# update-sso-provider

## Command Type

    Identity commands

## Name

update-sso-provider - update an SSO provider in place

## Description

Updates one SSO provider in place through UpsertProvider: the same provider row, a new client ID or client secret, and, when events are on, the stream reconciled at once. Use it after the provider's client secret was rotated: unlike register-sso-provider it accepts an existing code. Values that are not given are kept; --oidc-client-id is required on every call. A changed client ID moves the stream to the new client.

When no provider has the code, the site creates one (a new provider needs a client secret; with --creatio-id it gets the Creatio ID template). The answer says which: created true or false.

The client secret comes from --oidc-client-secret-file or --oidc-client-secret; without either the stored secret is kept. Unlike register-sso-provider, the CLIO_OIDC_CLIENT_SECRET environment variable is not read, so a variable left in the shell cannot replace this provider's secret. The secret is never printed.

The code identity-v3 is always sent as Creatio ID, with or without --creatio-id.

> **The settings service.** Every command of this family posts to one method of the site's settings service SsoSecurityEventsSettingsService (rest/SsoSecurityEventsSettingsService/<Method>, with the 0/ prefix on .NET Framework). The service comes with the SsoSecurityEvents package of Creatio 10.2 and later and needs the CanManageSso system operation. It answers every call with a reason code and never with a failure: the command prints the reason ("none" on success) and exits 0 whatever it is. It exits 1 only when the call itself failed: a refused option, no answer, an answer that is not the service's, or a site without the service (HTTP 404, reported as not supported). Read the reason before acting on the result.

> **Platform routing.** The request goes to `0/rest/SsoSecurityEventsSettingsService/UpsertProvider` on .NET Framework hosts and to `rest/SsoSecurityEventsSettingsService/UpsertProvider` on .NET Core hosts; make sure the environment's runtime flag (`--IsNetCore`) matches the site.

## Synopsis

```bash
update-sso-provider --code <code> --oidc-client-id <id> [options]
```

## Options

```bash
--code                              Code of the SSO provider (required)
                                    Creatio ID is always identity-v3

--oidc-client-id                    OAuth client ID registered with the provider (required)

--oidc-client-secret                OAuth client secret (prefer the file or env var)

--oidc-client-secret-file            Path to a file containing the OIDC client secret
                                    Mutually exclusive with --oidc-client-secret

--name                              Display name
                                    Default: kept

--issuer-url                        OIDC issuer / authority URL
                                    Default: kept

--discovery-url                     OIDC discovery endpoint
                                    Default: kept; ignored for Creatio ID

--creatio-id                        The provider is Creatio ID; requires --code identity-v3

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
# Store a rotated client secret for Creatio ID, keeping the provider and its stream
clio update-sso-provider -e demo-site --code identity-v3 --creatio-id \
    --oidc-client-id demo-site --oidc-client-secret-file ./new.secret
```

## Output

Text output is one "name: value" line per member the site returned: reason, reasonDetail, oauthError, providerId, isCreated, testResult, streamDeleted, then the state the call left (providerExists, featureEnabled, enabled, siteAddress, siteStatus, streamStatus, lastError, lastTestResult, lastTestOn, changedByOperation, changedOn, isDisablePending). --format json prints the site's whole answer. No answer carries a secret. Text from the site is printed on one line, with anything that looks like a credential removed.

    0       The site answered; read the printed reason code
    1       The call failed: refused option, no or foreign answer, service not found (404)
    78      Refused before the call: the site runs Creatio below 10.2, or its version could not be read

The reason codes are listed in [get-sso-security-events](get-sso-security-events.md#reason-codes).

## Notes

- `--creatio-id` with a code other than `identity-v3` is refused: the site stores Creatio ID under `identity-v3` whatever code it is sent.
- The call is sent once and never retried or replayed; after a failure without an answer, check with `get-sso-security-events` before repeating it.

## Availability

This command is experimental and hidden by default. Enable it before use:

```bash
clio experimental --name sso-security-events --enable
```

Before the call, clio reads the site's Creatio version and refuses the command on a site below 10.2 with exit code 78 (`version-too-old`); no request reaches the settings service.

## Prerequisites

- A Creatio 10.2+ site with the SsoSecurityEvents package, registered with clio (`clio reg-web-app ...`)
- The `sso-security-events` feature flag is enabled in clio (`clio experimental --name sso-security-events --enable`)
- The authenticated user has the `CanManageSso` system operation right

## Related commands

- [`get-sso-security-events`](get-sso-security-events.md)
- [`enable-sso-security-events`](enable-sso-security-events.md)
- [`disable-sso-security-events`](disable-sso-security-events.md)
- [`send-sso-security-test-event`](send-sso-security-test-event.md)
- [`set-sso-security-events-address`](set-sso-security-events-address.md)
- [`remove-sso-provider`](remove-sso-provider.md)
- [`register-sso-provider`](register-sso-provider.md)

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#update-sso-provider)
