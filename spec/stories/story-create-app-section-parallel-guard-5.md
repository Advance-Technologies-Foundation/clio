# Story 5: clio.mcp.e2e Concurrent-Create Scenario (Option C — coverage)

**Feature**: create-app-section-parallel-guard
**FR coverage**: FR-09
**PRD**: [prd-create-app-section-parallel-guard.md](../prd/prd-create-app-section-parallel-guard.md)
**ADR**: [adr-create-app-section-parallel-guard.md](../adr/adr-create-app-section-parallel-guard.md)
**Jira**: ENG-93089 (JAC-2)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: story-create-app-section-parallel-guard-1, story-create-app-section-parallel-guard-2 (exercises the guard + retry/verify behavior end to end)
**Blocks**: none

---

## As a

QA engineer

## I want

a deterministic `clio.mcp.e2e` scenario that issues N concurrent `create-app-section` calls against one seeded application through the real `clio mcp-server` and asserts no spurious failure plus the retry/serialization recovery

## So that

this exact contention bug is guarded against regression and the fix is provably exercised end to end

---

## Acceptance Criteria

- [ ] **AC-01** — Given a seeded application and N concurrent `create-app-section` calls issued against it through the real `clio mcp-server`, when the guard + recovery are active, then no call fails with a contention-caused `InsertQuery failed` / `contention` and every valid section is created — across repeated runs (traces PRD AC-01 / AC-05 / SM-01 / FR-09, JAC-2).
- [ ] **AC-02** — Given the same scenario, when it runs, then it exercises the real `clio mcp-server` path (not a mock) and asserts both the no-spurious-failure outcome and the retry/serialization recovery (traces PRD AC-05 / FR-09).
- [ ] **AC-03** — Given a legitimately invalid input in the batch (e.g. duplicate section code), when the scenario includes a counter-case, then it still fails fast with its actionable message and remains non-retryable — the guard does not mask real rejections (traces CM-01 / FR-04).
- [ ] **AC-ERR** — Given the scenario is destructive and environment-sensitive, when authored, then it is guarded by `AllowDestructiveMcpTests` + a seeded `EnvironmentName` and categorized `[Category("McpE2E.Sandbox")]`, mirroring existing sandbox tests, and is explicitly noted as **NOT in CI** (manual/release run only).

## Implementation Notes

Real-`clio mcp-server` E2E. MCP E2E is **not in CI** — flag this in the story and the test plan; the scenario is manual/release execution only.

Key file (new test): `clio.mcp.e2e/ApplicationSectionToolE2ETests.cs`
- New `[Category("McpE2E.Sandbox")]` test guarded by `AllowDestructiveMcpTests` + seeded `EnvironmentName`, mirroring the existing sandbox tests in that project.
- Issue N concurrent `create-app-section` calls against one seeded app; assert all sections created, zero `contention`/`InsertQuery failed` failures across repeated runs.
- Include a counter-case (invalid input) asserting fast, actionable, non-retryable failure (CM-01 / FR-04).

Pattern to follow: existing destructive sandbox tests in `clio.mcp.e2e/` (guard + seeded environment + harness helpers).

Use the `$test-mcp-tool` skill per repo policy.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| E2E `[Category("McpE2E.Sandbox")]` | N concurrent creates ⇒ no spurious failure, all created; retry/serialization recovery; invalid-input counter-case fails fast | `clio.mcp.e2e/ApplicationSectionToolE2ETests.cs` (new) |

Test naming: `MethodName_ShouldExpectedBehavior_WhenCondition`. **NOT in CI** — manual/release only; call this out in the PR and test plan.

## Definition of Done

- [ ] E2E scenario added, guarded by `AllowDestructiveMcpTests` + seeded `EnvironmentName`, `[Category("McpE2E.Sandbox")]`
- [ ] Scenario asserts no-spurious-failure + recovery + invalid-input counter-case
- [ ] Explicitly documented as NOT in CI (manual/release run only)
- [ ] `clio.mcp.e2e` project still builds
- [ ] Manual run against a live stand recorded in the PR / Dev Agent Record before merge
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
