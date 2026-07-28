# Story 11: `update-page` — header-aware platform-version probe (matrix tool)

**Feature**: mcp-passthrough-tool-parity
**FR coverage**: FR-03, FR-10
**PRD**: [prd-mcp-passthrough-tool-parity.md](../prd/prd-mcp-passthrough-tool-parity.md)
**ADR**: [adr-mcp-passthrough-tool-parity.md](../adr/adr-mcp-passthrough-tool-parity.md)
**Status**: ready-for-dev
**Size**: S (< 2h)

---

## As a

CI pipeline author (AI-Platform gateway operator)

## I want

`update-page`'s (`PageUpdateTool`) platform-version probe to resolve against the header tenant, not the
configured active/registered environment

## So that

the single tool proving one dependency path can honor the header while another is header-blind
(`PageUpdateTool.cs:64` vs `:273`) stops silently probing the wrong tenant

---

## Acceptance Criteria

- [x] **AC-01** (PRD AC-04, decision-matrix "Route") — Given authorized passthrough (header-only or
  header+`environment-name`), when `update-page` runs, then **no** named/active registered-environment
  repository lookup or version probe occurs before header routing or rejection — the platform version is
  either header-derived or a documented non-tenant fallback flag, never a silent non-tenant probe.
  Verified by `UpdatePage_ShouldResolveVersionAgainstHeaderTenant_WhenHeaderOnly` (header-tenant routing) and
  `UpdatePage_ShouldRejectProbeBeforeNamedTenantLookup_WhenMixedHeaderAndEnvironmentName` (rejection, never a
  silent probe) in `PageUpdateToolTests.cs`.
- [x] **AC-02** — Given the same setup, when `ResolvePlatformVersionAsync` (today `:104`) runs, then it calls
  `commandResolver.Resolve<EnvironmentSettings>(...)` instead of `settingsRepository.GetEnvironment(...)`
  (today `:273`) — this tool has **no** blank-name early return before the settings call, so the fix reaches
  the resolver on every input shape, including header-only (ADR "Matrix tools", `update-page` row).
  Confirmed by reading the pre-change method body: no blank-name early return exists (only a
  dependency-presence guard on `resolverFactory`/`settingsRepository`, retained unchanged as a
  dependency-presence gate — see Dev Agent Record).
- [x] **AC-03** — **Mixed input (PRD AC-06).** Given authorized passthrough with both the header and an
  explicit `environment-name` naming a different registered environment, when the version probe runs, then
  it is rejected by `HasExplicitCredentialArgs` before any named-tenant lookup — it never probes the named
  registered environment with stored credentials.
  Verified by `UpdatePage_ShouldRejectProbeBeforeNamedTenantLookup_WhenMixedHeaderAndEnvironmentName`
  (asserts `resolverFactory.DidNotReceiveWithAnyArgs().Create(...)` and fail-soft degrade to `latest`).
- [x] **AC-04** (PRD AC-09 / SM-03) — Given stdio or registered-environment `mcp-http`, when `update-page` is
  called with `environment-name`, then behavior — including the version probe and the already-compliant
  write path (`:64`) — matches the pre-change baseline exactly.
  Verified by `UpdatePage_ShouldResolveVersionAgainstRegisteredEnvironment_WhenEnvironmentNameSupplied`
  (both `Resolve<PageUpdateCommand>` and `Resolve<EnvironmentSettings>` hit the SAME registered environment)
  plus the full pre-existing `PageUpdateToolBaselineTests.cs` regression suite (36 tests, all green after
  updating its `EnvironmentSettings` stub to route through `commandResolver`).
- [x] **AC-ERR** — **Error semantics respect the ENG-93208 middleware boundary (PRD AC-ERR).**
  (a) A **malformed/unusable** header is out of scope: the middleware returns HTTP 400 **before** any tool
  is invoked (`McpHttpServerCommand.cs:317`) — the tool is never entered and must not add handling for it.
  Unaffected by this change; no handling added.
  (b) Given a **valid** header whose probe/write fails, when the tool executes, then the probe fails soft
  (matching the sibling matrix tools' shape) or the write returns the typed error envelope with
  `SensitiveErrorTextRedactor`-redacted text.
  Probe fail-soft path unchanged (`catch (Exception) { return null; }`) and covered by the mixed-input test
  above; the write path's `SensitiveErrorTextRedactor` redaction (`:66`) is untouched by this slice.

## Implementation Notes

Pattern-A, one-line swap; `update-page` already has `IToolCommandResolver` injected (its write path already
uses it), so this slice needs **no new constructor wiring**.

```csharp
// ResolvePlatformVersionAsync — before: settingsRepository.GetEnvironment(...)   (:273)
// after:
EnvironmentSettings settings = commandResolver.Resolve<EnvironmentSettings>(options);
```

Key file: `clio/Command/McpServer/Tools/PageUpdateTool.cs`.
Pattern to follow: the tool's own write path (`:64`) — this is literally the ADR's motivating example of one
tool with one honoring path and one header-blind path; make the version-probe path match the write path's
existing pattern.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Header-only: version probe reaches and resolves against header tenant; mixed-input: probe rejected before any named-tenant lookup; registered-env/stdio unchanged | `PageUpdateToolTests.cs` (extend) |
| Integration `[Category("Integration")]` | none required | — |
| E2E `[Category("E2E")]` | Owned by **Story 15**: two-tenant isolation (newly routed version-probe path) + stdio/`-e` no-regression. Manual only — MCP e2e not in CI | `clio.mcp.e2e/` (Story 15) |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [x] All `CLIO*` diagnostics clean in changed files — including CLIO005 (FR-10)
- [x] Targeted tests green before commit: `dotnet test clio.tests/clio.tests.csproj --filter
  "Category=Unit&Module=McpServer" --no-build` (ADR slice 9)
- [x] All new CLI flags are kebab-case (no new CLI flags in this story)
- [x] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] PR description references this story file (no PR opened by the coder agent — left for the architect)

## Dev Agent Record

- Implementation started: 2026-07-11
- Implementation completed: 2026-07-11
- Tests passing: yes — targeted `Category=Unit&Module=McpServer&FullyQualifiedName~PageUpdateTool` filter
  (covers all 3 PageUpdateTool test classes, including the new one): 39/39 green (36 pre-existing + 3 new).
  Full `Category=Unit&Module=McpServer` module run: transient failures (up to 4, trending down to 1 across
  repeated runs) appeared only in files under concurrent edit by parallel Stories 10/12/13/14
  (GetUserCulture/PageSync/ComponentInfo/BuildTheme) — this worktree is shared live by 5 parallel coder
  agents. `GetUserCultureTool` was re-verified 10/10 green on a targeted re-run once that story's edits
  settled. None of the transient failures were in `PageUpdateTool` scope (39/39 clean throughout). Re-verify
  the full module once all parallel stories land.
- Notes:
  - One-line swap in `PageUpdateTool.ResolvePlatformVersionAsync`: `settingsRepository.GetEnvironment(...)`
    → `commandResolver.Resolve<EnvironmentSettings>(...)`. The `settingsRepository is null` /
    `resolverFactory is null` dependency-presence guard clause was left **unchanged** — it is not a
    blank-name early return (confirmed by reading the pre-change method body), so `settingsRepository`
    stays a harmless, still-read constructor parameter (avoids an unused-primary-constructor-parameter
    warning) even though its `.GetEnvironment(...)` call site is gone.
  - `PageUpdateArgs.EnvironmentName` already carries no `[Required]` attribute — no schema relax needed for
    this tool (unlike `sync-pages`).
  - Updated `PageUpdateToolBaselineTests.cs`'s existing `SetUp` to stub
    `commandResolver.Resolve<EnvironmentSettings>(...)` instead of `settingsRepository.GetEnvironment(...)`,
    since the production swap would otherwise silently break its pre-existing
    `UpdatePage_ShouldScopeChartValidationToResolvedEnvironmentVersion` regression test.
  - New test file `PageUpdateToolTests.cs` created (no such file existed pre-story, despite the ADR test
    table naming it) with the 3 required scenarios (header-only, mixed-input, registered-env/stdio
    unchanged).
