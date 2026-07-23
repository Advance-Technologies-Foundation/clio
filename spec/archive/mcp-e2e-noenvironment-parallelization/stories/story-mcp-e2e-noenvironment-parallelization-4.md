# Story 4: Fail-Closed Parallel Cohort and Timing Proof

**Feature**: mcp-e2e-noenvironment-parallelization
**Jira**: ENG-92558
**PRD**: [prd-mcp-e2e-noenvironment-parallelization.md](../prd/prd-mcp-e2e-noenvironment-parallelization.md)
**ADR**: [adr-mcp-e2e-noenvironment-parallelization.md](../adr/adr-mcp-e2e-noenvironment-parallelization.md)
**Status**: in-progress
**Size**: M

---

## As a

CI pipeline owner

## I want

only vetted NoEnvironment fixtures to run in parallel with a conservative worker cap

## So that

the MCP e2e test step becomes faster without introducing shared-stand flake

## Acceptance Criteria

- [x] No assembly-wide `[Parallelizable]` is added.
- [x] Vetted NoEnvironment contract fixtures are explicitly marked `[Parallelizable(ParallelScope.Self)]`.
- [x] Process-global environment mutator fixtures remain `[NonParallelizable]` and outside the parallel cohort.
- [x] A `clio.mcp.e2e` `.runsettings` file caps `NUnit.NumberOfTestWorkers`.
- [ ] Worker count is justified in the PR using CI timing/stability observations.
- [ ] `McpE2E.NoEnvironment` passes with the new runsettings.
- [ ] Full TeamCity `Team_Atf_ClioMcpE2eTests` is green twice on the feature branch.
- [ ] PR records before/after NoEnvironment-tier and total test-step wall-clock.

## Implementation Notes

- Start with 2 workers unless CI measurement supports 3.
- Parallelize fixtures incrementally. If a fixture shows state coupling, remove it from the cohort rather than weakening tests.
- Treat the serial Sandbox floor as expected; do not chase it in this story.

## Test Requirements

| ID | Type | Scenario | Expected |
|----|------|----------|----------|
| TC-E2E-05 | MCP e2e | NoEnvironment filter with runsettings | pass, zero unexpected skips |
| TC-CI-01 | TeamCity | full MCP e2e run #1 | green with timings recorded |
| TC-CI-02 | TeamCity | full MCP e2e run #2 | green with timings recorded |

## Definition of Done

- [ ] Two TeamCity run URLs are linked in PR.
- [ ] Before/after timing table is in PR.
- [ ] Any deviation from the ~30 min target is explained with measurements.

## Dev Agent Record

### Implementation Notes

- Added `clio.mcp.e2e/clio.mcp.e2e.runsettings` with `NUnit.NumberOfTestWorkers=2`.
- Wired the runsettings through `RunSettingsFilePath` in `clio.mcp.e2e.csproj`.
- Added `[Parallelizable(ParallelScope.Self)]` only to vetted shared-server NoEnvironment fixtures.
- Confirmed no assembly-wide parallelization was added to `clio.mcp.e2e`.
- Fixed `ClearRedisToolE2ETests` so its two `McpE2E.NoEnvironment` negative tests no longer require sandbox configuration.
- Full local `Category=McpE2E.NoEnvironment` run was attempted but interrupted after it exceeded a reasonable local duration; before interruption it exposed the fixed `ClearRedis` sandbox-config dependency. Full TeamCity proof is still required.

### Validation

- `dotnet build clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --no-restore`
- `dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --no-build --filter "FullyQualifiedName~ClearRedisToolE2ETests"` - 2 passed.
- Vetted parallel cohort run: `dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --no-build --filter "<vetted cohort + McpFixturePolicyTests + ClearRedisToolE2ETests>"` - 48 passed in 2m48s.
