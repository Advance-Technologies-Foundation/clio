# Story 1: Culture Resolver Service + Singleton Cache

**Feature**: user-profile-language-detection
**FR coverage**: FR-01, FR-02, FR-10
**PRD**: [prd-user-profile-language-detection.md](../prd/prd-user-profile-language-detection.md)
**ADR**: [adr-user-profile-language-detection.md](../adr/adr-user-profile-language-detection.md)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: none
**Blocks**: story-user-profile-language-detection-2, -3, -4a, -4b, -5, -6

---

## As a

developer / CI pipeline author

## I want

clio to resolve and validate the connected Creatio user's profile culture server-side from `GetApplicationInfo`, with a singleton per-environment cache

## So that

caption culture is deterministic, independent of the host machine locale, and resolved at most once per environment per session without redundant round-trips

---

## Acceptance Criteria

- [ ] **AC-01** — Given a connected environment whose logged-in user profile culture is `uk-UA`, when `ResolveAsync` runs, then the resolver reads `applicationInfo.sysValues.userCulture.displayValue` from `ApplicationInfoService.svc/GetApplicationInfo` via `IApplicationClient` (no cliogate, no raw `HttpClient`) and returns `CultureResolution.Resolved("uk-UA")`.
- [ ] **AC-05** — Given an environment URI, when `ResolveAsync` is invoked twice within the TTL window via two `factory.Create(settings)` calls sharing the singleton cache, then the `GetApplicationInfo` round-trip happens at most once (sequential calls) and the second call is served from the cache.
- [ ] **SM-01** — Given any supported environment (.NET Framework or .NET Core, cliogate absent), when resolution succeeds, then a non-empty validated `cultureName` is returned via the clio-native path; zero new direct `HttpClient` usages are introduced.
- [ ] **AC-B1-INVALID** — Given `userCulture.displayValue` is malformed (unparseable by `CultureInfo.GetCultureInfo`), when `ResolveAsync` runs, then it returns `CultureResolution.Failed("userCulture-invalid")` and never throws into the creation path.
- [ ] **AC-B1-MISSING** — Given `userCulture.displayValue` is empty/whitespace/missing, when `ResolveAsync` runs, then it returns `CultureResolution.Failed("userCulture-missing")`.
- [ ] **AC-Mi1** — Given `userCulture` is absent but `primaryCulture` is present, when `ResolveAsync` runs, then it returns `Failed("userCulture-missing")` and MUST NOT substitute `primaryCulture` (system culture).
- [ ] **AC-ERR** — Given the endpoint is unreachable or unauthorized, when `ResolveAsync` runs, then it returns `CultureResolution.Failed(...)` (reason `unreachable` / `unauthorized`) rather than throwing.

## Implementation Notes

Pattern to follow: `PlatformVersionResolver` / `PlatformVersionResolverFactory` (precedent for the environment-bound factory + `Task.Run`-offloaded sync `IApplicationClient` call). See ADR Decision 1 (Option B chosen), Decision 3 (cache), Decision 0 (validation).

Files to create:
- `clio/Command/EntitySchemaDesigner/CultureResolution.cs` — DTO `record`. INVARIANT (NEW-6): consumers MUST branch on `Success` before reading `Culture`. On failure `Culture` is set to the `en-US` fallback so the M-4 non-fatal path can use it directly.
  ```csharp
  public sealed record CultureResolution(string Culture, bool Success, string? FailureReason)
  {
      public static CultureResolution Resolved(string culture) => new(culture, true, null);
      public static CultureResolution Failed(string reason) =>
          new(EntitySchemaDesignerSupport.DefaultCultureName, false, reason);
  }
  ```
- `clio/Command/EntitySchemaDesigner/CurrentUserCultureCache.cs` — `ICurrentUserCultureCache` (singleton): `ConcurrentDictionary<string, CacheEntry>` keyed by `EnvironmentSettings.Uri`; `TimeProvider`-driven TTL (default 5 min). Interface: `bool TryGet(string environmentUri, out CultureResolution resolution)` / `void Set(string environmentUri, CultureResolution resolution)`.
- `clio/Command/EntitySchemaDesigner/CurrentUserCultureResolver.cs` — `ICurrentUserCultureResolver.ResolveAsync(CancellationToken)`. Reads `sysValues.userCulture.displayValue` from `GetApplicationInfo`; offload the synchronous `IApplicationClient.ExecutePostRequest` via `Task.Run(..., cancellationToken)` (mirror `PlatformVersionResolver.TryGetCoreVersionFromApplicationInfoAsync`). Validate (Decision 0):
  ```csharp
  if (string.IsNullOrWhiteSpace(raw)) return CultureResolution.Failed("userCulture-missing");
  try { var c = CultureInfo.GetCultureInfo(raw.Trim()); return CultureResolution.Resolved(c.Name); }
  catch (CultureNotFoundException) { return CultureResolution.Failed("userCulture-invalid"); }
  ```
  Read culture objects' `displayValue` (the BCP-47 code), NEVER `primaryLanguage` (human label) or `primaryCulture` (system culture). Read cache first (keyed by `settings.Uri`); on miss probe, validate, store, return.
- `clio/Command/EntitySchemaDesigner/CurrentUserCultureResolverFactory.cs` — `ICurrentUserCultureResolverFactory.Create(EnvironmentSettings)`. Mirror `PlatformVersionResolverFactory` (uses `IApplicationClientFactory.CreateEnvironmentClient` + `IServiceUrlBuilderFactory`) but injects the shared singleton `ICurrentUserCultureCache` + DI `TimeProvider`.

URL: `ServiceUrlBuilder.KnownRoute.GetApplicationInfo` / `CreatioServicePaths.GetApplicationInfo`.

Concurrency (NEW-3): check-then-probe-then-`Set` is not atomic; duplicate concurrent probe is tolerated (idempotent, first-write-wins). Do NOT add locking that breaks the precedent's benign race.

Cache key (NEW-5): `EnvironmentSettings.Uri` only; same-URI-different-user is out of scope.

DI (`clio/BindingsModule.cs`): register `ICurrentUserCultureCache` as **singleton** (cross-call cache, M-5); register `ICurrentUserCultureResolverFactory` as singleton (like `IPlatformVersionResolverFactory`, ~L292). Use the DI-registered `TimeProvider` (`BindingsModule.cs` ~L268 → `TimeProvider.System`).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | parse + validate `displayValue`; malformed → `Failed("userCulture-invalid")`; missing/empty → `Failed("userCulture-missing")`; `userCulture` absent + `primaryCulture` present → `Failed` (Mi-1); cache hit via shared singleton across two `Create()` calls (M-5); TTL expiry with `FakeTimeProvider` (Mi-6); unreachable/unauthorized → `Failed`; `Resolved` returns normalized `CultureInfo.Name` | `clio.tests/Command/EntitySchemaDesigner/CurrentUserCultureResolverTests.cs` |

NSubstitute for `IApplicationClient`/`IApplicationClientFactory`/`IServiceUrlBuilderFactory`; `FakeTimeProvider` for deterministic TTL. AAA + a `because` on every assertion + `[Description]` on every test.
Test naming: `MethodName_ShouldBehavior_WhenCondition` (e.g. `ResolveAsync_ShouldReturnFailedUserCultureInvalid_WhenDisplayValueIsMalformed`).
Concurrency: do NOT assert a strict single-probe count under concurrency (NEW-3) — only sequential at-most-once.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004)
- [ ] Resolver contract is `Task<CultureResolution> ResolveAsync(CancellationToken)` (M-6); no sync overload on the interface
- [ ] `displayValue` validated via `CultureInfo.GetCultureInfo`; malformed → `Failed` (B-1)
- [ ] `ICurrentUserCultureCache` registered as singleton, env-URI-keyed (M-5)
- [ ] No raw `HttpClient`; `IApplicationClient` only (SM-01 counter); no cliogate dependency (FR-02)
- [ ] No MediatR — constructor-injected services only
- [ ] `CultureResolution` documents the `Success`-first invariant (NEW-6)
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-06-09
- Implementation completed: 2026-06-09
- Tests passing: 12/12 in `clio.tests/Command/CurrentUserCultureResolverTests.cs` (`Category=Unit`, `Module=Command`). Full `Category=Unit` suite: 3437 passed / 3 failed / 34 skipped — the 3 failures (`ListCreatioBuilds_Should_List_Builds_Newest_First`, `ListCreatioBuilds_Should_Report_NoBuilds_When_Folder_Empty`, `CreateUiProject_Should_Switch_Working_Directory_For_The_Command`) are pre-existing macOS path-normalization failures, confirmed failing identically with this story's changes stashed (unrelated to culture/DI).
- Files created: `clio/Command/EntitySchemaDesigner/CultureResolution.cs`, `CurrentUserCultureCache.cs`, `CurrentUserCultureResolver.cs`, `CurrentUserCultureResolverFactory.cs`; tests `clio.tests/Command/CurrentUserCultureResolverTests.cs`. Modified: `clio/BindingsModule.cs` (singleton cache + factory registration).
- Notes:
  - Test file placed at `clio.tests/Command/CurrentUserCultureResolverTests.cs` (flat, `[Property("Module","Command")]`) to match the actual layout of the other EntitySchemaDesigner tests, not the ADR's assumed `clio.tests/Command/EntitySchemaDesigner/` subfolder.
  - Failed resolutions are intentionally NOT cached (only successes), so a transient unreachable/unauthorized failure is retried on the next call rather than pinned for the TTL — locked by `ResolveAsync_ShouldReprobe_WhenPreviousResolutionFailed`.
  - `clio` builds with 0 Roslyn/CLIO warnings.
