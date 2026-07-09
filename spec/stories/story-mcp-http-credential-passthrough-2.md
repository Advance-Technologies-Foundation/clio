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
- [ ] **Q1** — Construct `new CreatioClient(appUrl, bearerToken, isNetCore)` (the public ctor confirmed to exist in `~/.nuget/packages/creatio.client/1.0.38/lib/netstandard2.0/Creatio.Client.dll`). Does an authenticated **write (POST)** actually attach `Authorization: Bearer <token>` on the outbound request?
- [ ] **Q2** — Does that ctor **suppress any auto-`Login()`** on the first call (it may be gated on an internal "useOAuth" flag)? Confirm no login round-trip occurs. *ADR "could-not-verify": the ctor exists but its outbound POST behavior was not decompiled/run — this spike must prove it.*
- [ ] **Q3** — Wrapping the pre-authenticated `CreatioClient` via the public `CreatioClientAdapter(CreatioClient)` ctor (`clio/Common/CreatioClientAdapter.cs:55-59`) + a `NoReauthExecutor` (never calls `Login()`), does an existing tool's write path work end-to-end against a live env?

### Cookie leg (AT-RISK / CONDITIONAL scope — determine and possibly drop)
- [ ] **Q4** — Is there **any supported public path** to inject an externally supplied cookie into `CreatioClient`? Reflection found **only non-public** `InitAuthCookie(int)` and `AddCsrfToken(HttpWebRequest|HttpClient)`; there are **no** public cookie/token setters on `CreatioClient` (the `set_AccessToken`/`set_CookieContainer`-style setters live on the DTOs `Dto.TokenResponse`/`NegotiateResponse`, not on the client). Confirm this on the inspected surface.
- [ ] **Q5** — If a supported path exists: does it attach `BPMCSRF` on POST? If **no supported path exists**, record the verdict: **DROP the cookie leg from v1** (Alternative A — a from-scratch `TokenCreatioClient` — is reconsidered only if the cookie leg is later required).

### Fallback
- [ ] **Q6** — If the **bearer** spike (Q1/Q2) also fails: record the degradation to `{url, login, password}`-only for v1 (still valuable, still nothing-persisted) and re-plan token/cookie.

## Deliverables

- [ ] A findings note (ADR OQ-01 section or `spec/mcp-http-credential-passthrough/token-cookie-client-spike-findings.md`) answering Q1–Q6 with evidence (a captured outbound POST showing the auth header, and a live authenticated write, or decompiled ctor behavior).
- [ ] **Bearer leg verdict**: GO (build the factory branch in Story 3) or NO-GO (degrade to login/password).
- [ ] **Cookie leg verdict** — explicitly one of: (a) supported injection path found → conditional GO with the exact API/contract; or (b) **no supported path → DROPPED from v1** (recorded as an at-risk scope item that did not ship, not a committed deliverable).
- [ ] Update OQ-01 / A-01 status in the PRD to RESOLVED with the outcome; update the ADR OQ-01 verdict if the empirical result differs from the reflection-based prediction.

## Implementation Notes

- Grounding (from ADR OQ-01 verdict): bearer ctor `CreatioClient(string appUrl, string bearerToken, bool isNetCore)`; adapter ctor `CreatioClientAdapter(CreatioClient)` (`clio/Common/CreatioClientAdapter.cs:55-59`); `ApplicationClientFactory` (`clio/Common/ApplicationClientFactory.cs:6-24`) has **no** token/cookie branch today; adapter defaults `IReauthExecutor` to `Login()` → opaque material needs `NoReauthExecutor`.
- All Creatio HTTP must go through `IApplicationClient`/`CreatioClient` — never raw `HttpClient`.
- Do not build the production `ApplicationClientFactory` branch here (that is Story 3); this spike only proves the mechanism. Any kept scaffold must be CLIO001/CLIO005-clean and DI-registered.

## Definition of Done

- [ ] Q1–Q6 answered with evidence from the real assembly / a live env
- [ ] Bearer-leg GO/NO-GO recorded; cookie-leg CONFIRMED or **explicitly DROPPED from v1**
- [ ] If bearer fails, the `{url, login, password}`-only degradation is recorded and downstream scope re-planned
- [ ] OQ-01 / A-01 marked resolved in the PRD; ADR verdict reconciled with the empirical result
- [ ] No production client-seam code merged from this spike beyond a verified scaffold

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
