# Test Plan: MCP E2E NoEnvironment Parallelization

- **Feature:** `mcp-e2e-noenvironment-parallelization`
- **Jira:** ENG-92558
- **PRD:** `spec/prd/prd-mcp-e2e-noenvironment-parallelization.md`
- **ADR:** `spec/adr/adr-mcp-e2e-noenvironment-parallelization.md`

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation / test |
|------|------------|--------|-------------------|
| Sandbox test accidentally runs in parallel | Medium | High | TC-U-01, TC-U-04 |
| Shared appsettings/feature flags contaminate parallel fixtures | Medium | High | TC-E2E-01 |
| Converted fixture depends on per-test startup state | Medium | Medium | TC-E2E-02, incremental conversion |
| Test coverage silently changes during split | Medium | High | TC-E2E-04 |
| Worker cap overloads TeamCity agent | Medium | Medium | TC-CI-01, TC-CI-02 |
| Expected timing gain is smaller than planned | Medium | Medium | before/after timing proof |

## Regression Scope

- `clio.mcp.e2e/Support/Mcp/McpContractFixtureBase.cs`
- `clio.mcp.e2e/*ToolE2ETests.cs`
- Any new `clio.mcp.e2e` meta-test files
- `clio.mcp.e2e/*.runsettings`

No command behavior, MCP tool schema, or documentation surface should change. If a command or
tool contract changes during implementation, stop and review the MCP/docs policy separately.

## Test Cases

| ID | Type | Title | Expected |
|----|------|-------|----------|
| TC-U-01 | Unit/meta | `SandboxFixtures_ShouldBeNonParallelizable_WhenTheyContainSandboxTests` | reflection scan fails if any `McpE2E.Sandbox` test's fixture lacks class-level `[NonParallelizable]` |
| TC-U-02 | Unit/meta | `SandboxFixtureGuard_ShouldIgnoreNoEnvironmentOnlyFixtures` | NoEnvironment-only fixtures are not required to be `[NonParallelizable]` |
| TC-U-03 | Static/meta | `ConvertedFixtures_ShouldNotStartMcpServerPerTest` | converted pure fixtures do not call `McpServerSession.StartAsync` from per-test arrange helpers |
| TC-U-04 | Unit/meta | `SandboxFixtureGuard_ShouldPass_AfterMixedFixtureSplit` | guard passes after split work |
| TC-E2E-01 | MCP e2e | `IsolatedHome_ShouldPreventFeatureFlagCrossContamination_WhenTwoFixturesUseSameKey` | each fixture sees only its own feature state |
| TC-E2E-02 | MCP e2e | Converted pure NoEnvironment fixtures | pass with shared fixture server |
| TC-E2E-03 | MCP e2e | Split NoEnvironment contract fixtures | pass without live stand |
| TC-E2E-04 | List-tests | NoEnvironment and Sandbox before/after diff | no removed test IDs and no uncategorized drift |
| TC-E2E-05 | MCP e2e | NoEnvironment filter with runsettings worker cap | pass with zero unexpected skips |
| TC-CI-01 | TeamCity | full `Team_Atf_ClioMcpE2eTests` run #1 | green; pass/ignore count and wall-clock recorded |
| TC-CI-02 | TeamCity | full `Team_Atf_ClioMcpE2eTests` run #2 | green; pass/ignore count and wall-clock recorded |

## Commands

List-tests gates:

```bash
dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --list-tests \
  --filter "Category=McpE2E.NoEnvironment"

dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --list-tests \
  --filter "Category=McpE2E.Sandbox"
```

NoEnvironment validation:

```bash
dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 \
  --filter "Category=McpE2E.NoEnvironment" \
  --settings clio.mcp.e2e/noenvironment.runsettings
```

## Notes

- MCP e2e is TeamCity/live-stand dependent for the full final proof.
- A local NoEnvironment run is still useful but does not replace two TeamCity full-suite runs.
- Treat `Skipped > 0` in the NoEnvironment filter as a signal to inspect classification; expected legacy skips must be explicitly documented.
