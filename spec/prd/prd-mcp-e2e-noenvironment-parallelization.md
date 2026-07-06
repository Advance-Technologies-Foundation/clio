# PRD: MCP E2E NoEnvironment Parallelization

**Status**: Draft
**Author**: PM Agent
**Created**: 2026-07-01
**Jira**: ENG-92558
**Parent**: ENG-90640

---

## Problem Statement

`clio.mcp.e2e` still spends substantial wall-clock time on avoidable `clio mcp-server`
cold starts. The suite is correctly split into `McpE2E.NoEnvironment` and
`McpE2E.Sandbox`, but many no-stand tests still start a new MCP server per test or sit
inside class-level `[NonParallelizable]` mixed Sandbox fixtures. This keeps safe,
contract-only tests on the serial critical path even though they do not touch the shared
destructive Creatio stand.

The safe opportunity is narrow: speed up the NoEnvironment tier while preserving the exact
test coverage and keeping every Sandbox test serial. Full Sandbox fixture parallelization is
out of scope and belongs to ENG-88789.

## Current Baseline

- `McpContractFixtureBase` exists and starts one shared MCP server per fixture, but has no
  isolated-home/process-environment override hook.
- `PackageHotfixToolE2ETests`, `AddPackageDependencyToolE2ETests`, and other pure
  NoEnvironment fixtures still start MCP per test.
- `OAuthConfigurationToolsE2ETests` and `SkillManagementToolE2ETests` need isolated home
  behavior before they can safely share a fixture-level server.
- Mixed fixtures such as `ApplicationToolE2ETests`, `WorkspaceSyncToolE2ETests`,
  `ApplicationSection*ToolE2ETests`, and `EntitySchemaToolE2ETests` still contain
  NoEnvironment tests inside `[NonParallelizable]` Sandbox fixtures.
- `FsmModeToolE2ETests` contains a Sandbox test but lacks class-level
  `[NonParallelizable]`.
- No `clio.mcp.e2e` `.runsettings` worker cap exists.

## Goals

- [ ] G1: Remove avoidable per-test MCP cold starts from safe NoEnvironment contract tests.
  Success measure: converted fixtures start one server per fixture, not per test.
- [ ] G2: Allow only vetted NoEnvironment contract fixtures to run in parallel.
  Success measure: no assembly-wide parallelization; only explicit NoEnvironment fixtures
  carry `[Parallelizable(ParallelScope.Self)]`.
- [ ] G3: Preserve all Sandbox safety constraints.
  Success measure: every fixture containing `McpE2E.Sandbox` tests is class-level
  `[NonParallelizable]`, enforced by a meta-test.
- [ ] G4: Improve TeamCity wall-clock while preserving coverage.
  Success measure: `Team_Atf_ClioMcpE2eTests` trends from roughly 39-42 min toward
  about 30 min, with before/after timings recorded.

## Non-goals

- No full Sandbox fixture parallelization or concurrent destructive access to the shared stand.
- No command, MCP tool, output, exit-code, or argument-contract changes.
- No assertion weakening, test deletion, or new `[Ignore]` to make timing look better.
- No retagging of `ShowWebAppListToolE2ETests` to NoEnvironment; it must keep validating
  the real registered sandbox secret-masking behavior.
- No TeamCity job configuration changes beyond documenting required commands and timings.

## Feature Requirements

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-01 | Add a fixture-level isolated-home/process-environment hook to `McpContractFixtureBase` | Must |
| FR-02 | Converted shared fixtures must use child-process environment variables only, never process-global `Environment.SetEnvironmentVariable` | Must |
| FR-03 | Convert safe pure NoEnvironment fixtures to `McpContractFixtureBase` | Must |
| FR-04 | Preserve fixture-specific isolated homes for OAuth and skill-management tests before sharing them | Must |
| FR-05 | Split stand-free NoEnvironment tests out of mixed Sandbox fixtures into shared-server contract fixtures | Must |
| FR-06 | Keep live Sandbox tests in serial `[NonParallelizable]` fixtures | Must |
| FR-07 | Add a meta-test that fails when any `McpE2E.Sandbox` test is in a fixture without class-level `[NonParallelizable]` | Must |
| FR-08 | Add fail-closed NoEnvironment parallelization without assembly-wide `[Parallelizable]` | Must |
| FR-09 | Add `.runsettings` with a conservative worker cap and document the chosen value | Must |
| FR-10 | Preserve the fully-qualified test-id set across NoEnvironment and Sandbox filters | Must |
| FR-11 | Record NoEnvironment-tier and total TeamCity test-step wall-clock before and after | Must |

## Acceptance Criteria

- [ ] AC-01: `McpContractFixtureBase` supports derived fixture environment overrides and temporary home lifecycle without process-global environment mutation.
- [ ] AC-02: Pure NoEnvironment fixtures converted in this scope no longer call `McpServerSession.StartAsync` per test.
- [ ] AC-03: NoEnvironment tests split from mixed fixtures retain their original assertions and categories.
- [ ] AC-04: Every fixture containing a `McpE2E.Sandbox` test has class-level `[NonParallelizable]`; known gap `FsmModeToolE2ETests` is fixed.
- [ ] AC-05: A meta-test enforces AC-04 for future drift.
- [ ] AC-06: No assembly-wide parallelization is introduced.
- [ ] AC-07: Only vetted NoEnvironment contract fixtures are marked `[Parallelizable(ParallelScope.Self)]`.
- [ ] AC-08: A two-fixture isolation canary proves same-key feature/appsettings writes do not cross-contaminate isolated homes.
- [ ] AC-09: `dotnet test --list-tests` before/after for `McpE2E.NoEnvironment` and `McpE2E.Sandbox` shows no removed test IDs and no uncategorized drift.
- [ ] AC-10: TeamCity `Team_Atf_ClioMcpE2eTests` is green twice on the feature branch, with before/after timing recorded.

## Assumptions

| ID | Assumption | Risk if wrong |
|----|------------|---------------|
| A-01 | Per-server MCP cold start remains roughly 10 s on TeamCity | Savings may be smaller; record actual timings |
| A-02 | CI agents can safely run 2-3 MCP server workers without memory/CPU saturation | Worker cap may need to be lower, reducing the gain |
| A-03 | NoEnvironment test assertions are independent of shared server state once isolated homes are used | Some fixtures may need to stay serial or per-test |

## Dependencies

- Depends on: ENG-92150 tier tagging and ENG-90640 stabilization work.
- Related but separate: ENG-92563 MCP host DI registration optimization.
- Out-of-scope follow-up: ENG-88789 Sandbox fixture parallelization.
