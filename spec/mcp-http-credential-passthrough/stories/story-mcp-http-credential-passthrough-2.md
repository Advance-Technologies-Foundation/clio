# Story 2: [SPIKE] Token/cookie client feasibility (OQ-01 / bearer vs cookie leg)

**Feature**: mcp-http-credential-passthrough
**FR coverage**: FR-01 (auth material shapes), FR-18 (no-reauth for opaque material), resolves OQ-01 / A-01
**PRD**: [prd-mcp-http-credential-passthrough.md](../prd/prd-mcp-http-credential-passthrough.md)
**ADR**: [adr-mcp-http-credential-passthrough.md](../adr/adr-mcp-http-credential-passthrough.md) (OQ-01 verdict; Implementation Plan step 2)
**Jira**: [ENG-93208](https://creatio.atlassian.net/browse/ENG-93208)
**Status**: ready-for-dev
**Size**: S (spike — timebox 1 day)
**Depends on**: —
**Blocks**: ALL downstream code stories (3–15). This spike is BLOCKING — no downstream story may start until it resolves.

---

## As a

architect / senior developer

## I want

to prove empirically, against the actual `Creatio.Client` assembly, whether clio can authenticate an outbound POST from a **pre-obtained bearer token** (and, separately, from a **pre-obtained cookie**) with **no** `Login()` round-trip

## So that

the ADR's "reuse `CreatioClient(appUrl, bearerToken, isNetCore)` ctor + `CreatioClientAdapter` + `NoReauthExecutor`" decision is confirmed for the bearer leg, and the cookie leg is either confirmed feasible or **explicitly dropped from v1** before any client-seam code is written

---

## Spike Questions (answer all)

### Bearer leg (committed scope — must confirm)
- [x] **Q1** — Construct `new CreatioClient(appUrl, bearerToken, isNetCore)` (the public ctor confirmed to exist in `~/.nuget/packages/creatio.client/1.0.38/lib/netstandard2.0/Creatio.Client.dll`). Does an authenticated **write (POST)** actually attach `Authorization: Bearer <token>` on the outbound request? **YES (body-verified)** — every write verb (`ExecutePost/Delete/Patch`, uploads, `CallConfigurationService`) attaches `Authorization: Bearer <token>` via the `if (_oauthToken != null)` branch. See findings note Q1.
- [x] **Q2** — Does that ctor **suppress any auto-`Login()`** on the first call (it may be gated on an internal "useOAuth" flag)? Confirm no login round-trip occurs. **YES (body-verified)** — flag is `_oauthToken`; `InitAuthCookie` short-circuits on a non-empty token and the bearer branch never touches cookie/CSRF/`AuthCookie`, so no `Login()`/ping. ADR "could-not-verify" discharged. See findings note Q2.
- [~] **Q3** — Wrapping the pre-authenticated `CreatioClient` via the public `CreatioClientAdapter(CreatioClient)` ctor (`clio/Common/CreatioClientAdapter.cs:55-59`) + a `NoReauthExecutor` (never calls `Login()`), does an existing tool's write path work end-to-end against a live env? **DEFERRED to Story 3** — mechanism proven statically at the `CreatioClient` layer; not constructible end-to-end today (no live env AND `NoReauthExecutor` does not exist / `IReauthExecutor` internal / public adapter ctor defaults to a `Login()`-based reauth wrong for opaque material). See findings note Q3.

### Cookie leg (AT-RISK / CONDITIONAL scope — determine and possibly drop)
- [x] **Q4** — Is there **any supported public path** to inject an externally supplied cookie into `CreatioClient`? **NO (body-verified)** — `_authCookie` private, `AuthCookie` internal getter-only, `InitAuthCookie`/`AddCsrfToken` private; no ctor accepts a `CookieContainer`; the `set_AccessToken`/`CookieContainer` setters live on DTOs, not the client. Confirmed on the inspected surface. See findings note Q4.
- [x] **Q5** — If a supported path exists: does it attach `BPMCSRF` on POST? **No supported path exists → verdict: DROP the cookie leg from v1** (Alternative A — from-scratch `TokenCreatioClient` — reconsidered only if the cookie leg is later required). See findings note Q5.

### Fallback
- [x] **Q6** — If the **bearer** spike (Q1/Q2) also fails: record the degradation to `{url, login, password}`-only for v1 and re-plan token/cookie. **NOT TRIGGERED** — bearer leg is GO, so degradation is not exercised. See findings note Q6.

## Deliverables

- [x] A findings note answering Q1–Q6 with evidence (decompiled ctor + write-path bodies): [`spec/mcp-http-credential-passthrough/token-cookie-client-spike-findings.md`](../mcp-http-credential-passthrough/token-cookie-client-spike-findings.md). Evidence is decompiled method bodies (ilspycmd on the real 1.0.38 assembly); live-env capture deferred to Story 3.
- [x] **Bearer leg verdict**: **GO** — build the factory branch in Story 3.
- [x] **Cookie leg verdict**: **(b) no supported path → DROPPED from v1** (recorded as an at-risk scope item that did not ship, not a committed deliverable).
- [~] Update OQ-01 / A-01 status in the PRD to RESOLVED with the outcome. **Recommended status text is in the findings note ("Recommended OQ-01 / A-01 status text" section) for the orchestrator to apply — not self-edited here.** Empirical result matches the reflection-based ADR prediction; no reconciliation delta.

## Implementation Notes

- Grounding (from ADR OQ-01 verdict): bearer ctor `CreatioClient(string appUrl, string bearerToken, bool isNetCore)`; adapter ctor `CreatioClientAdapter(CreatioClient)` (`clio/Common/CreatioClientAdapter.cs:55-59`); `ApplicationClientFactory` (`clio/Common/ApplicationClientFactory.cs:6-24`) has **no** token/cookie branch today; adapter defaults `IReauthExecutor` to `Login()` → opaque material needs `NoReauthExecutor`.
- All Creatio HTTP must go through `IApplicationClient`/`CreatioClient` — never raw `HttpClient`.
- Do not build the production `ApplicationClientFactory` branch here (that is Story 3); this spike only proves the mechanism. Any kept scaffold must be CLIO001/CLIO005-clean and DI-registered.

## Definition of Done

- [x] Q1–Q6 answered with evidence from the real assembly (Q3 live-env leg deferred to Story 3, mechanism proven statically)
- [x] Bearer-leg **GO** recorded; cookie-leg **explicitly DROPPED from v1**
- [x] Bearer did not fail → degradation N/A (recorded as not-triggered)
- [~] OQ-01 / A-01 resolution recorded in findings note with recommended PRD/ADR text; PRD/ADR edit left to orchestrator (empirical result matches ADR prediction — no reconciliation delta)
- [x] No production client-seam code written from this spike (investigation only)

## Dev Agent Record

- Implementation started: 2026-07-09
- Implementation completed: 2026-07-09
- Tests passing: N/A — investigation spike, no code written
- Notes:
  - Decompiled `Creatio.Client` 1.0.38 (`~/.nuget/packages/creatio.client/1.0.38/lib/netstandard2.0/Creatio.Client.dll`; version confirmed in `Directory.Packages.props:36`) with `ilspycmd` 10.1.0 — **full method bodies**, not signatures. All bearer/cookie answers are body-verified.
  - **Bearer GO (body-verified):** public ctor `CreatioClient(appUrl, bearerToken, isNetCore)` sets `_oauthToken = StripBearerPrefix(bearerToken)`; every write verb attaches `Authorization: Bearer <token>` under `if (_oauthToken != null)`; `InitAuthCookie` (only `Login()` caller) short-circuits on a non-empty token and the bearer branch never touches cookie/CSRF → no login/ping round-trip.
  - **Cookie DROPPED (body-verified):** no public/internal cookie-injection API — `_authCookie` private, `AuthCookie` internal getter-only, `InitAuthCookie`/`AddCsrfToken` private, no ctor takes a `CookieContainer`.
  - **Q3 blind spot for Story 3:** end-to-end adapter path not constructible today — `NoReauthExecutor` does not exist, `IReauthExecutor` is internal, and the public `CreatioClientAdapter(CreatioClient)` ctor (`clio/Common/CreatioClientAdapter.cs:55`) defaults to `new ReauthExecutor(() => _lazyClient.Value.Login())` (line 37), which is wrong for opaque bearer material (no credentials). Story 3 must add `NoReauthExecutor` + a supported injection path and reject null/blank tokens at the factory boundary.
  - Findings note: `spec/mcp-http-credential-passthrough/token-cookie-client-spike-findings.md`.
