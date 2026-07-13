# Story 2: Convert Pure NoEnvironment Fixtures to Shared Server

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

pure NoEnvironment MCP e2e fixtures to share one MCP server per fixture

## So that

the suite stops paying avoidable per-test server startup cost for contract-only checks

## Acceptance Criteria

- [x] `PackageHotfixToolE2ETests` and `AddPackageDependencyToolE2ETests` inherit `McpContractFixtureBase` and no longer call `McpServerSession.StartAsync` per test.
- [x] Eligible pure NoEnvironment fixtures are converted when safe: `DeployCreatioToolE2ETests`, `RestoreDbToolE2ETests`, `DownloadConfigurationToolE2ETests`, `CompileCreatioToolE2ETests`, and any other audited pure contract fixture from the PRD list.
- [x] `OAuthConfigurationToolsE2ETests` is converted only if the isolated-home behavior is preserved through the foundation hook.
- [x] `SkillManagementToolE2ETests` is converted only if its isolated HOME/USERPROFILE behavior and per-test workspace isolation are preserved.
- [x] Process-global environment mutator fixtures remain excluded from this story.
- [x] Test assertions and categories are unchanged.

## Implementation Notes

- Use `await using var context = Arrange(...)` for ordinary shared-server tests.
- Keep per-test temp files/workspaces in the test body or per-test context.
- Do not fold Sandbox or host-infra fixtures into this conversion merely because they have one test.

## Test Requirements

| ID | Type | Scenario | Expected |
|----|------|----------|----------|
| TC-E2E-02 | MCP e2e | Converted NoEnvironment fixture | all tests pass using one shared server |
| TC-U-03 | Static/meta | Converted files | no per-test `McpServerSession.StartAsync` remains in converted fixtures |

## Definition of Done

- [x] Converted fixture list is documented in the story implementation record.
- [x] `McpE2E.NoEnvironment` targeted run passes for converted fixtures.
- [x] No new `[Ignore]` attributes.

## Dev Agent Record

### Implementation Notes

- Converted `PackageHotfixToolE2ETests`, `AddPackageDependencyToolE2ETests`, `CompileCreatioToolE2ETests`, `DeployCreatioToolE2ETests`, `RestoreDbToolE2ETests`, `DownloadConfigurationToolE2ETests`, `OAuthConfigurationToolsE2ETests`, and `SkillManagementToolE2ETests` to shared-server `McpContractFixtureBase`.
- Preserved `OAuthConfigurationToolsE2ETests` feature-flag isolation through `ConfigureMcpServerSettings` and fixture-owned `CLIO_HOME`.
- Preserved `SkillManagementToolE2ETests` isolated home behavior through child-process `HOME` and `USERPROFILE` overrides.
- Preserved `DownloadConfigurationToolE2ETests` per-test workspace cleanup while sharing the MCP server; kept `[AllureNUnit]` omitted because of the existing deadlock note.

### Validation

- `dotnet build clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --no-restore`
- `dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --no-build --filter "FullyQualifiedName~PackageHotfixToolE2ETests|FullyQualifiedName~AddPackageDependencyToolE2ETests|FullyQualifiedName~CompileCreatioToolE2ETests|FullyQualifiedName~DeployCreatioToolE2ETests|FullyQualifiedName~RestoreDbToolE2ETests|FullyQualifiedName~OAuthConfigurationToolsE2ETests|FullyQualifiedName~SkillManagementToolE2ETests"` - 18 passed.
- `dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --no-build --filter "FullyQualifiedName~DownloadConfigurationToolE2ETests"` - 2 passed.
- `rg -n "McpServerSession\.StartAsync|NonParallelizable" <converted-fixtures>` - no matches.
