# Story 8: [CONDITIONAL] Docs + MCP Surface + E2E Coverage + Manual E2E Run (Phase B)

**Feature**: read-column-default-values
**FR coverage**: FR-07 (conditional-Must: mandatory together with any FR-05/FR-06 implementation per AGENTS.md docs + MCP maintenance policies)
**PRD**: [prd-read-column-default-values.md](../prd/prd-read-column-default-values.md)
**ADR**: [adr-read-column-default-values.md](../adr/adr-read-column-default-values.md)
**Jira**: ENG-91318 (epic ENG-85256)
**Status**: deferred — **GATED**: do not start until stories 6 and 7 are implemented (which themselves gate on story 4 confirming the gap). If Phase B is struck by story 4, strike this story with evidence.
**Size**: M (half day)
**Phase**: B — conditional implementation
**Depends on**: story-read-column-default-values-6, story-read-column-default-values-7

---

## As a

AI no-code agent integrator (and any clio user reading the docs)

## I want

the lookup-default usage pattern — enrichment fields, honest markers, write-side validation error, TOCTOU caveat, and "where the agent gets the GUID" — documented across CLI help, GitHub docs, and the MCP tool/prompt/contract surface, with E2E coverage proving the contract

## So that

agents and developers discover the machine-verifiable readback contract without reading clio source, and the MCP surface never drifts from command behavior

---

## Acceptance Criteria

- [ ] **AC-01 (docs)** — Given the Phase B behavior from stories 6–7, when docs are
  reviewed, then all of these are updated and mutually consistent:
  `clio/docs/commands/get-entity-schema-column-properties.md`,
  `clio/docs/commands/modify-entity-schema-column.md`,
  `clio/help/en/get-entity-schema-column-properties.txt`,
  `clio/help/en/modify-entity-schema-column.txt`, `clio/Commands.md` — covering
  `display-value` / `record-resolution` semantics, both marker meanings (verbatim
  honest semantics), the DRAFT-AC-06 error message, and the TOCTOU caveat.
- [ ] **AC-02 (MCP surface)** — Given the changed commands, when the MCP surface is
  reviewed, then `clio/Command/McpServer/Tools/EntitySchemaTool.cs` (tool
  descriptions for both verbs), `clio/Command/McpServer/Prompts/EntitySchemaPrompt.cs`
  (lookup-default usage pattern incl. the FR-03 step 2 "where does the agent get
  the GUID" answer), and `clio/Command/McpServer/Tools/ToolContractGetTool.cs`
  (extended `default-value-config` readback contract) are aligned with actual
  behavior.
- [ ] **AC-03 (E2E coverage)** — Given `clio.mcp.e2e/EntitySchemaToolE2ETests.cs`,
  when reviewed, then `[Category("E2E")]` scenarios exist for: lookup-`Const`
  readback with display value; readback degrading to a marker; write rejection for
  a nonexistent GUID; the empty-just-created-lookup edge (DRAFT-AC-06 caveat) —
  mirroring the FR-03 six-step scenario incl. the runtime-default application
  analogue (step 6).
- [ ] **AC-04 (manual E2E run — SM-01 Phase B counter)** — Given that MCP E2E is
  **NOT in CI**, when the Phase B PR is prepared, then the full
  `clio.mcp.e2e` entity-schema suite has been run **manually against a real
  instance** before merge and the run result is recorded in the PR description.
- [ ] **AC-ERR (review statements)** — Given the PR description, when reviewed,
  then it contains the explicit "docs reviewed" and "MCP reviewed" statements
  (or the corresponding update lists) required by AGENTS.md, and references this
  story file.

## Implementation Notes

Use the repo skills: `$document-command` for the docs targets,
`$create-mcp-tool` / `$test-mcp-tool` for the MCP tool/prompt/contract and E2E
work (AGENTS.md preferred invocation).

Files (from the ADR Phase B plan):

- Docs: `clio/docs/commands/get-entity-schema-column-properties.md`,
  `clio/docs/commands/modify-entity-schema-column.md`,
  `clio/help/en/get-entity-schema-column-properties.txt`,
  `clio/help/en/modify-entity-schema-column.txt`, `clio/Commands.md`
- MCP: `clio/Command/McpServer/Tools/EntitySchemaTool.cs`,
  `clio/Command/McpServer/Prompts/EntitySchemaPrompt.cs`,
  `clio/Command/McpServer/Tools/ToolContractGetTool.cs`
- MCP unit coverage: `clio.tests/Command/McpServer/*` (mapping-only coverage is
  insufficient per AGENTS.md — but tool-description/contract tests live here)
- E2E: `clio.mcp.e2e/EntitySchemaToolE2ETests.cs`
- Capability map: review `docs/McpCapabilityMap.md` for the
  `get-entity-schema-column-properties` / `modify-entity-schema-column` rows

Marker semantics and the error string must be copied verbatim from the
implementation constants (stories 6–7), not paraphrased — markers must not claim
more than the query proves.

Key file: `clio/Command/McpServer/Tools/EntitySchemaTool.cs`
Pattern to follow: existing entity-schema tool/prompt/contract trio + `clio.mcp.e2e` suite structure

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | MCP tool descriptions/contract advertise the extended `default-value-config` (kebab-case property names); prompt guidance present | `clio.tests/Command/McpServer/EntitySchemaToolTests.cs` (or existing fixture) |
| Integration `[Category("Integration")]` | n/a | — |
| E2E `[Category("E2E")]` | DRAFT-AC-05/06 full scenarios per AC-03 — **manual only, NOT in CI**; must be run before merge and recorded in the PR | `clio.mcp.e2e/EntitySchemaToolE2ETests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`; AAA + `because` + `[Description]`.
Pre-commit filter: `dotnet test --filter "Category=Unit&(Module=Command|Module=McpServer)"`.

## Definition of Done

- [ ] Gate evidence linked: stories 6 and 7 merged (or merged-in-same-PR) with their DoD complete
- [ ] All five doc targets updated and consistent with shipped behavior
- [ ] MCP tool + prompt + `get-tool-contract` aligned; `docs/McpCapabilityMap.md` reviewed
- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004)
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] E2E scenarios added with `[Category("E2E")]`; manual run executed and recorded in the PR (SM-01 Phase B counter)
- [ ] "docs reviewed" / "MCP reviewed" statements in the PR description
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
