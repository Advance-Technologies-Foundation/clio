# disable-sso-security-events

## Command Type

    Identity commands

## Name

disable-sso-security-events - turn security events off for an SSO provider

## Description

Turns security events off for one SSO provider through DisableEvents. The site stores the switch, disables its own stream at the provider and records the change as an operation ("Disabled by Creatio support ..." on the card). A provider that cannot be reached is disabled at the site's next start.

Do not run it on a copied site that still holds its source's stream record: the stream belongs to the source, which would stop receiving its events. Use remove-sso-provider --local-only there.

> **The settings service.** Every command of this family posts to one method of the site's settings service SsoSecurityEventsSettingsService (rest/SsoSecurityEventsSettingsService/<Method>, with the 0/ prefix on .NET Framework). The service comes with the SsoSecurityEvents package of Creatio 10.2 and later and needs the CanManageSso system operation. It answers every call with a reason code and never with a failure: the command prints the reason ("none" on success) and exits 0 whatever it is. It exits 1 only when the call itself failed: a refused option, no answer, an answer that is not the service's, or a site without the service (HTTP 404, reported as not supported). Read the reason before acting on the result.

> **Platform routing.** The request goes to `0/rest/SsoSecurityEventsSettingsService/DisableEvents` on .NET Framework hosts and to `rest/SsoSecurityEventsSettingsService/DisableEvents` on .NET Core hosts; make sure the environment's runtime flag (`--IsNetCore`) matches the site.

## Synopsis

```bash
disable-sso-security-events --code <code> [options]
```

## Options

```bash
--code                              Code of the SSO provider (required)
                                    Creatio ID is always identity-v3

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
# Turn events off for Creatio ID
clio disable-sso-security-events -e demo-site --code identity-v3
```

## Output

Text output is one "name: value" line per member the site returned: reason, reasonDetail, oauthError, providerId, isCreated, testResult, streamDeleted, then the state the call left (providerExists, featureEnabled, enabled, siteAddress, siteStatus, streamStatus, lastError, lastTestResult, lastTestOn, changedByOperation, changedOn, isDisablePending). --format json prints the site's whole answer. No answer carries a secret. Text from the site is printed on one line, with anything that looks like a credential removed.

    0       The site answered; read the printed reason code
    1       The call failed: refused option, no or foreign answer, service not found (404)
    78      Refused before the call: the site runs Creatio below 10.2, or its version could not be read

The reason codes are listed in [get-sso-security-events](get-sso-security-events.md#reason-codes).

## Notes

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
- [`send-sso-security-test-event`](send-sso-security-test-event.md)
- [`set-sso-security-events-address`](set-sso-security-events-address.md)
- [`update-sso-provider`](update-sso-provider.md)
- [`remove-sso-provider`](remove-sso-provider.md)
- [`register-sso-provider`](register-sso-provider.md)

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#disable-sso-security-events)
