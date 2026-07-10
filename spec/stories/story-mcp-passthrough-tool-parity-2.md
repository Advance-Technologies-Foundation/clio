# Story 2: Shared c1 dependencies — settings-based `ICaptionCultureResolver` + `IApplicationInfoService` overloads

**Feature**: mcp-passthrough-tool-parity
**FR coverage**: FR-01 (enabling dependency), FR-05, FR-10
**PRD**: [prd-mcp-passthrough-tool-parity.md](../prd/prd-mcp-passthrough-tool-parity.md)
**ADR**: [adr-mcp-passthrough-tool-parity.md](../adr/adr-mcp-passthrough-tool-parity.md)
**Status**: ready-for-dev
**Size**: M (half day)

---

## As a

developer implementing the class-c1 Application tool fixes (stories 3-9 in this feature)

## I want

a settings-based overload of `ICaptionCultureResolver.Resolve` and of
`IApplicationInfoService.GetApplicationInfo` / `FindApplicationId` that never touches
`ISettingsRepository.GetEnvironment` / `FindEnvironment`

## So that

every c1 tool's **nested** culture-resolution and app-info lookups — not just their outermost call — can be
routed against the header tenant, closing the leak the ADR's verification #4 found one level deep inside
`create-app` / `create-app-section` / `update-app-section`

---

## Acceptance Criteria

- [ ] **AC-01** — Given `ICaptionCultureResolver.Resolve(EnvironmentSettings settings, string
  overrideCulture)` is called, when it executes, then it resolves culture **without** calling
  `ISettingsRepository.GetEnvironment` or `FindEnvironment` at all (ADR "Key interfaces / contracts";
  closes the nested twin of Security mode ii found in `CaptionCultureResolver.cs:54`).
- [ ] **AC-02** — Given `IApplicationInfoService.GetApplicationInfo(EnvironmentSettings environmentSettings,
  string? id, string? code)` is called, when it executes, then it resolves the application info against the
  supplied settings object directly, without a name-based `ISettingsRepository` lookup.
- [ ] **AC-03** — Given `IApplicationInfoService.FindApplicationId(EnvironmentSettings environmentSettings,
  string code)` is called, when it executes, then it behaves identically to the existing name-based overload
  except it never calls `ISettingsRepository.FindEnvironment`.
- [ ] **AC-04** — **Observable parity for the existing name-based overloads.** Given
  `Resolve(EnvironmentOptions, string)`, `GetApplicationInfo(string, string?, string?)`, and
  `FindApplicationId(string, string)` are called from CLI/stdio callers, when they run, then their behavior
  is observably unchanged: same results for the same inputs, same factory/repository call sequence
  (verifiable via NSubstitute `Received` checks), and **all existing named tests for these members pass
  without modification** (PRD AC-09 / SM-03 — this story adds overloads, it does not modify existing
  signatures or bodies).
- [ ] **AC-ERR** — Given a settings-based overload is called with a **null** `EnvironmentSettings` argument,
  when it executes, then it throws `ArgumentNullException` from a guard clause **before** any factory
  invocation (`IApplicationClientFactory.CreateEnvironmentClient` etc. is never entered with null settings)
  — covered by a named unit test per overload.

## Implementation Notes

This is the shared-dependency slice the task explicitly calls out as landing first — stories 3-9 depend on
it. Add exactly the two new overload pairs below (ADR "Key interfaces / contracts", "OQ-01 — c1"):

```csharp
public interface ICaptionCultureResolver {
    string Resolve(EnvironmentOptions options, string overrideCulture);      // unchanged
    string Resolve(EnvironmentSettings settings, string overrideCulture);    // NEW — no repository call
}
public interface IApplicationInfoService {
    ApplicationInfoResult GetApplicationInfo(string environmentName, string? id, string? code);              // unchanged
    ApplicationInfoResult GetApplicationInfo(EnvironmentSettings environmentSettings, string? id, string? code); // NEW
    InstalledAppSummary FindApplicationId(string environmentName, string code);                                // unchanged
    InstalledAppSummary FindApplicationId(EnvironmentSettings environmentSettings, string code);                // NEW
}
```

**Scope note:** this story owns ONLY these two shared interfaces. The per-tool service overloads
(`IApplicationListService`, `IApplicationCreateService`, the section services) belong to their own stories
(3-9, per ADR slices 6c-6g) — do not add them here.

**The enforceable invariant (ADR, stated explicitly so it survives implementation):** a settings-based
overload may call another settings-based overload or a factory that takes `EnvironmentSettings` directly
(`IApplicationClientFactory.CreateEnvironmentClient`, `IServiceUrlBuilderFactory.Create`,
`ICurrentUserCultureResolverFactory.Create`) — it may **never** call a name-based overload or
`ISettingsRepository.FindEnvironment`/`GetEnvironment` directly. Verify this per file during review, not
from the interface shape alone (ADR Pre-Implementation Checklist).

Key files: `clio/Command/EntitySchemaDesigner/CaptionCultureResolver.cs`,
`clio/Command/ApplicationInfoService.cs` (both paths verified against the current tree).
Pattern to follow: existing per-environment factories already accept `EnvironmentSettings` directly
(`CurrentUserCultureResolverFactory.Create`, `PlatformVersionResolverFactory.Create`) — mirror their
signature shape, not a new abstraction.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | `Resolve(EnvironmentSettings, ...)` never calls `ISettingsRepository` (NSubstitute `Received(0)`); null-settings `ArgumentNullException` before factory invocation; existing `Resolve(EnvironmentOptions, ...)` unchanged | `clio.tests/Command/EntitySchemaDesigner/CaptionCultureResolverTests.cs` |
| Unit `[Category("Unit")]` | `GetApplicationInfo(EnvironmentSettings, ...)` / `FindApplicationId(EnvironmentSettings, ...)` never call `ISettingsRepository`; null-settings `ArgumentNullException` before factory invocation; existing name-based overloads unchanged | `clio.tests/Command/ApplicationInfoServiceTests.cs` |
| Integration `[Category("Integration")]` | none required — pure logic overloads, no I/O beyond the existing factory calls already covered elsewhere | — |
| E2E `[Category("E2E")]` | none at this slice — covered indirectly by Story 15's e2e cases for the consuming tools | — |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [ ] All `CLIO*` diagnostics clean in changed files — including CLIO005 (FR-10)
- [ ] Targeted tests green before commit: `dotnet test clio.tests/clio.tests.csproj --filter
  "Category=Unit&Module=Command" --no-build` (changed files live under `clio/Command/`) (ADR slice 9)
- [ ] All new CLI flags are kebab-case (no new CLI flags in this story)
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] PR description references this story file

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
