# Story 1: Shared Fixture Foundation and Sandbox Guards

**Feature**: mcp-e2e-noenvironment-parallelization
**Jira**: ENG-92558
**PRD**: [prd-mcp-e2e-noenvironment-parallelization.md](../prd/prd-mcp-e2e-noenvironment-parallelization.md)
**ADR**: [adr-mcp-e2e-noenvironment-parallelization.md](../adr/adr-mcp-e2e-noenvironment-parallelization.md)
**Status**: in-progress
**Size**: M

---

## As a

clio maintainer

## I want

shared MCP fixture startup to support isolated per-fixture homes and automated Sandbox guard checks

## So that

later parallel NoEnvironment work cannot corrupt shared appsettings and cannot accidentally parallelize Sandbox tests

## Acceptance Criteria

- [ ] `McpContractFixtureBase` allows derived fixtures to add child-process environment overrides before the shared MCP server starts.
- [ ] The base or helper API supports fixture-owned temp directory lifecycle for isolated `CLIO_HOME`/`HOME`/`USERPROFILE` scenarios.
- [ ] The implementation uses `McpE2ESettings.ProcessEnvironmentVariables`; no process-global `Environment.SetEnvironmentVariable` is introduced.
- [ ] `FsmModeToolE2ETests` is class-level `[NonParallelizable]` because it contains a Sandbox test.
- [ ] A meta-test fails if any test categorized `McpE2E.Sandbox` belongs to a fixture without class-level `[NonParallelizable]`.
- [ ] A two-fixture isolation canary proves two parallel fixtures can write the same feature key in separate isolated homes without cross-contamination.

## Implementation Notes

- Prefer protected virtual hooks on `McpContractFixtureBase`, for example:
  - `ConfigureMcpServerSettings(McpE2ESettings settings)`;
  - `OneTimeSetUp` temp-home helper methods;
  - `OneTimeTearDown` cleanup.
- Keep `Arrange()` behavior unchanged for existing subclasses.
- The meta-test can use reflection over the `clio.mcp.e2e` test assembly and NUnit attributes.

## Test Requirements

| ID | Type | Scenario | Expected |
|----|------|----------|----------|
| TC-U-01 | Unit/meta | Sandbox fixture guard scan | every Sandbox test's fixture has `[NonParallelizable]` |
| TC-U-02 | Unit/meta | Known NoEnvironment-only fixture | guard does not require `[NonParallelizable]` |
| TC-E2E-01 | MCP e2e | Two isolated fixtures write same feature key | each fixture sees only its own feature state |

## Definition of Done

- [x] Foundation tests pass.
- [x] No production command/MCP tool contract changes.
- [ ] Story references validation command in PR.

## Dev Agent Record

- Implementation started: 2026-07-01
- Implementation checkpoint: foundation code complete locally; no PR opened yet.
- Files:
  - `clio.mcp.e2e/Support/Mcp/McpContractFixtureBase.cs`
  - `clio.mcp.e2e/FsmModeToolE2ETests.cs`
  - `clio.mcp.e2e/ShowWebAppListToolE2ETests.cs`
  - `clio.mcp.e2e/McpFixturePolicyTests.cs`
  - `clio.mcp.e2e/IsolatedHomeFeatureFlagCanaryE2ETests.cs`
- Validation:
  - `dotnet build clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0`
  - `dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --no-build --filter "FullyQualifiedName~McpFixturePolicyTests"` -> 2 passed
  - `dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --no-build --filter "FullyQualifiedName~IsolatedHomeFeatureFlag"` -> 2 passed
  - `dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --no-build --filter "Category=Unit"` -> 14 passed
- Notes:
  - The new guard found an additional real gap beyond `FsmModeToolE2ETests`: `ShowWebAppListToolE2ETests` also contained Sandbox coverage without class-level `[NonParallelizable]`; both are now guarded.
  - Build still reports pre-existing warnings outside this story (`CLIO005` business-rule DI registrations and nullable warnings in existing e2e files); no new errors.
