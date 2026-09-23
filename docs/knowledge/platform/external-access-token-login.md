---
description: OAuthTokenLogin accepts only a support external-access token; it reports refusals inside HTTP 200 and is served from the site root on both hosts
applies-to:
  - clio/Common/ExternalAccess/
  - clio/Common/ApplicationClientFactory.cs
  - clio/BindingsModule.cs
ticket: none
date: 2026-09-17
---

**What is true** — `<site>/ServiceModel/AuthService.svc/OAuthTokenLogin` is the only endpoint that
accepts a support external-access token, and three of its properties are not visible in the code
that calls it:

1. It is served from the **site root on both hosts**, the same prefix exception already recorded for
   `AuthService.svc/Login` in `auth-service-login-is-served-at-the-site-root.md` — which is why this
   route is deliberately absent from `ServiceUrlBuilder.KnownRoutes` too.
2. A **refusal is reported inside HTTP 200**, as `{"Code": <non-zero>, "Message": "..."}`. Verified
   against a live .NET Framework site on 2026-09-17: a junk token answered `200` with
   `{"Code":1,"Message":"IDX12741: JWT: ... must have three segments"}`.
3. The token is valid for **120 seconds**. Measured on 2026-09-17 on three consecutively minted
   tokens: `exp - iat == 120` every time, while the grant itself (`prop:ExpirationDate`) ran for
   another five days. The grant window and the token window are unrelated.
4. The token is **not an API bearer token**. `OAuthAuthorizationHelper` on the target site requires
   an `OAuthClientApp` row matching the token's client id, which an external-access token does not
   have, so sending it as `Authorization: Bearer` on ordinary requests fails on every call.

`ExternalAccessValidator.ValidateExternalAccessToken` additionally requires the claims
`prop:SysAdminUnitId` and `prop:ResourceId` plus an active, unexpired `ExternalAccess` row on the
target site granted by that admin unit. This is what the `browser-session-handoff` Story-11 spike
found when it rejected clio's own client-credentials token.

**Why it is this way** — the endpoint exists for the support access-list flow: the grantor site
mints a token for one grant, and the target site exchanges it for a session. It was never an API
authentication path.

The token also carries the grant's terms as claims: `prop:IsDataIsolationEnabled`,
`prop:IsSystemOperationsRestricted`, `prop:ExpirationDate`, `prop:OwnerClientId` and
`prop:ResourceId` (the access id). A caller that holds the token can read them without asking the
grantor site again.

**What breaks if you ignore it** — each property fails silently in its own way. Reading only the
HTTP status code treats an expired or deactivated grant as a **successful login**, and the failure
then surfaces on an unrelated later request. Reusing clio's existing `AccessToken` branch produces a
client that authenticates against nothing and fails on every call instead of at connect time.
