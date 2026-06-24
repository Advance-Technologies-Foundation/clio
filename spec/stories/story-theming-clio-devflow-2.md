# Story 2: ClioGate ClearThemesCache endpoint + cliogate bump

**Feature**: theming-clio-devflow (ENG-90636 — Theming with AI, Clio dev flow, Contour A)
**Capability coverage**: CAP-01 (backend body — the surgical theme-cache eviction)
**SPEC**: [spec-theming-clio-devflow.md](../prd/spec-theming-clio-devflow.md)
**ADR**: [adr-theming-clio-devflow.md](../adr/adr-theming-clio-devflow.md)
**Status**: review — resolved via R2 resolution (a); no ClioGate method / cliogate bump required (live-stand E2E pending, manual)
**Size**: M (collapsed to route-wiring once the native endpoint was confirmed)

---

## As a

clio operator hitting an environment whose theme cache must be invalidated

## I want

a ClioGate `ClearThemesCache` endpoint that evicts **only** the platform theme cache (not the whole Redis DB)

## So that

a theme pushed or edited beforehand becomes visible after a Creatio reload, while unrelated cached state survives (SPEC constraint C2)

---

## Resolution (R2 resolved 2026-06-18) — resolution (a)

The platform already exposes a native, purpose-built endpoint, so **no ClioGate work is needed**. The Creatio `ThemeService` web service (`IThemeService` in `Terrasoft.Core.ServiceModelContract.Theme.Interfaces`) defines `POST ThemeService.svc/ClearThemesCache` → `BaseResponse { Success, ErrorInfo }`, documented as *"Forces a refresh of the theme catalog so that the next GetAvailableThemes call observes themes added/modified outside this service."* It requires the `CanCustomizeBranding` license + `CanManageThemes` system operation (runtime auth — **not** a clio package gate).

**What was actually done (instead of the ClioGate body):**
- `KnownRoute.ClearThemesCache = 42` → `ServiceModel/ThemeService.svc/ClearThemesCache` in `clio/Common/ServiceUrlBuilder.cs` (native ThemeService route, NOT `/rest/CreatioApiGateway/...`).
- `ClearThemesCacheCommand.ProceedResponse` parses the `BaseResponse` (case-insensitive `success`; surfaces `errorInfo.message` on `success=false`; tolerates an empty body as success but treats a non-empty non-JSON body as a failure).
- **No** `cliogate/Files/cs/CreatioApiGateway.cs` change, **no** cliogate version bump, **no** `[RequiresPackage("cliogate", …)]` on `ClearThemesCacheOptions`.
- The R1 full-`ClearRedisDb` fallback was **not needed**.

The acceptance criteria below were written for the ClioGate path and are superseded by this resolution; effective acceptance is "the command POSTs to `ThemeService.svc/ClearThemesCache` and reports success/failure from `BaseResponse`," covered by the unit tests in `clio.tests/Command/ClearThemesCacheCommand.Tests.cs`.

---

## Acceptance Criteria

- [ ] **AC-01** — Given a deployed cliogate at the bumped version, when `POST /rest/CreatioApiGateway/ClearThemesCache` is called by a user who can manage solutions, then the theme cache is evicted and a theme change made beforehand is visible after a Creatio reload.
- [ ] **AC-02** — Given the same call, when it runs, then it does **NOT** flush the entire Redis DB (a key unrelated to themes set before the call still resolves afterward) — unless the explicit R1 fallback path is taken, in which case a "full flush" warning is logged.
- [ ] **AC-03** — Given the endpoint method, when inspected, then its **first line** is `CheckCanManageSolution()` and the route is served under `/rest/CreatioApiGateway/ClearThemesCache` (the `[WebInvoke] UriTemplate="ClearThemesCache"` form).
- [ ] **AC-04** — Given an unauthenticated / unprivileged caller, when the endpoint is invoked, then `CheckCanManageSolution()` rejects it before any cache mutation.
- [ ] **AC-05** — Given the cliogate package version was bumped, when `clio/cliogate/cliogate.gz` is rebuilt, then it carries the new version and that version is the one pinned by Story 1's `[RequiresPackage("cliogate", "<new-version>")]`.
- [ ] **AC-ERR** — Given the eviction mechanism is unavailable, when the endpoint runs, then it either takes the logged R1 fallback or returns a user-friendly failure (no raw stack trace; no bare `catch (Exception)`).

## Implementation Notes

This story carries the **OPEN DEPENDENCY**. Do not invent a cache key — confirm via R2 first.

Key file (modify, resolution b): `cliogate/Files/cs/CreatioApiGateway.cs`
```csharp
[WebInvoke(Method = "POST", UriTemplate = "ClearThemesCache",
    BodyStyle = WebMessageBodyStyle.Wrapped,
    RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
public Stream ClearThemesCache()
{
    CheckCanManageSolution(); // MUST be first line
    // theme-only eviction via platform caching API (key/group from R2)
}
```

Eviction idiom (resolution b): mirror `cliogate/Files/cs/Feature/Feature.cs` —
`UserConnection.SessionCache.WithLocalCaching().SetOrRemoveValue(key, null)`, `Terrasoft.Core.Store`.
Grep for `CheckCanManageSolution()` near the target method first — it must be present and first (AGENTS.md ClioGate security rule).

cliogate bump + rebuild (AGENTS.md "Building ClioGate"):
1. `set-pkg-version ./cliogate --PackageVersion X.Y.Z.W` (bump from current).
2. `compress ./cliogate -d ./clio/cliogate/cliogate.gz` (rebuild the embedded gz).
3. The bumped version is what Story 1's `[RequiresPackage("cliogate", "<new-version>")]` pins.

If resolution (a) is chosen instead, skip the ClioGate method: re-point `KnownRoute.ClearThemesCache` (Story 1) at the existing platform route and document that no new endpoint was needed. If R1 fallback is taken, delegate to the existing `ClearRedisDb` path and log the "full flush" warning.

Pattern to follow: ClioGate `CallGate` invocation in `clio/Package/PackageUnlocker.cs`; existing `[WebInvoke]` methods in `CreatioApiGateway.cs` for the contract shape.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | command POSTs to `ServiceModel/ThemeService.svc/ClearThemesCache` and maps `BaseResponse` (`success` / `success=false`+`errorInfo` / empty→success / non-JSON→failure) — supersedes the obsolete `[RequiresPackage]`-drift check | `clio.tests/Command/ClearThemesCacheCommand.Tests.cs` |
| Integration `[Category("Integration")]` | gz artifact carries the bumped version (descriptor read) — only if a deterministic FS check is feasible | `clio.tests/...Tests.cs` |
| E2E `[Category("E2E")]` (manual, NOT in CI) | deploy bumped cliogate to a live stand, push a theme change, call `clear-themes-cache`, verify visible-after-reload AND non-theme Redis state survives (AC-01/AC-02) | manual stand verification (record evidence in Dev Agent Record) |

- ClioGate C# (in `cliogate/`) is verified on a live stand; the `CheckCanManageSolution()`-first invariant is the key code-review gate.
- Test naming: `MethodName_ShouldBehavior_WhenCondition`. AAA + `because` + `[Description]`.

## Definition of Done

- [ ] R2 resolved with platform team (UC/Kuvarzin) OR R1 fallback explicitly chosen and logged
- [ ] If resolution (b): ClioGate `ClearThemesCache` method exists, **first line is `CheckCanManageSolution()`**, evicts theme cache only
- [ ] If resolution (a): no ClioGate method added; `KnownRoute` re-pointed at the existing platform route and documented
- [ ] cliogate version bumped; `clio/cliogate/cliogate.gz` rebuilt; version matches Story 1's `[RequiresPackage]` pin
- [ ] No bare `catch (Exception)`; user-friendly error messages
- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO005)
- [ ] Manual live-stand E2E recorded (AC-01/AC-02 verified or fallback path documented) — MCP/stand E2E NOT in CI
- [ ] PR description references this story file and notes the R2 resolution path taken

## Dev Agent Record

- R2 resolution path (a / b / fallback): **(a)** — native `ThemeService.svc/ClearThemesCache`.
- Implementation: route + response parsing landed with Story 1 (`ServiceUrlBuilder.cs`, `ClearThemesCacheCommand.cs`). No ClioGate change.
- cliogate version bumped to: **n/a** (no cliogate change; cliogate stays at 2.0.0.43).
- Live-stand E2E evidence: pending manual run against a branding-licensed Creatio stand (deploy a theme, `push`, `clear-themes-cache`, verify visible-after-reload + non-theme cache survives).
- Notes: `[RequiresPackage("cliogate", …)]` was intentionally NOT added — the endpoint is a native platform service gated by `CanCustomizeBranding` + `CanManageThemes` at runtime.
