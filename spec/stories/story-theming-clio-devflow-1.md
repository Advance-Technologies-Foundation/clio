# Story 1: clear-themes-cache route + CLI command

**Feature**: theming-clio-devflow (ENG-90636 — Theming with AI, Clio dev flow, Contour A)
**Capability coverage**: CAP-01 (CLI surface + transport wiring; backend body is Story 2)
**SPEC**: [spec-theming-clio-devflow.md](../prd/spec-theming-clio-devflow.md)
**ADR**: [adr-theming-clio-devflow.md](../adr/adr-theming-clio-devflow.md)
**Status**: review
**Size**: M (half day)

> **R2 resolved (2026-06-18) — native endpoint, no ClioGate.** This story was originally drafted for
> the ClioGate path; resolution (a) superseded it. The route is the native
> `ServiceModel/ThemeService.svc/ClearThemesCache` (NOT `/rest/CreatioApiGateway/...`); there is **no**
> `[RequiresPackage("cliogate", …)]` (the endpoint is gated at runtime by the `CanCustomizeBranding`
> license + `CanManageThemes` system operation), and no cliogate bump. The AC, notes, and DoD below are
> corrected to match the shipped code.

---

## As a

developer driving a Creatio environment through clio

## I want

a `clear-themes-cache` CLI verb that POSTs to the native `ServiceModel/ThemeService.svc/ClearThemesCache` endpoint on the target environment

## So that

I can invalidate the theme cache surgically from the command line instead of flushing the entire Redis DB with `clear-redis-db`

---

## Acceptance Criteria

- [ ] **AC-01** — Given `ServiceUrlBuilder` with a new `KnownRoute.ClearThemesCache = 42`, when `Build(KnownRoute.ClearThemesCache)` is called for a .NET Core environment, then it returns `ServiceModel/ThemeService.svc/ClearThemesCache`.
- [ ] **AC-02** — Given a .NET Framework environment (`IsNetCore = false`), when `Build(KnownRoute.ClearThemesCache)` is called, then the result is prefixed with `0/` (same idiom as every other `KnownRoute`).
- [ ] **AC-03** — Given `ClearThemesCacheCommand` resolved from DI, when its `ServicePath` is read, then it equals `_urlBuilder.Build(KnownRoute.ClearThemesCache)` (no hard-coded URL string).
- [ ] **AC-04** — Given the options class, when reflected, then the verb is `clear-themes-cache` with alias `flush-themes`, both kebab-case (CLIO001 has no new surface to flag), and it carries **no** `[RequiresPackage]` attribute (the native `ThemeService` endpoint is gated at runtime, not by a clio package).
- [ ] **AC-05** — Given `clio --help` / the parser, when listing verbs, then `clear-themes-cache` is reachable as a real verb (registered in `Program.cs` and DI in `BindingsModule.cs`).
- [ ] **AC-ERR** — Given the `ThemeService` returns `BaseResponse { success: false, errorInfo }` (e.g. the caller lacks `CanManageThemes`), when `clear-themes-cache` runs, then clio surfaces `errorInfo.message` and exits non-zero (no stack trace, no bare `catch (Exception)`).

## Implementation Notes

This story is the **transport + CLI shell** of CAP-01. The cache eviction itself is performed by the
native Creatio `ThemeService` (Story 2, resolved via R2 — no ClioGate body to author); this story wires
the route + command and parses the `BaseResponse` it returns.

Mirror `clear-redis-db` exactly — it is the reference pattern (ADR D2).

Key file (create): `clio/Command/ClearThemesCacheCommand.cs`
```csharp
[Verb("clear-themes-cache", Aliases = ["flush-themes"], HelpText = "Refresh the Creatio theme catalog cache")]
public class ClearThemesCacheOptions : RemoteCommandOptions { }

public class ClearThemesCacheCommand : RemoteCommand<ClearThemesCacheOptions>
{
    private readonly IServiceUrlBuilder _urlBuilder;
    public ClearThemesCacheCommand(IApplicationClient applicationClient, EnvironmentSettings settings, IServiceUrlBuilder urlBuilder)
        : base(applicationClient, settings) => _urlBuilder = urlBuilder;
    protected override string ServicePath => _urlBuilder.Build(ServiceUrlBuilder.KnownRoute.ClearThemesCache);
    // ProceedResponse parses the ThemeService BaseResponse { success, errorInfo } — see the shipped command.
}
```

Key file (modify): `clio/Common/ServiceUrlBuilder.cs`
- Add `ClearThemesCache = 42` to the `KnownRoute` enum (continues after the current highest, `41` — do NOT reuse gaps).
- Add `{ KnownRoute.ClearThemesCache, "ServiceModel/ThemeService.svc/ClearThemesCache" }` to `KnownRoutes` (the native `ThemeService` route resolved via R2 — **not** a `/rest/CreatioApiGateway/...` ClioGate route). `ServiceUrlBuilder.Build` prepends `0/` for `.NET Framework` automatically.

Pattern to follow: `clio/Command/RedisCommand.cs` (verb + `RemoteCommand<TOptions>` + `ServicePath => _urlBuilder.Build(...)`); `KnownRoute.ClearRedisDb = 21` is the route precedent.

Register in:
- `clio/Program.cs` — wire the `clear-themes-cache` verb to its command.
- `clio/BindingsModule.cs` — register `ClearThemesCacheCommand` in DI.

No `[RequiresPackage]` / cliogate coordination is needed: R2 resolved Story 2 to the native `ThemeService` endpoint, so there is no cliogate version to pin. Authorization is enforced at runtime (`CanCustomizeBranding` + `CanManageThemes`).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | command POSTs to `ServiceModel/ThemeService.svc/ClearThemesCache` (Core) and the `0/`-prefixed form (NetFW) | `clio.tests/Command/ClearThemesCacheCommand.Tests.cs` |
| Unit `[Category("Unit")]` | `ServicePath` resolves via `_urlBuilder.Build(...)`; verb/alias kebab-case; **no** `[RequiresPackage]`; `ProceedResponse` maps `success=false` → failure + surfaces `errorInfo.message` | `clio.tests/Command/ClearThemesCacheCommand.Tests.cs` |

- Use `BaseCommandTests<ClearThemesCacheOptions>` as the fixture base; resolve the command from the DI container; register doubles in `AdditionalRegistrations`.
- Do not add `[Category("UnitTests")]`.
- Test naming: `MethodName_ShouldBehavior_WhenCondition` (e.g. `Build_ShouldReturnThemeCacheRoute_WhenEnvironmentIsNetCore`).
- AAA + a `because` on every assertion + `[Description]` on every test.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO005)
- [ ] All new CLI flags / verb / alias are kebab-case
- [ ] `KnownRoute.ClearThemesCache = 42` maps to `ServiceModel/ThemeService.svc/ClearThemesCache` (native endpoint)
- [ ] `ClearThemesCacheCommand` registered in `BindingsModule.cs` and wired in `Program.cs`
- [ ] **No** `[RequiresPackage]` on `ClearThemesCacheOptions` (native endpoint, runtime auth)
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] Targeted tests run: `dotnet test --filter "Category=Unit&(Module=Command|Module=Common)"`; full unit suite also run (BindingsModule.cs / Program.cs / Common/ touched — smart-regression rule 4)
- [ ] PR description references this story file

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
