# send-sso-security-test-event

## Command Type

    Identity commands

## Name

send-sso-security-test-event - ask the provider for a security events test event

## Description

Asks the provider for a verification event through SendTestEvent. With --wait the site waits up to that many seconds for the event before it answers (testResult Received, Pending or NotReceived; reason TestNotReceived when it did not arrive in time, with the site address in the state). Without a wait it answers at once with Pending. Needs events on and the stream enabled (StreamNotEnabled, PausedByOperator or ForeignAddress otherwise); RateLimited when tests are sent too often.

> **The settings service.** Every command of this family posts to one method of the site's settings service SsoSecurityEventsSettingsService (rest/SsoSecurityEventsSettingsService/<Method>, with the 0/ prefix on .NET Framework). The service comes with the SsoSecurityEvents package of Creatio 10.2 and later and needs the CanManageSso system operation. It answers every call with a reason code and never with a failure: the command prints the reason ("none" on success) and exits 0 whatever it is. It exits 1 only when the call itself failed: a refused option, no answer, an answer that is not the service's, or a site without the service (HTTP 404, reported as not supported). Read the reason before acting on the result.

> **Platform routing.** The request goes to `0/rest/SsoSecurityEventsSettingsService/SendTestEvent` on .NET Framework hosts and to `rest/SsoSecurityEventsSettingsService/SendTestEvent` on .NET Core hosts; make sure the environment's runtime flag (`--IsNetCore`) matches the site.

## Synopsis

```bash
send-sso-security-test-event --code <code> [--wait <seconds>] [options]
```

## Options

```bash
--code                              Code of the SSO provider (required)
                                    Creatio ID is always identity-v3

--wait                              Seconds the site waits for the test event (0-60)
                                    Default: 0, do not wait

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
# Send a test event and wait up to 60 s for it
clio send-sso-security-test-event -e demo-site --code identity-v3 --wait 60
```

## Output

Text output is one "name: value" line per member the site returned: reason, reasonDetail, oauthError, providerId, isCreated, testResult, streamDeleted, then the state the call left (providerExists, featureEnabled, enabled, siteAddress, siteStatus, streamStatus, lastError, lastTestResult, lastTestOn, changedByOperation, changedOn, isDisablePending). --format json prints the site's whole answer. No answer carries a secret. Text from the site is printed on one line, with anything that looks like a credential removed.

    0       The site answered; read the printed reason code
    1       The call failed: refused option, no or foreign answer, service not found (404)

The reason codes are listed in [get-sso-security-events](get-sso-security-events.md#reason-codes).

## Notes

- With `--wait` the request timeout is raised to the wait plus 30 seconds when `--timeout` is shorter.
- A wait outside 0-60 is refused before anything is sent.
- The call is sent once and never retried or replayed, so one command sends one test event.

## Prerequisites

- A Creatio 10.2+ site with the SsoSecurityEvents package, registered with clio (`clio reg-web-app ...`)
- The authenticated user has the `CanManageSso` system operation right

## Related commands

- [`get-sso-security-events`](get-sso-security-events.md)
- [`enable-sso-security-events`](enable-sso-security-events.md)
- [`disable-sso-security-events`](disable-sso-security-events.md)
- [`set-sso-security-events-address`](set-sso-security-events-address.md)
- [`update-sso-provider`](update-sso-provider.md)
- [`remove-sso-provider`](remove-sso-provider.md)
- [`register-sso-provider`](register-sso-provider.md)

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#send-sso-security-test-event)
