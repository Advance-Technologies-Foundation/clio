# Story 5: test coverage — ambiguous-failure re-run class, E2E, read-budget counter-metric

**Feature**: sync-schemas-ensure-semantics
**ADR unit**: U5
**FR coverage**: FR-05, FR-06 (SM-01, SM-02, SM-03, AC-03, AC-09)
**PRD**: [prd-sync-schemas-ensure-semantics.md](../prd/prd-sync-schemas-ensure-semantics.md)
**ADR**: [adr-sync-schemas-ensure-semantics.md](../adr/adr-sync-schemas-ensure-semantics.md)
**Jira**: ENG-93807
**Status**: ready-for-dev
**Size**: M (half day)

---

## As a

QA engineer / CI pipeline author relying on `sync-schemas` retries

## I want

a consolidated ambiguous-failure re-run test class, mandatory `clio.mcp.e2e` coverage, and a
read-budget counter-metric assertion

## So that

the three residual holes are provably eliminated (or explicitly deferred for seed) and the read cost
is proven not to add an MCP round-trip

---

## Acceptance Criteria

- [ ] **AC-03 (the thesis, SM-01/SM-02)** — Given any convergent op whose mutation already applied on the server but whose response was lost, when the identical batch is re-run, then the result is `success: true` (`outcome: already-satisfied`) with no masked failure and no duplicate or rejected mutation. Covered by the ambiguous-failure re-run test class, which must stay green (SM-01c counter).
- [ ] **AC-BUDGET (SM-03, round-trip formulation per OQ-04/OI-01)** — Given a clean (no-collision) batch, when it runs, then it adds **zero MCP round-trips** and performs **no post-write verify read-back**; server-side DataService reads within the single batch call are NOT counted against the budget (up to 2 server-side reads on the reconcile path). Expected-and-passing: create-only path = 1 server-side read/op, reconcile-existing path = 2 server-side reads/op. Do NOT assert the literal AC-09 "at most one extra state-read per operation" wording — the ADR (OI-01) flags it as self-contradictory for the reconcile path.
- [ ] **AC-E2E** — Given real `clio mcp-server`, the E2E suite exercises absent-create → success, existing-in-package reconcile → only-missing-added, replay idempotency (AC-03), and cross-package collision → `success:false` + `outcome:collision`; plus a contract-text assertion of the updated `BuildSchemaSync`.

## Implementation Notes

Unit tier (NUnit + NSubstitute + FluentAssertions):
- Consolidated **ambiguous-failure re-run class** in `SchemaSyncToolTests` covering all outcomes
  (`created`/`reconciled`/`already-satisfied`/`collision`), no-op columns, remove-already-absent, and
  the collision path — this class staying green IS the SM-01c/SM-02c counter-metric guard.
- Read-count/round-trip assertion on the clean path: verify no verify-read-back and no added MCP
  round-trip; assert server-side read counts (1 create-only, 2 reconcile) via the mocked
  `IApplicationClient` / command call counts.

E2E tier (`clio.mcp.e2e`, real `mcp-server`, MCP protocol — **NOT in CI yet; run manually**):
- `clio.mcp.e2e/SchemaSyncToolE2ETests.cs`: absent-create, existing-reconcile, replay-idempotency,
  cross-package collision.
- `clio.mcp.e2e/ToolContractGetToolE2ETests.cs`: assert updated `BuildSchemaSync` contract text.

Depends on Stories 1, 2, 3 (behaviors under test) and Story 4 (contract text asserted by the E2E).
Classifier-level and per-op unit tests introduced in Stories 1/2/3 are NOT duplicated here; this story
adds the consolidated re-run class, the budget assertion, and the E2E surface.

Key files: `clio.tests/Command/McpServer/SchemaSyncToolTests.cs`,
`clio.tests/Command/McpServer/SchemaConvergenceServiceTests.cs`,
`clio.mcp.e2e/SchemaSyncToolE2ETests.cs`, `clio.mcp.e2e/ToolContractGetToolE2ETests.cs`.
Pattern to follow: existing `SchemaSyncToolTests` fixture and existing `clio.mcp.e2e` harness.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | ambiguous-failure re-run class (all outcomes, collision, no-op, remove-absent); clean-path round-trip/read-count budget | `clio.tests/Command/McpServer/SchemaSyncToolTests.cs`, `SchemaConvergenceServiceTests.cs` |
| E2E `[Category("E2E")]` | absent-create / existing-reconcile / replay-idempotency / cross-package collision; contract-text assertion — manual, NOT in CI | `clio.mcp.e2e/SchemaSyncToolE2ETests.cs`, `ToolContractGetToolE2ETests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`; `[Property("Module","McpServer")]`; AAA + `because`;
`[Description(...)]` per method; `[Category("Unit")]` (never `UnitTests`).

## Definition of Done

- [ ] Ambiguous-failure re-run class covers all outcomes and stays green (SM-01c/SM-02c).
- [ ] Read-budget assertion uses the round-trip formulation (zero added MCP round-trips, no verify read-back; server-side reads not counted) — NOT the literal one-state-read wording.
- [ ] Mandatory `clio.mcp.e2e` coverage (`SchemaSyncToolE2ETests`) added. Because AC-03 (idempotent-replay thesis) and AC-09 (round-trip budget) are verifiable ONLY at the E2E tier and `clio.mcp.e2e` is NOT in CI, the manual E2E run is a **HARD, RECORDED merge gate**: the PR MUST NOT merge without the recorded manual E2E result attached to the PR (this is the compensating control for the absence of CI E2E — not merely "flagged").
- [ ] Code compiles without Roslyn analyzer warnings (CLIO001–CLIO005).
- [ ] All new unit tests use `[Category("Unit")]` (never `UnitTests`); E2E uses `[Category("E2E")]`.
- [ ] PR description references this story file.

## Dev Agent Record

- Implementation started: 2026-07-20
- Implementation completed: 2026-07-20
- Tests passing: Unit — `dotnet test clio.tests/clio.tests.csproj -f net10.0 --filter "Category=Unit&Module=McpServer" --no-build` → 2555 passed, 0 failed, 1 skipped. E2E — `dotnet build clio.mcp.e2e/clio.mcp.e2e.csproj` → 0 errors, 0 new warnings in touched files. E2E execution is the HARD, RECORDED manual/TeamCity (Team_Atf_ClioMcpE2eTests) merge gate: clio.mcp.e2e is NOT in CI and the stdio harness does not run locally.

### New unit tests (clio.tests/Command/McpServer/SchemaSyncToolTests.cs)
Added under a new `#region Ambiguous-failure re-run class (AC-03 - SM-01c/SM-02c counter-metric)` that also carries the manifest mapping every re-run matrix cell to its existing (Stories 1/2/3) covering test:
- `SchemaSync_CreateLookup_ShouldClassifyOnceAndNotReadBackAfterWrite_WhenSchemaCreatedCleanly` — clean create: `Classify` received exactly once, `ReadColumns` never (no post-write verify read-back; zero added round-trip).
- `SchemaSync_UpdateEntity_ShouldReadColumnsOnceAndNotReadBackAfterWrite_WhenReconcilingCleanly` — clean update: `ReadColumns` received exactly once, `Classify` never.

The server-side read-count budget (AC-BUDGET: 1 create-only / 2 reconcile) was already covered at the service tier by `SchemaConvergenceServiceTests.Classify_ShouldReadSchemaExactlyOnce_WhenSchemaIsAbsent` and `Classify_ShouldReadSchemaTwice_WhenSchemaExistsInTargetPackage` (not duplicated).

### New E2E tests — the manual merge gate (run these on a live stand / TeamCity)
clio.mcp.e2e/SchemaSyncToolE2ETests.cs (inherit class-level `[Category("McpE2E.Sandbox")]`):
- `SchemaSync_AbsentSchema_ShouldReportCreatedOutcome_WhenCreatedOnRealEnvironment`
- `SchemaSync_ExistingSchema_ShouldReportReconciledOutcomeAndAddOnlyMissingColumn_WhenReconciledOnRealEnvironment`
- `SchemaSync_IdenticalReplay_ShouldReportAlreadySatisfiedWithNoDuplicateMutation_WhenBatchReRun` (AC-03)
- `SchemaSync_CrossPackageSchema_ShouldReportCollisionOutcome_WhenSchemaExistsInDifferentPackage`

clio.mcp.e2e/ToolContractGetToolE2ETests.cs (inherit class-level `[Category("McpE2E.NoEnvironment")]`):
- `ToolContractGet_Should_Advertise_Convergent_SchemaSync_Contract`

- Notes: Deviation from DoD literal wording — the two E2E fixtures categorize at the CLASS level with the `McpE2E.*` scheme (which drives TeamCity `TestCategory!=` filtering); no per-method `[Category("E2E")]` was added because that tag is unknown to the harness. Followed the harness per AGENTS.md; flagged for architect adjudication. Cross-package collision E2E uses the OOTB `Contact` schema name (owned by a base package, never the sandbox package) to trigger a real cross-package collision without provisioning a second package; the owning-package name is asserted loosely (non-empty). No PR opened (out of scope), so "PR description references this story file" is N/A here.
