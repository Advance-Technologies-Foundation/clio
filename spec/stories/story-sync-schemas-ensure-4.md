# Story 4: contract, guidance, and docs for convergent semantics

**Feature**: sync-schemas-ensure-semantics
**ADR unit**: U4
**FR coverage**: FR-07 (SM-04, SM-04c)
**PRD**: [prd-sync-schemas-ensure-semantics.md](../prd/prd-sync-schemas-ensure-semantics.md)
**ADR**: [adr-sync-schemas-ensure-semantics.md](../adr/adr-sync-schemas-ensure-semantics.md)
**Jira**: ENG-93807
**Status**: ready-for-dev
**Size**: M (half day)

---

## As a

developer / AI agent reading the `sync-schemas` tool contract and guidance

## I want

the tool contract, tool `[Description]`, the four guidance resources, docs, and the capability map to
describe the convergent superset semantics, the `outcome` discriminator, the collision failure, and
the `Name`-keyed seed-data contract

## So that

no guidance still instructs me to hand-compose a catch-up batch — re-submitting the identical batch is
documented as the safe recovery path

---

## Acceptance Criteria

- [ ] **AC-CONTRACT** — Given `ToolContractGetTool.BuildSchemaSync()`, when it is read, then it describes the convergent superset (`create-lookup`/`update-entity`), the additive `outcome` values (`created`/`reconciled`/`already-satisfied`/`collision`), the collision failure shape, and the `Name`-keyed seed-data contract.
- [ ] **AC-DESC** — The `SchemaSyncTool` `[Description]` states re-run safety (a completed batch is safe to re-submit; no hand-composed catch-up batch needed).
- [ ] **AC-GUIDANCE (SM-04c)** — Given the four sync-schemas-touching guidance resources, when reviewed, then none instructs the agent to hand-compose a catch-up batch for the convergent ops; the safe recovery is "re-submit the identical batch".
- [ ] **AC-DOCS** — `docs/commands/sync-schemas.md` and `docs/McpCapabilityMap.md` document convergent semantics, `outcome` values, the collision failure, and the seed-data `Name` contract.
- [ ] **AC-TPL** — `clio/tpl/**` (shipped `AGENTS.md`/`CLAUDE.md`) carry no catch-up-batch language for the convergent ops; recorded as "checked, no catch-up guidance in tpl/**" in the PR. `WorkspaceTemplateGuidanceDriftTests` stays green (no rename → no drift expected).

## Implementation Notes

No operation-type rename (OQ-03 → keep `create-lookup` / `update-entity`); semantics are surfaced via
the additive `outcome` discriminator, so there is zero `McpToolCompatibilityCatalog` /
`WorkspaceTemplateGuidanceDriftTests` churn.

Files to modify:
- `clio/Command/McpServer/Tools/ToolContractGetTool.cs` — `BuildSchemaSync()` (~line 3402).
- `clio/Command/McpServer/Tools/SchemaSyncTool.cs` — tool `[Description]`.
- Guidance resources: `AppModelingGuidanceResource.cs`, `ExistingAppMaintenanceGuidanceResource.cs`,
  `DataBindingsGuidanceResource.cs`, `AgentExecutionGuidanceResource.cs` — rewrite catch-up guidance to
  "re-submit the identical batch is safe"; remove hand-composed catch-up instructions (SM-04c); state
  the `Name`-keyed seed contract.
- `docs/commands/sync-schemas.md`, `docs/McpCapabilityMap.md`.
- No CLI-help change (`Commands.md`, `clio/help/en/*.txt`, `WikiAnchors.txt`) — MCP-only tool, no CLI
  verb; record "docs reviewed, no CLI-help change required" in the PR.

Depends on Stories 1, 2, 3 (documents the shipped semantics and the resolved seed decision).
Pattern to follow: existing `BuildSchemaSync` structure and the four guidance resources updated by #910.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | `WorkspaceTemplateGuidanceDriftTests` stays green (resident-or-bridged oracle; no rename) | `clio.tests/Command/McpServer/WorkspaceTemplateGuidanceDriftTests.cs` |
| E2E `[Category("E2E")]` | Contract-text assertion of updated `BuildSchemaSync` — implemented in Story 5 (`ToolContractGetToolE2ETests`, NOT in CI) | `clio.mcp.e2e/` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`; `[Category("Unit")]`; `[Description(...)]`; AAA + `because`.

## Definition of Done

- [ ] `BuildSchemaSync`, tool `[Description]`, four guidance resources, `docs/commands/sync-schemas.md`, `docs/McpCapabilityMap.md` all updated.
- [ ] SM-04c verified: no guidance instructs a hand-composed catch-up batch for the convergent ops.
- [ ] `clio/tpl/**` checked; result recorded in the PR; `WorkspaceTemplateGuidanceDriftTests` green.
- [ ] MCP reviewed: contract/guidance aligned with the current tool behavior; "docs reviewed, no CLI-help change required" recorded (MCP-only tool).
- [ ] Code compiles without Roslyn analyzer warnings (CLIO001–CLIO005).
- [ ] PR description references this story file.

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
