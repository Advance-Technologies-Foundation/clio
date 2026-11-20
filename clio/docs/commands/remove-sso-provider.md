# remove-sso-provider

## Command Type

    Identity commands

## Name

remove-sso-provider - remove an SSO provider and its security events stream

## Description

Removes one SSO provider through RemoveProvider. The site first deletes its own stream at the provider (best effort, 10 s), then the provider and its stream record. A stream it could not delete is recorded for the banner of SSO settings (reason Unavailable, detail StreamNotDeleted).

With --local-only the site calls nothing at the provider and removes the provider on the site only: use it on a copied site whose stream record belongs to its source, so the source keeps receiving its events. Once the copy has a client of its own, register the provider and run enable-sso-security-events with the copy's address to give it its own stream.

> **The settings service.** Every command of this family posts to one method of the site's settings service SsoSecurityEventsSettingsService (rest/SsoSecurityEventsSettingsService/<Method>, with the 0/ prefix on .NET Framework). The service comes with the SsoSecurityEvents package of Creatio 10.2 and later and needs the CanManageSso system operation. It answers every call with a reason code and never with a failure: the command prints the reason ("none" on success) and exits 0 whatever it is. It exits 1 only when the call itself failed: a refused option, no answer, an answer that is not the service's, or a site without the service (HTTP 404, reported as not supported). Read the reason before acting on the result.

> **Platform routing.** The request goes to `0/rest/SsoSecurityEventsSettingsService/RemoveProvider` on .NET Framework hosts and to `rest/SsoSecurityEventsSettingsService/RemoveProvider` on .NET Core hosts; make sure the environment's runtime flag (`--IsNetCore`) matches the site.

## Synopsis

```bash
remove-sso-provider --code <code> [--local-only] [options]
```

## Options

```bash
--code                              Code of the SSO provider (required)
                                    Creatio ID is always identity-v3

--local-only                        Remove on the site only; call nothing at the provider

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
# Remove the copied Creatio ID provider without touching the source's stream
clio remove-sso-provider -e copied-site --code identity-v3 --local-only

# Remove the provider and delete the site's stream at the provider
clio remove-sso-provider -e old-site --code identity-v3
```

## Output

Text output is one "name: value" line per member the site returned: reason, reasonDetail, oauthError, providerId, isCreated, testResult, streamDeleted, then the state the call left (providerExists, featureEnabled, enabled, siteAddress, siteStatus, streamStatus, lastError, lastTestResult, lastTestOn, changedByOperation, changedOn, isDisablePending). --format json prints the site's whole answer. No answer carries a secret. Text from the site is printed on one line, with anything that looks like a credential removed.

    0       The site answered; read the printed reason code
    1       The call failed: refused option, no or foreign answer, service not found (404)

The reason codes are listed in [get-sso-security-events](get-sso-security-events.md#reason-codes).

## Notes

- The call is sent once and never retried or replayed; after a failure without an answer, check with `get-sso-security-events` before repeating it.

## Prerequisites

- A Creatio 10.2+ site with the SsoSecurityEvents package, registered with clio (`clio reg-web-app ...`)
- The authenticated user has the `CanManageSso` system operation right

## Related commands

- [`get-sso-security-events`](get-sso-security-events.md)
- [`enable-sso-security-events`](enable-sso-security-events.md)
- [`disable-sso-security-events`](disable-sso-security-events.md)
- [`send-sso-security-test-event`](send-sso-security-test-event.md)
- [`set-sso-security-events-address`](set-sso-security-events-address.md)
- [`update-sso-provider`](update-sso-provider.md)
- [`register-sso-provider`](register-sso-provider.md)

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#remove-sso-provider)
