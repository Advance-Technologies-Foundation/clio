# SPEC — external access login (support access list)

Status: proposed
Date: 2026-09-17
Prior art: `spec/archive/browser-session-handoff` (Story-11 spike, 2026-06-10)

## Problem

Support engineers reach a customer site through the **access list** on `work.creatio.com`
(`Actions → Show access list`). Selecting a grant opens the customer site already authenticated —
no login and password are ever typed, and in most support cases no login/password exist for that site
at all. clio cannot connect to such a site today, so every clio-driven diagnosis on a customer stand
is blocked behind credentials the customer never gives out.

## How the platform mechanism works (verified, not inferred)

Sources: live `work.creatio.com` / `4bua.creatio.com` client sources, `creatio-core`, and the
decompiled `creatio.client` 2.0.2 assembly.

1. `Terrasoft.CustomerAccessListMixin` (package `SupportService`) adds the menu item, gated by the
   `CanUseExternalAccess` system operation.
2. `POST /0/rest/TempAccessService/GetTempAccessList` `{customerIds, clientId}` returns the grants:
   `accessId`, `url`, `description`, `expirationDate`, `customerId`, `isDataIsolationEnabled`,
   `isSystemOperationsRestricted`.
3. `POST /0/rest/TempAccessService/GetAccessToken` `{accessId}` returns a JWT. A row is written to
   `ExternalAccessRequestLog`, then the browser opens
   `<customerUrl>/Login/ExternalAccessLogin.aspx#access_token=<jwt>`.
4. `externalAccessLoginModule.js` (platform core) reads the fragment and exchanges the token:

   ```
   POST <base>/ServiceModel/AuthService.svc/OAuthTokenLogin
   Authorization: Bearer <jwt>
   Content-Type: application/json
   <bare integer: time-zone offset in minutes>
   ```

   On success the response is `{"Code":0}` and the session cookies (`.ASPXAUTH`, `BPMCSRF`,
   `BPMLOADER`) are set. Implemented on both hosts:
   `Terrasoft.WebApp.Loader/ServiceModel/AuthService.svc.cs` (NetFW) and
   `Terrasoft.Authentication/Controllers/AuthController.cs` (NetCore), behind
   `Feature-OAuthTokenLogin` (default on).
5. `ExternalAccessValidator.ValidateExternalAccessToken` validates the token against the identity
   service the **customer site** trusts, requires the claims `prop:SysAdminUnitId` and
   `prop:ResourceId`, and requires a matching `ExternalAccess` row on the customer site that is
   active, unexpired, and granted by that admin unit.

## Verified against a live site (2026-09-17)

An unauthenticated probe with a deliberately invalid token against a .NET Framework customer site
answered:

```
POST https://<site>/ServiceModel/AuthService.svc/OAuthTokenLogin   ->  HTTP 200
{"Code":1,"Message":"IDX12741: JWT: 'System.String' must have three segments (JWS) or five segments (JWE)."}
```

This settles two things the code depends on: the route really is served from the site root with no
`0/` prefix, and a refusal arrives **inside** an HTTP 200 as a non-zero `Code` with a `Message`.

## End-to-end run against a real grant (2026-09-17)

Case SR-01545805, grant `62821f5d-8af8-492a-8a82-03b575a94e69` on `https://4bua.creatio.com`
(both restriction flags false), token minted through `TempAccessService` and the
`ExternalAccessRequestLog` row written first.

| Check | Result |
|---|---|
| `get-info` (DI-singleton dispatch) | full instance report, session running as the grantor (`Supervisor`, culture `uk-UA`) |
| `ping` (`ExecuteRemoteCommand` dispatch) | `Done` |
| `get-user-culture` | `uk-UA` |
| `list-packages` | `Find 409 packages` |
| `list-apps` (ATF path) | refused with the named reason, as designed |

**The token is valid for 120 seconds** (`exp - iat`, identical on three consecutive mints), while the
grant itself ran for another five days. That measurement is what changed the caching decision.

## Session caching, verified on the same grant

With the cache in place, one token was minted and then deliberately allowed to expire:

| Run | Time relative to the token's `exp` | Result |
|---|---|---|
| `get-user-culture` | 98 s before | exchanged, session written to `<clio home>/sessions/…_ea_<accessId>.storageState.json`, mode `-rw-------` |
| `get-user-culture` | 31 s **after** | `uk-UA` — cached session reused, the expired token never sent |
| `list-packages` | 31 s after | `Find 409 packages` |
| `get-user-culture` | ~2 min after | reused again, and printed `External access grant 62821f5d-… - no restrictions, valid until 2026-09-22` |

So one token now covers a whole working session instead of a single command.

## Scope

**In scope (clio):** step 4 only — exchange a supplied external-access token for a Creatio session
and run ordinary clio commands on it.

**Out of scope (clio):** steps 1–3. `work.creatio.com` is reachable only to Creatio employees and
only through SSO, while clio ships to partners and customers. Minting belongs to a skill in
`ai-instructions` that drives the browser, writes the `ExternalAccessRequestLog` audit row the
platform writes today, and then hands clio the token.

## Functional requirements

| # | Requirement |
|---|---|
| FR-01 | A new `--external-access-token <jwt>` environment option. When present, clio exchanges it for a session before the first request. |
| FR-02 | The exchange posts to `<base>/ServiceModel/AuthService.svc/OAuthTokenLogin` with `Authorization: Bearer <jwt>` and a bare integer body. The route is **site-root on both hosts** — no `0/` prefix on .NET Framework. |
| FR-03 | The token is never persisted: same secret discipline as `AccessToken` (`[YamlIgnore]`, both `JsonIgnore` attributes), so it never reaches `appsettings.json` or `ShowSettingsTo`. |
| FR-04 | A session obtained this way is **not renewable**: clio wires `NoReauthExecutor`, so an expired session fails with a message naming external access and asking for a fresh token, instead of attempting a forms login with credentials that do not exist. |
| FR-05 | The token is rejected when combined with `AccessToken` (the two are different authentication models and silently picking one would hide a caller mistake). |
| FR-06 | ATF-backed commands fail closed with a named reason: `ATF.Repository.RemoteDataProvider` has no session-cookie constructor, so no external-access data provider can be built. |
| FR-07 | Every client-construction site must honour the same rules — the factory, the `BindingsModule` DI registrations (active environment and MCP child containers), and `Program.ExecuteRemoteCommand`. A silent fall-through to the `Supervisor` default is the failure this requirement exists to prevent. See docs/knowledge/Common/creatio-client-is-built-at-three-sites.md. |
| FR-08 | An external-access failure reaches the user verbatim. The exception carries `IAuthoritativeErrorMessage`, and `get-info`, which otherwise generalizes any unclassified failure into "verify the credentials", surfaces it as-is — an external-access session has no credentials to verify. |

## Decisions

- **The session is cached, the token is not.** Superseded the original "no cache" decision once the
  token lifetime was measured at 120 seconds (see below). One cache entry per (environment, grant),
  owner-only, in the same store and format as the browser-session cache. The objection that killed
  the first version still stands and is answered rather than ignored: a cached cookie can outlive the
  server session, so aliveness is settled by an actual request against the Shell, never by the
  cookie's own expiry. A dead entry is deleted and the token is exchanged again.
- **The grant terms are read from the token.** `prop:IsDataIsolationEnabled`,
  `prop:IsSystemOperationsRestricted`, `prop:ExpirationDate` and `prop:ResourceId` are claims on the
  token itself, so clio names the grant and its restrictions once per run. The payload is read
  without verifying the signature, which is correct: clio is not the party that decides whether the
  token is genuine — the customer site is, and it refuses the exchange otherwise.
- **Not the existing `AccessToken` branch.** That branch sends `Authorization: Bearer` on every
  request, which the customer site's `OAuthAuthorizationHelper` rejects: it requires an
  `OAuthClientApp` row matching the token's `client_id`. The external-access token is only ever
  accepted by `OAuthTokenLogin`.
- **`KnownRoute` is deliberately NOT extended.** `ServiceUrlBuilder.Build` prepends `0/` on .NET
  Framework, which would produce a URL that does not exist. This route is the site-root
  authentication exception AGENTS.md names, exactly like `AuthService.svc/Login`, which
  `creatio.client` also builds from the site root.

## Facts that make the implementation possible

- `CreatioAuthenticationHandler.EnsureAuthenticatedAsync` returns early when `HasSession()` is true,
  and `HasSession()` without `ICredentials` means "the cookie container holds `.ASPXAUTH`". Importing
  the exchanged cookies therefore stops the client from ever attempting a login.
- `.ASPXAUTH` is the auth cookie name on both hosts (`Terrasoft.Web.Common/AuthConsts.cs`,
  `AuthCookieName`).
- The public constructor `new CreatioClient(uri, userName: null, userPassword: null, useUntrustedSsl,
  isNetCore)` yields a client with no credentials, ready for `ImportSessionCookies`.
- `CreatioAuthenticationHandler` takes `BPMCSRF` from the same cookie container, so imported cookies
  also satisfy the CSRF header on every later POST.

## Out of clio's reach — the skill must cover it

- `ExternalAccessRequestLog` — the audit row the platform mixin writes before opening the tab. clio
  has no `work.creatio.com` session and cannot write it; the skill must, before handing over a token.
- `isDataIsolationEnabled` / `isSystemOperationsRestricted` — only the grant list carries them. The
  skill must report which grant it chose and both flags, so a later `push-pkg` failure is
  attributable to a restricted grant rather than read as a clio defect.
