# Story 11: OAuthTokenLogin Spike (OQ-01) — confirm NetFW availability

**Feature**: browser-session-handoff
**FR coverage**: FR-07 (OAuth branch), resolves OQ-01
**PRD**: [prd-browser-session-handoff.md](../prd/prd-browser-session-handoff.md)
**ADR**: [adr-browser-session-handoff.md](../adr/adr-browser-session-handoff.md) (Decision 1)
**Status**: done (spike resolved 2026-06-10)
**Size**: S (spike — timebox 1 day)
**Blocks**: Story 2 (resolved — OAuth token→cookie branch dropped)
**Created**: 2026-06-10 — added per review (OAuth trigger / OQ-01 was unresolved while Story 2 was ready-for-dev)
**Resolved**: 2026-06-10 — **NO-GO** (see Dev Agent Record)

---

## As a

architect

## I want

to confirm whether `AuthController.OAuthTokenLogin` (the access-token → browser-cookie exchange) exists and is reachable on **.NET Framework** Creatio, and at what URL

## So that

Story 2 can implement a real OAuth branch (or be confidently scoped to mock-only) and the OAuth-only-NetFW fail-closed branch is correct

---

## Spike Questions (answer all)

- [ ] **Q1** — Does `AuthController.OAuthTokenLogin` (or an equivalent token→cookie endpoint) exist on NetFW Creatio? If not, what is the closest mechanism?
- [ ] **Q2** — What is the exact request contract on each host (URL incl. any `0/` prefix, HTTP method, headers, body, token type expected) for NetCore vs NetFW?
- [ ] **Q3** — Does the endpoint return the same browser cookies (`.ASPXAUTH`/`BPMCSRF`/`UserType`) as forms-auth, suitable for a Playwright storageState?
- [ ] **Q4** — Can clio's existing OAuth access token (from `ClientId`/`ClientSecret`, the `connect/token` flow implied by `EnvironmentSettings.AuthAppUri`) be exchanged directly, or is a different token type required?
- [ ] **Q5** — If NetFW does **not** support it: confirm the fail-closed decision (OAuth-only-NetFW unsupported, structured error per FR-07(d)) and record it.

## Deliverables

- [ ] A short findings note appended to the ADR Decision 1 (or `spec/browser-session-handoff/oauth-spike-findings.md`) answering Q1–Q5 with evidence (file:line in `~/Projects/creatio-core`, or a captured request/response against a real env).
- [ ] A go/no-go for the real OAuth branch in Story 2: either (a) concrete request contract → Story 2 implements it for real and AC-08 becomes testable, or (b) NetFW unsupported → Story 2 keeps the mock-only OAuth branch and the fail-closed path, and AC-08 is marked NetCore-only.
- [ ] Update OQ-01 status in the PRD to **RESOLVED** with the outcome.

## Implementation Notes

- Grounding references from prior research: `AuthController.OAuthTokenLogin` (`AuthController.cs:402` in creatio-core, NetCore); `BaseAuthController.ExecuteSignInAsync` (NetCore passwordless issuance); `AuthEngine.SetAuthCookies` (NetFW/System.Web passwordless issuance) — both are host-side, so the question is whether a **reachable HTTP endpoint** wraps them on NetFW.
- No production clio code ships from this spike unless Q1/Q2 yield a concrete, safe contract; otherwise the output is the documented decision only.

## Definition of Done

- [ ] Q1–Q5 answered with evidence
- [ ] ADR Decision 1 updated with findings; OQ-01 marked resolved in the PRD
- [ ] Story 2's OAuth branch scope (real vs mock-only) decided and recorded
- [ ] No production code merged unless a concrete, reviewed contract was found

## Dev Agent Record

- Investigation: completed 2026-06-10 against `~/Projects/creatio-core` @ `b929bfb6137` (trunk).
- **Decision: NO-GO** for exchanging clio's OAuth access token for a browser cookie — on **both** hosts.

### Findings (Q1–Q5)
- **Q1 — Does OAuthTokenLogin exist on NetFW? YES.** WCF contract `IAuthService.OAuthTokenLogin` (`Terrasoft.Core.ServiceModelContract/IAuthService.cs:50-53`, `[WebInvoke(Method="POST", UriTemplate="OAuthTokenLogin")]`); NetFW impl `Terrasoft.WebApp.Loader/ServiceModel/AuthService.svc.cs:911` (host project net472, `Terrasoft.WebApp.Loader.csproj:5`). Gated by `Feature-OAuthTokenLogin` (default true, `GlobalAppSettings.cs:2869`).
- **Q2 — URL/contract.** `POST 0/ServiceModel/AuthService.svc/OAuthTokenLogin` (NetFW `0/` alias, consistent with clio `ServiceUrlBuilder`). Token in the **`Authorization: Bearer <token>`** header (`AuthService.svc.cs:919-923`), body `timeZoneOffset`. On success issues `.ASPXAUTH` + `BPMCSRF` via `AuthEngine.SetAuthCookies` (`AuthEngine.cs:587-638`).
- **Q3 — Token TYPE (the blocker).** Validated by `ExternalAccessValidator.ValidateExternalAccessToken` (`Terrasoft.Authentication/Validators/ExternalAccessValidator.cs:78-101`) which **requires `prop:SysAdminUnitId` + `prop:ResourceId` claims AND a matching active row in the `ExternalAccess` DB table** (`:122-168`). clio's standard client-credentials token (`connect/token` via ClientId/ClientSecret) carries **no `prop:ResourceId`** and has no `ExternalAccess` row → `SecurityException`, fails closed. Identical validator on NetCore (`AuthController.cs:419-420`) → same limitation on both hosts.
- **Q4 — Forms-auth `AuthService.svc/Login` on both hosts? YES.** NetFW `AuthService.svc.cs:902` (`0/ServiceModel/AuthService.svc/Login`); NetCore `AuthController.cs:41,346` (`ServiceModel/AuthService.svc/Login`, no `0/`). Universal primary, consistent with clio's IsNetCore-aware URL.
- **Q5 — Fail-closed condition confirmed.** OAuth-only environments authenticated with clio's client-credentials token cannot be exchanged for a browser cookie via `OAuthTokenLogin`; and the OAuth API path itself uses `Authorization: Bearer` (no cookie session at all). So OAuth-only → fail closed on both hosts.

### Decision applied
- Drop the OAuth token→cookie branch from Story 2 entirely (not viable with clio's token type on any host).
- **Forms-auth `AuthService.svc/Login` (Login+Password) is the sole, universal cookie-issuance path** (both hosts; NetFW under `0/`).
- OAuth-only / incomplete-credential environments → fail closed with AC-ERR (FR-07).
- OQ-01 → RESOLVED (PRD); ADR Decision 1 updated.
