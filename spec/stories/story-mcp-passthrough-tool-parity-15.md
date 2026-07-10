# Story 15: Consolidated E2E coverage — multi-tenant, concurrency isolation, and no-regression suites (FR-08)

**Feature**: mcp-passthrough-tool-parity
**FR coverage**: FR-08, FR-10
**PRD**: [prd-mcp-passthrough-tool-parity.md](../prd/prd-mcp-passthrough-tool-parity.md)
**ADR**: [adr-mcp-passthrough-tool-parity.md](../adr/adr-mcp-passthrough-tool-parity.md)
**Status**: ready-for-dev
**Size**: L (full day)

---

## As a

QA engineer certifying the passthrough tool set for the AI-Platform gateway (ENG-92869)

## I want

one story that **owns** every MCP E2E deliverable mandated by PRD FR-08 / AC-07 / AC-09 and the ADR test
strategy — the multi-tenant cases, the per-routed-tool two-tenant isolation proofs, and the per-touched-tool
stdio + registered-HTTP no-regression sweep

## So that

E2E coverage is not diffused as unowned "sweep" references across stories 1-14, and Story 16's coverage
registry has concrete, named test methods to map to

---

## Status note (mandatory context)

**MCP E2E is NOT in CI.** Every case authored here is compile-verified in `clio.mcp.e2e` and executed
**manually against a live stand** (same caveat and skip mechanism as ENG-93208 story 15:
`Assert.Ignore`/stand-config guard when the `CLIO_MCP_HTTP_E2E_*` environment variables are absent). The
manual run before merge is part of this story's DoD.

## Acceptance Criteria

- [ ] **AC-01** — **Mandatory multi-tenant cases (PRD FR-08, ADR test strategy rows 8-9), extending
  `McpHttpMultiTenantE2ETests`.** For each of the four mandated targets, BOTH input shapes are covered —
  **header-only** (executes against the header tenant / uniform rejection per the tool's decision) and
  **header + `environment-name`** (mixed input — proves no confused-deputy, PRD AC-06):
  - `list-apps` (Story 3)
  - `create-app-section` — the "one section tool" case, and its assertion must reach the **nested
    caption-culture path** (Story 6)
  - `get-user-culture` (Story 10)
  - one Creatio-reaching `link-from-repository-*` branch — `link-from-repository-unlocked` (always
    Creatio-reaching) asserting the uniform guard rejection (Story 1)
- [ ] **AC-02** — **Two-tenant concurrency isolation for EVERY newly routed tool (PRD AC-07), extending
  `McpHttpConcurrencyIsolationE2ETests`.** The newly routed set is exactly: `list-apps`, `get-app-info`,
  `create-app`, `create-app-section`, `update-app-section`, `delete-app-section`, `list-app-sections`,
  `get-user-culture`, `update-page` (version probe), `sync-pages` (version probe), `get-component-info`
  (mixed-input path), `build-theme` (version path). Each gets the same two-tenant isolation proof already
  established for `describe-environment` (`McpHttpMultiTenantE2ETests.cs:33`) — distinct authenticated
  containers, no cross-tenant session/log bleed.
- [ ] **AC-03** — **No-regression sweep for EVERY touched tool (PRD AC-09 / SM-03), extending
  `McpHttpNoRegressionE2ETests`.** For each of the 15 touched tools (the 12 routed tools above + the three
  `link-from-repository-*` tools), behavior over `clio mcp` (stdio) AND `clio mcp-http -e <env>` with
  `environment-name` supplied matches the pre-change baseline — this is also the layer that proves the
  `[Required]` relaxations (stories 1, 3-9, 12) did not break existing registered-env callers.
- [ ] **AC-04** — **Exact, named test methods.** Every case above is a named `[Test]` method following
  `MethodName_ShouldBehavior_WhenCondition`, e.g.
  `ApplicationGetList_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly`,
  `ApplicationSectionCreate_ShouldResolveHeaderTenantCulture_WhenHeaderOnly`,
  `LinkFromRepositoryUnlocked_ShouldReturnUniformRejection_WhenPassthroughActive`. The full
  (tool, dependency-path, scenario) → method list is recorded in this story's Dev Agent Record on
  completion — Story 16's registry consumes it verbatim.
- [ ] **AC-05** — Given the E2E project, when it is built in CI, then it **compiles** on every push (the
  suites self-skip without stand config); no `[Category]` other than `E2E` is used.
- [ ] **AC-ERR** — Given a stand-configured run where a case fails, when the suite reports, then failure
  output never echoes `accessToken`/`login`/`password` material (assert on redacted text only).

## Implementation Notes

- Extend the three existing ENG-93208 fixtures — `McpHttpMultiTenantE2ETests`,
  `McpHttpConcurrencyIsolationE2ETests`, `McpHttpNoRegressionE2ETests` — rather than creating parallel
  fixtures; reuse their stand-config/skip plumbing (`McpHttpPassthroughStand`, `CLIO_MCP_HTTP_E2E_*`).
- This story runs **after stories 1-14** (it exercises their shipped behavior) and **before Story 16**
  (whose registry rows name these methods) and Story 17 (docs).
- Keep per-tool cases data-driven where the assertion shape is identical (e.g. `[TestCaseSource]` over the
  newly-routed-tool list for the isolation proof) — but each (tool, path, scenario) must still surface as an
  individually named/reportable case so Story 16's reflection check can find it.
- The `link-from-repository-by-env-package-path` `skip-preparation=false` rejection and `=true` bypass are
  unit-covered in Story 1; here only the `unlocked` branch is mandated (one Creatio-reaching branch), though
  adding the package-path branch is cheap if the stand supports it.

Key files: `clio.mcp.e2e/McpHttpMultiTenantE2ETests.cs`, `clio.mcp.e2e/McpHttpConcurrencyIsolationE2ETests.cs`,
`clio.mcp.e2e/McpHttpNoRegressionE2ETests.cs` (extend all three).
Pattern to follow: the existing `describe-environment` two-tenant case (`McpHttpMultiTenantE2ETests.cs:33`)
and the ENG-93208 story-15 skip/stand mechanics.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | none — unit coverage lives in stories 1-14 | — |
| Integration `[Category("Integration")]` | none required | — |
| E2E `[Category("E2E")]` | Everything in AC-01..AC-03 (this story IS the E2E deliverable) | `clio.mcp.e2e/McpHttp{MultiTenant,ConcurrencyIsolation,NoRegression}E2ETests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [ ] All `CLIO*` diagnostics clean in changed files (FR-10; note `clio.mcp.e2e` is outside the Sonar gate
  but the analyzers still apply at build)
- [ ] `clio.mcp.e2e` compiles; suites self-skip without stand config
- [ ] Manual live-stand run executed; results (pass/fail per case) recorded in the Dev Agent Record
- [ ] The (tool, dependency-path, scenario) → test-method list is recorded for Story 16's registry
- [ ] Unit tests N/A; E2E tests carry `[Category("E2E")]` only
- [ ] PR description references this story file and flags "MCP e2e NOT in CI — manual run attached"

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started:
- Implementation completed:
- Tests passing:
- (tool, dependency-path, scenario) → test-method list:
- Notes:
