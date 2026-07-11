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

- [x] **AC-01** (PRD AC-08, guard core) — Given the guard, when a new or edited tool reaches Creatio on a
  request path without going through `IToolCommandResolver`, then the guard fails — while legitimate local
  `ISettingsRepository` use (e.g. `list-creatio-builds`) does **not** trip it.
  **PARTIAL — see Dev Agent Record "AC-01/AC-08 scope caveat".** `list-creatio-builds` verifiably does NOT
  trip the guard (`OutOfScopeTools_ShouldBeExcludedFromRequiredCoverage_WhenClassified`). The "a new/edited
  tool reaches Creatio outside the resolver ⇒ guard fails" half holds ONLY when the tool is classified
  `Routed`/`Guarded` with incomplete coverage, OR is entirely new/unclassified (completeness test). It does
  **not** hold if a future contributor mis-classifies a genuinely-broken new tool as `NotApplicable` — this
  design (allowlist + mapping, not a Roslyn dataflow analyzer) cannot catch that, by the ADR's own OQ-04
  choice. Marked done because the ADR-chosen mechanism (not the analyzer alternative) is what was
  authorized; the residual gap is documented, not silently glossed over.
- [x] **AC-02** — **Completeness (discovery-drift guard).** Given
  `McpFeatureToggleFilter.GetAttributedTypes(assembly, typeof(McpServerToolTypeAttribute))` enumerates every
  `[McpServerToolType]` tool, when the completeness test runs, then the result set is asserted **exactly**
  equal to `PassthroughToolClassificationRegistry.Classification.Keys` — a newly added tool with no row fails
  this test immediately.
- [x] **AC-03** — **Per-(tool, dependency-path, scenario) mapping presence (ADR OQ-04, path granularity).**
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
  **DONE with two documented scope decisions** (see Dev Agent Record): (1) nested sub-call paths
  (`caption-culture-readback`, `caption-culture-validation`, `app-info-validation`, `app-info-polling`,
  `find-application-id`) require `HeaderOnly` only, not the full 3-scenario matrix — Mixed/RegisteredEnvStdio
  are structurally proven once at the tool's outer/entry path; (2) `update-page`'s `write` path is excluded
  from the required set — it was already compliant BEFORE this feature (no Stories 1-14 test targets it
  specifically). Both are flagged to the architect, not silently assumed.
- [x] **AC-04** — **Not merely "a test exists somewhere."** Given a fixture with an unrelated `[Test]` method
  but no row naming the exact required (tool, path, scenario) method, when the mapping-presence test runs,
  then it fails — proving the coarse "any test in fixture X" design rejected in the ADR (`OQ-04`
  alternatives table) does not silently pass here.
- [x] **AC-05** — Given the PRD's audit (out-of-scope tools: telemetry, `get-guidance`, `get-tool-contract`,
  infra assertions, `list-creatio-builds`, etc.), when the registry is populated and the completeness test
  runs, then each is present with a single `NotApplicable`/`NotEnvironmentSensitive` row and does **not**
  trip the guard.
- [x] **AC-06** — Given every tool touched in Stories 1-14 (7 c1 tools, `get-user-culture`, the 3
  `link-from-repository-*` tools, `update-page`/`sync-pages`/`get-component-info`/`build-theme`), when the
  registry is populated, then each has `Coverage` rows for **every one of its audited dependency paths and
  required scenarios**, pointing at the actual unit-test methods written in stories 1-14 and the E2E methods
  recorded by Story 15's Dev Agent Record. **DONE, subject to the same two AC-03 scope decisions above.**
- [x] **AC-ERR** — Given a registry row naming a method that does not exist on `FixtureType`, when the
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

- [x] All `CLIO*` diagnostics clean in changed files — including CLIO005 (FR-10)
- [x] Targeted tests green before commit: `dotnet test clio.tests/clio.tests.csproj --filter
  "Category=Unit&Module=McpServer" --no-build` (ADR slice 9)
- [x] All new CLI flags are kebab-case (no new CLI flags in this story)
- [x] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [x] Registry lists every `[McpServerToolType]` tool name discovered in the current tree, not just the ones
      touched by this feature — and every audited dependency path for the touched ones
- [ ] PR description references this story file — **not done in this session** (no PR opened; flag for
      whoever opens the PR, same pattern as Story 15)

## Dev Agent Record

- Implementation started: 2026-07-11
- Implementation completed: 2026-07-11
- Tests passing:
  - Build: `dotnet build clio.tests/clio.tests.csproj -c Release -f net10.0` → 0 errors, 0 new `CLIO*`
    diagnostics.
  - Targeted: `dotnet test clio.tests/clio.tests.csproj -c Release -f net10.0 --no-build --filter
    "FullyQualifiedName~PassthroughToolClassificationGuardTests"` → **7/7 passed** (completeness, mapping
    presence against the real registry, the AC-04 coarse-fixture self-test, the AC-ERR nonexistent-method
    self-test, the AC-05 out-of-scope test, the RequiredCoverage/Classification consistency test, and the
    sentinel method the AC-04 self-test references).
  - Module filter (DoD-mandated): `dotnet test clio.tests/clio.tests.csproj -c Release -f net10.0 --no-build
    --filter "Category=Unit&Module=McpServer"` → **2115 passed, 0 failed, 1 skipped** (2116 total). The
    handful of "Verb '...' is not recognized" lines in the console output are captured stderr from
    pre-existing CLI-parsing-error test cases, not failures.
- Notes:
  - **Registry location deviates from the story's suggested path** (`clio/Command/McpServer/*.cs`) —
    placed instead at `clio.tests/Command/McpServer/PassthroughToolClassificationRegistry.cs`. Reason:
    `PassthroughCoverageEntry.FixtureType` must reference NUnit fixture types declared in BOTH `clio.tests`
    (Stories 1-14 unit fixtures) and `clio.mcp.e2e` (Story 15 fixtures). `clio.tests` already
    project-references both `clio` and `clio.mcp.e2e`; the main `clio` assembly cannot reference either
    test project without a circular reference. `McpFixturePolicyTests.cs` (pre-existing, same folder) is
    the established precedent for a cross-test-project reflection guard living in `clio.tests` for exactly
    this reason. The story's own text authorized this ("or equivalent namespace — verify current McpServer
    layout before creating").
  - **Ground truth for discovery, not PRD prose.** Ran a temporary throwaway reflection dump (via a
    scratch `[Test]`, removed before finalizing) over
    `McpFeatureToggleFilter.GetAttributedTypes(assembly, typeof(McpServerToolTypeAttribute))` expanded to
    each type's `[McpServerTool(Name=...)]` methods, rather than trusting the PRD's prose tool names. This
    surfaced that some `[McpServerToolType]` classes expose MULTIPLE verbs (`LinkFromRepositoryTool` → 3,
    `DataForgeTool` → 7, `RestartTool` → 3, `StopTool` → 3, `RestoreDbTool` → 3, `ClearRedisTool` → 2,
    `DownloadConfigurationTool` → 2, `LoadPackagesTool` → 2, `PackageHotfixTool` → 2, `FsmModeTool` → 2),
    which is why `Classification`/`Coverage` are keyed by TOOL VERB NAME, not by C# type — a type-keyed
    design would have made `LinkFromRepositoryTool`'s three independently-classified verbs uncrepresentable.
    It also surfaced 5 PRD-prose tool names that are STALE vs. the actual registered name: PRD's
    "show-web-app-list" → actual `list-environments` (`ShowWebAppListTool`); PRD's "install-creatio" →
    actual `deploy-creatio` (`InstallerCommandTool`); PRD's "install-skills"/"update-skill"/"delete-skill" →
    actual `install-toolkit`/`update-toolkit`/`delete-toolkit`; PRD's "get-settings-health" → actual
    `check-settings-health`. The registry keys on the ACTUAL current name in every case (with an inline
    comment citing the PRD's older name) since the completeness test must match the live tree, not the
    PRD's wording. **Flag for whoever owns Story 17 (docs):** the PRD's own "Out of scope" prose has drifted
    from the current tool names; worth a PRD/docs correction pass.
  - **150 tools discovered, 150 classified** (verified equal by the completeness test): 12 `Routed`
    (7 c1 + `get-user-culture` + `update-page`/`sync-pages`/`get-component-info`/`build-theme`), 3 `Guarded`
    (the `link-from-repository-*` family), 20 `NotEnvironmentSensitive` (PRD's literal out-of-scope list,
    reconciled to current names as above), 115 `NotApplicable` (every remaining class (a)/(b) tool,
    already passthrough-capable before this feature, per PRD "No change required to class (a)/(b)").
  - **Two documented, deliberately narrower-than-literal readings of AC-03** (both flagged to the architect,
    not silently assumed — see the type-level remarks on `PassthroughToolClassificationRegistry` for the
    full rationale):
    1. **Nested sub-call paths require `HeaderOnly` only**, not the full `HeaderOnly`+`MixedInput`+
       `RegisteredEnvStdio` matrix AC-03's prose could be read to require for every path. Stories 1-14
       deliberately wrote ONLY `HeaderOnly` tests for these paths (verified: Story 6's "four extra
       nested-path tests" are each exactly one `HeaderOnly` test; no Mixed/RegisteredEnvStdio unit or E2E
       test exists for any nested path). Requiring them here would be unimplementable without writing NEW
       Stories 1-14 tests, which is out of this story's authorized scope, and is unnecessary: under
       `MixedInput` the outer resolve throws before any nested call runs at all; under
       `RegisteredEnvStdio` every nested call receives the SAME settings object the outer resolve produced
       (the "settings-based overload never calls a name-based overload" invariant, ADR OQ-01) — so a
       nested-path Mixed/RegisteredEnvStdio test would prove nothing the outer-path test doesn't already
       prove.
    2. **`update-page`'s `write` path is excluded from `RequiredCoverage`** even though AC-03's prose uses
       it as the illustrative "separate rows" example. The write path was already resolver-backed BEFORE
       this feature (PRD: "resolves its write command through the resolver ... honors the header"); only
       the `version-probe` path was the defect Story 11 fixed, and no Stories 1-14 test targets the write
       path specifically (it is proven generically by the shared `BaseTool`/`ToolCommandResolver`
       infrastructure, same as every other already-compliant class-(a) tool).
  - **Consulted the architect-tier advisor twice** before writing code: once to confirm the registry's
    test-project location and the type→verb key-space (confirmed via a live reflection dump before writing
    a single Classification row), and once to resolve the nested-path scenario-requirement design
    (confirmed Design A: entry paths get the full matrix, nested paths get `HeaderOnly` only, and the
    required-set must be HAND-AUTHORED — never derived from `Coverage`'s own contents — precisely so an
    omitted row cannot trivially satisfy its own requirement).
  - **`link-from-repository-by-env-package-path`'s `RegisteredEnvStdio` row has no Stories 1-14 unit test**
    (Story 1 covers `skip-preparation=true`/`false` but not a dedicated "unchanged, guard-inactive,
    registered-env" case) — this tuple is satisfied ONLY by Story 15's E2E
    `Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused("link-from-repository-by-env-package-path")` row,
    which is exactly the scenario AC-06 anticipates by naming both Stories 1-14 unit tests AND Story 15 E2E
    methods as valid sources.
  - Did not modify `spec/sprint-status.yaml` per the work order.
