# ADR: MCP E2E NoEnvironment Parallelization

**Status**: Draft
**Author**: Architect Agent
**PRD**: [prd-mcp-e2e-noenvironment-parallelization.md](../prd/prd-mcp-e2e-noenvironment-parallelization.md)
**Created**: 2026-07-01
**Jira**: ENG-92558

---

## Context

The MCP e2e suite has two infrastructure tiers: `McpE2E.NoEnvironment` and
`McpE2E.Sandbox`. NoEnvironment tests validate tool discovery, schemas, binding, and
structured failure contracts without a live Creatio stand. Sandbox tests touch the shared
destructive stand and must stay serial.

`McpContractFixtureBase` already demonstrates the high-value pattern: one MCP server process
per fixture instead of per test. ENG-92558 extends that pattern and introduces fail-closed
parallelization only for vetted NoEnvironment fixtures.

## Decision

Implement a four-step plan:

1. Add isolated fixture environment support and Sandbox guard enforcement.
2. Convert safe pure NoEnvironment fixtures to shared-server fixtures.
3. Split NoEnvironment tests out of mixed Sandbox fixtures.
4. Opt vetted NoEnvironment contract fixtures into fixture-level parallelism with a
   conservative `.runsettings` worker cap.

The design is deliberately fail-closed: no assembly-wide parallelization, no Sandbox
parallelization, and no process-global environment mutation in parallel fixtures.

## Key Design Decisions

### Decision 1: Fixture-level environment override hook

`McpContractFixtureBase` gains overridable setup points that allow derived fixtures to:

- create a temporary fixture-owned home directory;
- seed `appsettings.json` or feature flags before the shared MCP server starts;
- add child-process environment variables such as `CLIO_HOME`, `HOME`, `USERPROFILE`,
  `LOCALAPPDATA`, or tool-specific overrides;
- clean up in one-time teardown.

The hook mutates only the `McpE2ESettings.ProcessEnvironmentVariables` passed to the child
server process. It must not use process-global `Environment.SetEnvironmentVariable`.

### Decision 2: Shared server only for read-only/stateless contract tests

A fixture can share a server only when its tests do not depend on per-test startup state and
do not mutate process-global environment. Per-test temporary workspaces remain per-test even
when the MCP server is shared.

OAuth and skill-management fixtures are eligible only after they can use fixture-level
isolated homes safely.

### Decision 3: Split mixed fixtures, do not retag behavior

Mixed fixtures keep their live Sandbox tests in the original serial fixture. Stand-free
NoEnvironment tests move into new contract fixtures. Assertions, tool calls, and categories
must be preserved. The split changes scheduling and server lifecycle, not coverage.

### Decision 4: Fail-closed parallelization

Do not add `[assembly: Parallelizable]`. Instead, add `[Parallelizable(ParallelScope.Self)]`
only to vetted NoEnvironment contract fixtures. Any missed fixture remains serial by default.

Every fixture containing any `McpE2E.Sandbox` test must have class-level
`[NonParallelizable]`, enforced by a meta-test.

### Decision 5: Worker cap is part of the design

Parallel MCP servers are expensive enough that oversubscription can erase the gain or create
flake. Add a `clio.mcp.e2e` `.runsettings` file with a conservative
`NUnit.NumberOfTestWorkers`, expected to be 2-3 after CI measurement. The chosen cap and
before/after timings must be recorded in the PR.

## Alternatives Considered

| Option | Pros | Cons | Decision |
|--------|------|------|----------|
| Assembly-wide `[Parallelizable]` | Minimal code | Unsafe; missed Sandbox fixtures can parallelize accidentally | Rejected |
| Parallelize Sandbox fixtures too | Bigger possible speedup | Shared destructive stand collisions; belongs to ENG-88789 | Rejected |
| Convert all fixtures to shared server | Simple-looking | Breaks startup-state and mutation assumptions | Rejected |
| Keep all mixed fixtures intact | Lower refactor | NoEnvironment tests remain on serial critical path | Rejected |

## Implementation Plan

1. Foundation:
   - extend `McpContractFixtureBase`;
   - add fixture-home helpers and isolation canary;
   - add Sandbox guard meta-test;
   - fix `FsmModeToolE2ETests` guard.
2. Pure fixture conversion:
   - convert eligible pure NoEnvironment fixtures;
   - preserve per-test workspace temp dirs;
   - keep process-global env mutators excluded.
3. Mixed fixture split:
   - move stand-free NoEnvironment tests to contract fixtures;
   - keep Sandbox fixtures serial and per-test where needed.
4. Parallel cohort:
   - mark vetted NoEnvironment contract fixtures `[Parallelizable(ParallelScope.Self)]`;
   - add `.runsettings`;
   - produce list-tests diff and TeamCity timing evidence.

## Validation

- Unit/meta tests for guard and isolation behavior.
- `dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj --list-tests --filter "Category=McpE2E.NoEnvironment"` before/after.
- Same list-tests diff for `Category=McpE2E.Sandbox`.
- `dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --filter "Category=McpE2E.NoEnvironment" --settings <runsettings>`.
- Two consecutive green TeamCity `Team_Atf_ClioMcpE2eTests` runs.

## Consequences

- NoEnvironment tier becomes faster and safer to run in parallel.
- Sandbox floor remains the dominant post-change cost.
- Future MCP e2e additions get stronger guard rails around tier classification and seriality.
