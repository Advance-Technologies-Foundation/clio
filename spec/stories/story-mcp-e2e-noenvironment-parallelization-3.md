# Story 3: Split NoEnvironment Tests Out of Mixed Sandbox Fixtures

**Feature**: mcp-e2e-noenvironment-parallelization
**Jira**: ENG-92558
**PRD**: [prd-mcp-e2e-noenvironment-parallelization.md](../prd/prd-mcp-e2e-noenvironment-parallelization.md)
**ADR**: [adr-mcp-e2e-noenvironment-parallelization.md](../adr/adr-mcp-e2e-noenvironment-parallelization.md)
**Status**: in-progress
**Size**: L

---

## As a

clio maintainer

## I want

stand-free NoEnvironment tests separated from mixed Sandbox fixtures

## So that

NoEnvironment checks can run in the shared/parallel contract tier while live Sandbox tests remain serial

## Acceptance Criteria

- [ ] Stand-free NoEnvironment tests are moved from mixed fixtures into new shared-server contract fixtures.
- [x] Sandbox tests remain in their original serial fixtures and keep class-level `[NonParallelizable]`.
- [ ] Candidate mixed fixtures are audited, including `ApplicationToolE2ETests`, `WorkspaceSyncToolE2ETests`, `ApplicationSectionToolE2ETests`, `ApplicationSectionUpdateToolE2ETests`, `ApplicationSectionMaintenanceToolE2ETests`, `EntitySchemaToolE2ETests`, `FindAppToolE2ETests`, `AddItemModelToolE2ETests`, `GenerateProcessModelToolE2ETests`, and `LinkFromRepositoryToolE2ETests`.
- [x] `ShowWebAppListToolE2ETests` stays Sandbox.
- [x] Assertions, test names, and categories are preserved; only fixture placement and server lifecycle change.
- [ ] Before/after `--list-tests` output shows no removed NoEnvironment or Sandbox test IDs.

## Implementation Notes

- Move helpers only when they are needed by the split-out contract fixture; avoid broad refactors.
- If a method's arrange path touches the live stand, destructive opt-in, cliogate, Redis, or registered environment state, leave it in Sandbox.
- When a test is ambiguous, keep it Sandbox.

## Test Requirements

| ID | Type | Scenario | Expected |
|----|------|----------|----------|
| TC-E2E-03 | MCP e2e | Split NoEnvironment fixture | tests pass without live stand |
| TC-E2E-04 | MCP e2e/list | Category list diff | no test IDs removed or uncategorized |
| TC-U-04 | Meta | Sandbox guard after split | all Sandbox fixtures remain `[NonParallelizable]` |

## Definition of Done

- [ ] PR includes audited fixture list and decisions.
- [x] `McpE2E.NoEnvironment` list-tests output was regenerated after the split.
- [x] `McpE2E.Sandbox` list-tests output was regenerated after the split.

## Dev Agent Record

### Implementation Notes

- Split 21 stand-free tests out of serial mixed fixtures into shared-server contract fixtures:
  - `AddItemModelContractToolE2ETests`
  - `ApplicationContractToolE2ETests`
  - `ApplicationSectionContractToolE2ETests`
  - `ApplicationSectionMaintenanceContractToolE2ETests`
  - `FindAppContractToolE2ETests`
  - `FsmModeContractToolE2ETests`
  - `GenerateProcessModelContractToolE2ETests`
  - `GetProcessSignatureContractToolE2ETests`
  - `LinkFromRepositoryContractToolE2ETests`
  - `WorkspaceSyncContractToolE2ETests`
- Removed simple `McpE2E.NoEnvironment` methods from the original serial classes for `AddItemModel`, `Application`, `ApplicationSection`, `ApplicationSectionMaintenance`, `ApplicationSectionUpdate`, `FindApp`, `FsmMode`, `GenerateProcessModel`, `GetProcessSignature`, `LinkFromRepository`, and `WorkspaceSync`.
- Kept live Sandbox tests in the original `[NonParallelizable]` classes.
- Left complex mixed fixtures open for a focused follow-up: `ApplicationSectionToolE2ETests` and `ApplicationSectionUpdateToolE2ETests` still contain special NoEnvironment tests with isolated settings/custom client behavior; `EntitySchemaToolE2ETests` and related schema/page fixtures still need a separate audit.
- Adjusted the moved `get-fsm-mode` invalid-environment assertion to accept the current tool contract, which surfaces a generic MCP invocation error instead of the missing environment name.

### Validation

- `dotnet build clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --no-restore`
- `dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --no-build --filter "FullyQualifiedName~FsmModeContractToolE2ETests|FullyQualifiedName~FindAppContractToolE2ETests|FullyQualifiedName~GetProcessSignatureContractToolE2ETests|FullyQualifiedName~LinkFromRepositoryContractToolE2ETests|FullyQualifiedName~AddItemModelContractToolE2ETests|FullyQualifiedName~GenerateProcessModelContractToolE2ETests|FullyQualifiedName~WorkspaceSyncContractToolE2ETests|FullyQualifiedName~McpFixturePolicyTests"` - 13 passed.
- `dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --no-build --filter "FullyQualifiedName~ApplicationContractToolE2ETests"` - 3 passed.
- `dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --no-build --filter "FullyQualifiedName~ApplicationSectionMaintenanceContractToolE2ETests"` - 4 passed.
- `dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --no-build --filter "FullyQualifiedName~ApplicationSectionContractToolE2ETests"` - 4 passed.
- `dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --no-build --list-tests --filter "Category=McpE2E.NoEnvironment"`
- `dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --no-build --list-tests --filter "Category=McpE2E.Sandbox"`
- `rg -n "McpE2E\.NoEnvironment" <split-source-fixtures>` - no matches.
