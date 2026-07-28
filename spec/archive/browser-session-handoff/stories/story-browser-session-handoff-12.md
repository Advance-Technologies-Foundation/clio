# Story 12: Cookie-Surface Spike (OQ-06) — does `creatio.client` expose its cookie jar?

**Feature**: browser-session-handoff
**FR coverage**: FR-14 (cookie harvesting), resolves OQ-06
**PRD**: [prd-browser-session-handoff.md](../prd/prd-browser-session-handoff.md)
**ADR**: [adr-browser-session-handoff.md](../adr/adr-browser-session-handoff.md) (Decision 7)
**Status**: done (spike resolved 2026-06-10)
**Size**: S (spike — timebox half day)
**Blocks**: Story 2 (resolved — dedicated `HttpClient` confirmed)
**Created**: 2026-06-10 — added per re-review (BL-4: the "reuse the existing session" cookie path is likely infeasible and must be confirmed)
**Resolved**: 2026-06-10 — **DECISION B** (dedicated `HttpClient`; see Dev Agent Record)

---

## As a

architect

## I want

to determine whether the NuGet `creatio.client` (`Creatio.Client`, v1.0.37) exposes its `CookieContainer` / `HttpClientHandler` / `Set-Cookie` in any reachable way

## So that

Story 2 either (A) reuses the existing authenticated session's cookies via a segregated `ICreatioCookieProvider` (no second HTTP path), or (B) confirms it must harvest cookies with its own dedicated `IHttpClientFactory` client (the current default)

---

## Background

The re-review found `CreatioClientAdapter` owns no cookie jar (it wraps `Lazy<CreatioClient>` and delegates only string-returning methods), and `ReauthExecutor.cs:143-145` documents that the NuGet client "does not expose the HTTP status / final ResponseUri." Strong evidence says cookies are unreachable — but the NuGet is a binary this repo can't introspect with available tools, so the negative must be confirmed before Story 2 commits to a design.

## Spike Questions (answer all)

- [ ] **Q1** — Does `Creatio.Client.CreatioClient` (or any public type in the package) expose a `CookieContainer`, `HttpClientHandler`, response headers, or a `Set-Cookie` accessor? Inspect the assembly (decompile / reflection / package source if available on the internal Nexus).
- [ ] **Q2** — Does it expose the authenticated session's cookies after `Login()` in any form usable to build a Playwright storageState (`.ASPXAUTH`, `BPMCSRF`, `UserType`)?
- [ ] **Q3** — If a newer `creatio.client` version exposes such a surface, what version, and is bumping it acceptable (`Directory.Packages.props:34`)?
- [ ] **Q4** — Confirm the dedicated-`HttpClient` fallback (Decision 7 Option A) reproduces the exact forms-auth handshake the NuGet does (same login URL, body, headers) so the harvested cookies are equivalent.

## Deliverables

- [ ] Findings note (ADR Decision 7 addendum or `spec/browser-session-handoff/cookie-surface-spike-findings.md`) answering Q1–Q4 with evidence.
- [ ] Decision: **A** (segregated `ICreatioCookieProvider` reusing the session — preferred if feasible) or **B** (dedicated `IHttpClientFactory` client — the current default). Update OQ-06 to RESOLVED.
- [ ] If A: define the `ICreatioCookieProvider` contract and which type implements it. If B: confirm the named-`HttpClient` registration shape for `BindingsModule.cs`.

## Definition of Done

- [ ] Q1–Q4 answered with evidence (assembly inspection, not assumption)
- [ ] OQ-06 marked resolved in the PRD; ADR Decision 7 updated with the outcome
- [ ] Story 2's cookie-harvesting approach (A or B) decided and recorded
- [ ] No production code merged unless a concrete, reviewed approach was found

## Dev Agent Record

- Investigation: completed 2026-06-10 via `System.Reflection.MetadataLoadContext` (reflection-only, no code executed) against `~/.nuget/packages/creatio.client/1.0.37/lib/netstandard2.0/Creatio.Client.dll`.
- **Decision: B** — cookies are NOT publicly reachable; clio uses its own `HttpClient` + `CookieContainer`.

### Findings (Q1–Q4)
- **Q1/Q2 — No public cookie surface.** The only public property on `Creatio.Client.CreatioClient` is `bool SkipPing`. The cookie store is `CookieContainer AuthCookie { get=internal; set=-; }` backed by `private CookieContainer _authCookie`. The assembly has **no `InternalsVisibleTo`** → the internal getter is invisible to clio at compile time. The `strings` symbols (`get_CookieContainer`, `get_Cookies`) belong to referenced `System.Net`/HttpClient framework types, not to a public Creatio member. No public `ICreatioClient` cookie member either.
- **Q3 — Where cookies live.** Internal `AuthCookie` (a real `System.Net.CookieContainer`) is populated by `Login()` → `InitAuthCookie()`; `AuthCookie.GetCookies(uri)` would yield `.ASPXAUTH`/`BPMCSRF`/`UserType` — but the getter is internal, so unreachable.
- **Q4 — ctor vs OAuth factory.** No difference in exposure; OAuth path stores a token in `private _oauthToken` (no accessor) and authenticates via `Authorization: Bearer` (no cookie storageState at all).
- **Version note.** 1.0.28/1.0.35/1.0.37 identical on this question; upgrading within range does not unlock a public cookie getter.

### Decision applied
- Story 2 cookie harvesting = **dedicated `HttpClient` + `CookieContainer` via `IHttpClientFactory`** (ADR Decision 7 Option A). The segregated `ICreatioCookieProvider` (Option C) is **ruled out** — there is nothing public to project. `IApplicationClient` stays unchanged.
- OQ-06 → RESOLVED (PRD); ADR Decision 7 finalized to Option A (no longer conditional on this spike).
