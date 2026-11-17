# set-sso-security-events-address

## Command Type

    Identity commands

## Name

set-sso-security-events-address - change the site address of an SSO provider's security events

## Description

Stores a new site address for the security events of one SSO provider through SetSiteAddress, for a renamed site. With --move-stream the site first updates its own stream at the provider to the new events address, but only when that stream delivers to the stored address: a stream that delivers elsewhere (a copied site) is never moved (ForeignAddress). A provider that refuses the new address answers SiteAddressRefused with its error code. Nothing is stored in either case.

> **The settings service.** Every command of this family posts to one method of the site's settings service SsoSecurityEventsSettingsService (rest/SsoSecurityEventsSettingsService/<Method>, with the 0/ prefix on .NET Framework). The service comes with the SsoSecurityEvents package of Creatio 10.2 and later and needs the CanManageSso system operation. It answers every call with a reason code and never with a failure: the command prints the reason ("none" on success) and exits 0 whatever it is. It exits 1 only when the call itself failed: a refused option, no answer, an answer that is not the service's, or a site without the service (HTTP 404, reported as not supported). Read the reason before acting on the result.

> **Platform routing.** The request goes to `0/rest/SsoSecurityEventsSettingsService/SetSiteAddress` on .NET Framework hosts and to `rest/SsoSecurityEventsSettingsService/SetSiteAddress` on .NET Core hosts; make sure the environment's runtime flag (`--IsNetCore`) matches the site.

## Synopsis

```bash
set-sso-security-events-address --code <code> --site-address <url> [--move-stream] [options]
```

## Options

```bash
--code                              Code of the SSO provider (required)
                                    Creatio ID is always identity-v3

--site-address                      New public https address of the site (required)

--move-stream                       Also move the site's own stream at the provider

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
# Store the new address and move the stream to it
clio set-sso-security-events-address -e renamed-site --code identity-v3 \
    --site-address https://new-name.creatio.com --move-stream
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
- [`update-sso-provider`](update-sso-provider.md)
- [`remove-sso-provider`](remove-sso-provider.md)
- [`register-sso-provider`](register-sso-provider.md)

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#set-sso-security-events-address)
