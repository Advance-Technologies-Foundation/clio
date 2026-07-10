# Story 16: FR-06/OQ-04 guard — audited classification registry + discovery-completeness + exact (tool, dependency-path, scenario)→test mapping

**Feature**: mcp-passthrough-tool-parity
**FR coverage**: FR-06, FR-07, FR-10
**PRD**: [prd-mcp-passthrough-tool-parity.md](../prd/prd-mcp-passthrough-tool-parity.md)
**ADR**: [adr-mcp-passthrough-tool-parity.md](../adr/adr-mcp-passthrough-tool-parity.md)
**Status**: ready-for-dev
**Size**: L (full day)

---

## As a

QA engineer / future contributor to `clio/Command/McpServer/**`

## I want

a committed, path-based guard that (a) proves every discovered `[McpServerToolType]` tool is classified, and
(b) proves every routed/guarded tool has a **named, existing** test method for each required
**(tool, dependency-path, scenario)** tuple — not merely "a test exists somewhere", and not merely per tool

## So that

a newly added tool or a silently reintroduced header-blind path fails the guard immediately instead of
shipping unnoticed (PRD FR-06 / SM-02; the exact defect class this whole feature exists to prevent)

---

## Acceptance Criteria

- [ ] **AC-01** (PRD AC-08, guard core) — Given the guard, when a new or edited tool reaches Creatio on a
  request path without going through `IToolCommandResolver`, then the guard fails — while legitimate local
  `ISettingsRepository` use (e.g. `list-creatio-builds`) does **not** trip it.
- [ ] **AC-02** — **Completeness (discovery-drift guard).** Given
  `McpFeatureToggleFilter.GetAttributedTypes(assembly, typeof(McpServerToolTypeAttribute))` enumerates every
  `[McpServerToolType]` tool, when the completeness test runs, then the result set is asserted **exactly**
  equal to `PassthroughToolClassificationRegistry.Classification.Keys` — a newly added tool with no row fails
  this test immediately.
- [ ] **AC-03** — **Per-(tool, dependency-path, scenario) mapping presence (ADR OQ-04, path granularity).**
  Given every `Classification` entry that is not `NotEnvironmentSensitive`, when the mapping-presence test
  runs, then `Coverage` contains a row for each of its audited **dependency paths** × expected scenarios —
  e.g. `update-page` has SEPARATE rows for its `write` path and its `version-probe` path; `build-theme` for
  its `version` path; `create-app-section` for `outer`, `caption-culture-readback`,
  `caption-culture-validation`, `app-info-validation`, `app-info-polling`; each `link-from-repository-*`
  branch (`by-environment`, `env-package-path-preparation`, `unlocked`) is its own path. Routed/guarded
  paths require `HeaderOnly` + `MixedInput` + `RegisteredEnvStdio` rows (fail-fast-only paths:
  `HeaderOnly` maps to the guard-rejection test, `RegisteredEnvStdio` to the unchanged-behavior test) —
  **and**, via reflection over `FixtureType`, `FixtureType.GetMethod(MethodName)` returns a non-null
  `MethodInfo` carrying `[Test]` or `[TestCase]`. A missing path row, missing scenario row, or a row naming
  a nonexistent/non-test method fails the guard.
- [ ] **AC-04** — **Not merely "a test exists somewhere."** Given a fixture with an unrelated `[Test]` method
  but no row naming the exact required (tool, path, scenario) method, when the mapping-presence test runs,
  then it fails — proving the coarse "any test in fixture X" design rejected in the ADR (`OQ-04`
  alternatives table) does not silently pass here.
- [ ] **AC-05** — Given the PRD's audit (out-of-scope tools: telemetry, `get-guidance`, `get-tool-contract`,
  infra assertions, `list-creatio-builds`, etc.), when the registry is populated and the completeness test
  runs, then each is present with a single `NotApplicable`/`NotEnvironmentSensitive` row and does **not**
  trip the guard.
- [ ] **AC-06** — Given every tool touched in Stories 1-14 (7 c1 tools, `get-user-culture`, the 3
  `link-from-repository-*` tools, `update-page`/`sync-pages`/`get-component-info`/`build-theme`), when the
  registry is populated, then each has `Coverage` rows for **every one of its audited dependency paths and
  required scenarios**, pointing at the actual unit-test methods written in stories 1-14 and the E2E methods
  recorded by Story 15's Dev Agent Record.
- [ ] **AC-ERR** — Given a registry row naming a method that does not exist on `FixtureType`, when the
  mapping-presence test runs, then it fails with a clear assertion message identifying the
  tool/path/scenario — not a silent pass or an unrelated reflection exception.

## Implementation Notes

The registry entry carries a **stable dependency-path identifier** — this is what lifts the mapping from
per-tool to per-path granularity (ADR OQ-04 requires exact (tool, dependency-path, scenario) tuples):

```csharp
internal enum PassthroughScenario { HeaderOnly, MixedInput, RegisteredEnvStdio, NotApplicable }

internal sealed record PassthroughCoverageEntry(
    string ToolName,
    string DependencyPath,     // stable id, e.g. "outer", "version-probe", "write",
                               // "caption-culture-readback", "app-info-polling", "preparation", "unlocked-query"
    PassthroughScenario Scenario,
    Type FixtureType,
    string MethodName);        // exact method name via nameof(...)

internal static class PassthroughToolClassificationRegistry {
    internal static readonly IReadOnlyDictionary<string, PassthroughClassification> Classification = ...; // one row per discovered tool
    internal static readonly IReadOnlyList<PassthroughCoverageEntry> Coverage = [
        new("list-apps", "outer", PassthroughScenario.HeaderOnly, typeof(ApplicationGetListToolPassthroughTests),
            nameof(ApplicationGetListToolPassthroughTests.ApplicationGetList_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly)),
        new("update-page", "version-probe", PassthroughScenario.HeaderOnly, typeof(PageUpdateToolTests),
            nameof(PageUpdateToolTests.ResolvePlatformVersionAsync_ShouldResolveHeaderTenant_WhenHeaderOnly)),
        // ... one row per (tool, path, scenario) from Stories 1-15; one NotApplicable row per out-of-scope tool.
    ];
}
```

The `DependencyPath` vocabulary is fixed and documented in the registry file header (the audited paths come
from the ADR's decision matrix and verification #4/#6: outer, version-probe, write,
caption-culture-readback, caption-culture-validation, app-info-validation, app-info-polling,
find-application-id, preparation, unlocked-query, culture-read).

Two tests only (ADR "OQ-04 — corrected"): (1) completeness (discovery vs. registry keys), (2) per-path/
per-scenario mapping presence (reflection-verified method existence + `[Test]`/`[TestCase]` attribute). Do
**not** build a Roslyn analyzer for this — the ADR chose the allowlist+mapping design over the analyzer
alternative (OQ-04 alternatives table).

This story runs **after** Stories 1-15 (it needs the final tool set and the exact unit + E2E test method
names those stories produced — Story 15's Dev Agent Record carries the E2E method list) and **before**
Story 17 (docs review benefits from the now-stable, fully classified tool list).

Key files: new `clio/Command/McpServer/PassthroughToolClassificationRegistry.cs` (or equivalent namespace —
verify current `McpServer` layout before creating), reusing
`McpFeatureToggleFilter.GetAttributedTypes` (already exists, per ADR "Revision history").
Pattern to follow: `McpFeatureToggleFilter.cs:89`'s existing attribute-scan helper — do not reimplement tool
discovery.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Completeness (discovered tool set == registry keys); per-(tool, path, scenario) mapping presence (every required tuple has a named, existing, `[Test]`-attributed method); coarse-fixture rejection (AC-04); out-of-scope tools present as `NotApplicable` and don't trip the guard | `clio.tests/Command/McpServer/PassthroughToolClassificationGuardTests.cs` |
| Integration `[Category("Integration")]` | none required | — |
| E2E `[Category("E2E")]` | none — this guard is a unit-level, compile/reflection-time invariant; the E2E methods it references were authored in Story 15 | — |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [ ] All `CLIO*` diagnostics clean in changed files — including CLIO005 (FR-10)
- [ ] Targeted tests green before commit: `dotnet test clio.tests/clio.tests.csproj --filter
  "Category=Unit&Module=McpServer" --no-build` (ADR slice 9)
- [ ] All new CLI flags are kebab-case (no new CLI flags in this story)
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] Registry lists every `[McpServerToolType]` tool name discovered in the current tree, not just the ones
      touched by this feature — and every audited dependency path for the touched ones
- [ ] PR description references this story file

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
