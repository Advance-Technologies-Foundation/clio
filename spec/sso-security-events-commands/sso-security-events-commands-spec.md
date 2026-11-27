# SSO security events commands: spec

Jira: ENG-106080 "Every site connected to Creatio ID is protected by security events from day one" (parent
APD-15031). The site side is the SsoSecurityEvents package of Creatio 10.2 (ENG-104753, ENG-104252, ENG-105959):
`SsoSecurityEventsSettingsService`, one service for Step 3 of the provider card, for onboarding and for clio.

## Goal

Support and Creatio's own operations (onboarding, trials, rename, copy, secret rotation, site removal) get one clio
command per operation of the site's settings service, so each operation can turn security events on, test them,
move them, and remove a provider without touching another site's stream. `register-sso-provider` stays as it is and
never turns events on.

## Decision

Seven CLI commands, thin over the service, one per method the operations need. No MCP tools. All seven are
internal operator commands: they are hidden behind the clio feature flag `sso-security-events` (off by default,
`clio experimental --name sso-security-events --enable`) and require Creatio 10.2 (`[RequiresCreatioVersion]`
on `SsoSecurityEventsOptions`), as the maintainer asked in review of PR #1977.

| Command | Method | Changes the site |
| --- | --- | --- |
| `get-sso-security-events` | `GetProviderState` | no |
| `enable-sso-security-events` | `EnableEvents` | yes |
| `disable-sso-security-events` | `DisableEvents` | yes |
| `send-sso-security-test-event` | `SendTestEvent` | yes |
| `set-sso-security-events-address` | `SetSiteAddress` | yes |
| `update-sso-provider` | `UpsertProvider` | yes |
| `remove-sso-provider` | `RemoveProvider` | yes |

Not exposed: `GetStatus`, `Check`, `Save`, `Retry` (the card's own calls, keyed by the card's record id) and
`GetLeftovers`, `DismissLeftover` (the SSO settings banner). No operation needs them.

Rejected:
- MCP tools for the seven: these are operations for Support and automation that move a client secret and can switch
  protection off for a whole site; an agent has no use case that the card and the CLI do not cover, and the tools
  would need e2e coverage on a stand with the package.
- One verb with an action argument: clio has one verb per action, and the operations contract names one command
  per operation.
- A flag on `register-sso-provider` that turns events on: registration posts to core `api/SsoProvider/Register`,
  which is create-only and knows nothing of events; a flag there would turn events on for customers' own
  registrations by habit.

## Platform facts the design rests on

- The service answers every call with HTTP 200 and `SecurityEventSettingsResult` `{reason, reasonDetail,
  oauthError, status, check, providerId, isCreated, testResult, streamDeleted, leftovers}`; `reason` is empty on
  success. A failure of the site itself is `reason` `Unavailable` with the exception type, still 200. The commands
  therefore exit 0 on every reason code and print it; they exit 1 only when the call itself failed.
- `providerCode` is the key of every method the commands use; Creatio ID is always `identity-v3`. Every method
  checks the `CanManageSso` operation first (`AccessDenied`, nothing read or changed).
- `GetProviderState`, `UpsertProvider` and `RemoveProvider` work with the site feature `IdentitySecurityEvents`
  off; the others answer `FeatureOff`.
- `UpsertProvider` sets only the values it is given (`SetIfGiven`), requires `clientId` every time and a
  `clientSecret` for a new provider, and stores Creatio ID under `identity-v3` whatever `providerCode` it is sent.
  `update-sso-provider` therefore requires `--oidc-client-id` and refuses `--creatio-id` with another code.
- `SendTestEvent` waits on the server for up to `waitSeconds` (at most 60) and polls every 2 s after its own request,
  so the request timeout is raised to the wait plus 30 s.
- `RemoveProvider` with `localOnly` calls nothing at the provider: on a copied site the stream belongs to the
  source. `DisableEvents` on such a copy would disable the source's stream.

## Behaviour common to the seven

- Request: `POST <site>[/0]/rest/SsoSecurityEventsSettingsService/<Method>`, routes registered in
  `ServiceUrlBuilder.KnownRoute` 131-137; a JSON body with named members, a member without a value left out so the
  service uses its default.
- A call that changes the site is sent once (`ExecuteNonReplayablePostRequest`, one attempt): a timed-out turn-off,
  test event or remove is never repeated behind the caller's back. `get-sso-security-events` keeps the command's
  ordinary retries.
- Refused before any call: the flag `sso-security-events` off (the parser does not know the verb; exit 1), or a site
  below Creatio 10.2, or a site whose version cannot be read (exit 78, `version-too-old` and the other version-gate
  codes). A development build of Creatio (core version 0.0.0.x) is not checked, as for the theming commands
  (`AllowDevelopmentBuild` keeps its default): no security property rests on the version, the site checks
  `CanManageSso` itself, and a build without the package still answers 404.
- Exit 1 with a message: a blank `--code` or another refused option (nothing sent); HTTP 404 (a 10.2 site without
  the package, or the wrong runtime flag: "not supported"); an empty answer; an answer that is not a
  JSON object with a `reason` member (a login page, a platform error).
- Output: text prints `reason: <code|none>` and one line per returned field; `--format json` prints the answer.
  No answer carries a secret; the secret `update-sso-provider` sends is never printed.
- The client secret of `update-sso-provider` comes from `--oidc-client-secret-file` or `--oidc-client-secret` only;
  unlike `register-sso-provider` it never reads `CLIO_OIDC_CLIENT_SECRET`, because no secret means "keep the stored
  one" and a variable left in the shell would replace another provider's secret.

## Acceptance (from ENG-106080)

- Criterion 10: `register-sso-provider` is unchanged and turns no events on.
- Criterion 11: the seven commands; each prints the site's reason code and exits non-zero only when the call itself
  failed.
- Criterion 13 (clio part): `remove-sso-provider --local-only` on a copy, then `enable-sso-security-events` with the
  copy's own address once it has its own client.

## Tests

- `clio.tests/Command/SsoSecurityEventsCommandsTests.cs`: request body and route per command, replay and attempts,
  reason printed with exit 0, 404 on both transports, foreign answers, refused options, secret never printed.
- `clio.tests/Common/SsoSecurityEventsRouteTests.cs`: the seven routes on .NET Framework and .NET Core.
- `clio.tests/Command/SsoSecurityEventsGateTests.cs`: each of the seven refused with the flag off and allowed with
  it on; refused with `version-too-old` on a 10.1 site and allowed on 10.2; `register-sso-provider` stays ungated.
- Stand: a 10.2 .NET Framework site with the package; a real stream needs a public https site Identity reaches.
