# get-sso-security-events

## Command Type

    Identity commands

## Name

get-sso-security-events - read the security events state of an SSO provider

## Description

Reads the security events state of one SSO provider through GetProviderState. It changes nothing and also answers while the site feature IdentitySecurityEvents is off (reason FeatureOff, featureEnabled false), so it is the check before and after every other command of the family.

> **The settings service.** Every command of this family posts to one method of the site's settings service SsoSecurityEventsSettingsService (rest/SsoSecurityEventsSettingsService/<Method>, with the 0/ prefix on .NET Framework). The service comes with the SsoSecurityEvents package of Creatio 10.2 and later and needs the CanManageSso system operation. It answers every call with a reason code and never with a failure: the command prints the reason ("none" on success) and exits 0 whatever it is. It exits 1 only when the call itself failed: a refused option, no answer, an answer that is not the service's, or a site without the service (HTTP 404, reported as not supported). Read the reason before acting on the result.

> **Platform routing.** The request goes to `0/rest/SsoSecurityEventsSettingsService/GetProviderState` on .NET Framework hosts and to `rest/SsoSecurityEventsSettingsService/GetProviderState` on .NET Core hosts; make sure the environment's runtime flag (`--IsNetCore`) matches the site.

## Synopsis

```bash
get-sso-security-events --code <code> [options]
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
# Print the reason and the events state of Creatio ID
clio get-sso-security-events -e demo-site --code identity-v3

# Print the site's whole answer
clio get-sso-security-events -e demo-site --code identity-v3 --format json
```

## Output

Text output is one "name: value" line per member the site returned: reason, reasonDetail, oauthError, providerId, isCreated, testResult, streamDeleted, then the state the call left (providerExists, featureEnabled, enabled, siteAddress, siteStatus, streamStatus, lastError, lastTestResult, lastTestOn, changedByOperation, changedOn, isDisablePending). --format json prints the site's whole answer. No answer carries a secret. Text from the site is printed on one line, with anything that looks like a credential removed.

    0       The site answered; read the printed reason code
    1       The call failed: refused option, no or foreign answer, service not found (404)

## Reason codes

Every command of the family answers with one of these codes in `reason`.

| Reason | Meaning |
|---|---|
| (empty, printed as none) | Success |
| `AccessDenied` | The user lacks the CanManageSso operation; nothing was read or changed |
| `FeatureOff` | The site feature IdentitySecurityEvents is off |
| `ProviderNotFound` | No provider has the code |
| `ProviderInvalid` | The provider was refused (update or remove); the detail says why |
| `NotSupported` | The provider does not support security events (detail NoConfiguration, NoPushDelivery, NoConfigurationEndpoint) |
| `NotAuthorized` | The provider refused the site's client (oauth error given) |
| `OtherHostNotConfirmed` | The SSF URL is on another host; confirm it with --confirm-other-host |
| `Unavailable` | The provider or the site could not answer (detail Busy, StreamNotDeleted, or an error type) |
| `StreamNotCreated` | The switch is on but no stream exists (detail: the site status) |
| `SiteAddressRefused` | The provider refuses the events address (detail: its error code) |
| `ForeignAddress` | The stream delivers to another address (a copied site); nothing was changed |
| `PausedByOperator` | The provider's operator paused the stream |
| `StreamNotEnabled` | The stream is not enabled, so no test event can be sent |
| `RateLimited` | Test events were sent too often |
| `TestNotReceived` | The test event did not arrive within the wait |
| `SiteAddressRequired` | No site address is stored and the call's own host is local or private |
| `SiteAddressInvalid` | The site address is not an absolute public https address |
| `SsfUrlInvalid` | The SSF discovery URL is not valid |

## Notes

- A provider registered with `register-sso-provider` has no stream record: `enabled` is false and no stream exists at the provider until `enable-sso-security-events` or an administrator turns events on.
- The call is a read: it may be retried and replayed after a re-login.

## Prerequisites

- A Creatio 10.2+ site with the SsoSecurityEvents package, registered with clio (`clio reg-web-app ...`)
- The authenticated user has the `CanManageSso` system operation right

## Related commands

- [`enable-sso-security-events`](enable-sso-security-events.md)
- [`disable-sso-security-events`](disable-sso-security-events.md)
- [`send-sso-security-test-event`](send-sso-security-test-event.md)
- [`set-sso-security-events-address`](set-sso-security-events-address.md)
- [`update-sso-provider`](update-sso-provider.md)
- [`remove-sso-provider`](remove-sso-provider.md)
- [`register-sso-provider`](register-sso-provider.md)

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#get-sso-security-events)
