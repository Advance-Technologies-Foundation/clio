# Story 11: Docs/MCP migration ×74 + tool-budget ratchet test from scratch

**Feature**: mcp-lazy-schema
**FR coverage**: ADR Consequences ("Large doc/MCP/test migration ×74"; "build a tool-budget ratchet from scratch")
**PRD**: _none — spike-driven feature_
**ADR**: [adr-mcp-lazy-schema.md](../adr/adr-mcp-lazy-schema.md)
**Status**: ready-for-dev
**Size**: L (full day; likely split per-batch during execution)
**Risk**: MEDIUM — volume + AGENTS.md mandates 8 artifact types per moved command
**Blocked by**: story-mcp-lazy-schema-9 (per-command checklist), story-mcp-lazy-schema-10 (aliases landed)

---

## As a

clio maintainer finishing the lazy-schema migration

## I want

every migrated long-tail command's docs/MCP artifacts updated per AGENTS.md, plus a from-scratch tool-budget ratchet test that fails if `tools/list` regrows

## So that

the migration is consistent across all surfaces and the token-reduction success metric is enforced going forward (clio `tools/list` ≤ ~5-8k tokens / fits the 128-tool host limit)

---

## Acceptance Criteria

- [ ] **AC-01** — Given the Story 9 per-command checklist, when this story closes, then for every migrated command the following are updated/verified: tool/prompt/resource, `clio.tests`, `clio.mcp.e2e`, `help/en/<verb>.txt`, `docs/commands/<verb>.md`, `Commands.md`, `docs/McpCapabilityMap.md`.
- [ ] **AC-02** — Given prompts/resources that referenced moved tool names, when updated, then they reference the new surface (`clio-run` + `get-tool-contract`), no dangling flat-name references remain (grep-clean).
- [ ] **AC-03** — Given no `McpToolBudgetTests` exists in this branch, when built, then a budget-ratchet test asserts core-profile `tools/list` token/byte size ≤ the ADR target (~5-8k tokens) and FAILS if it regrows past a recorded ceiling.
- [ ] **AC-04** — Given the budget test, when the FULL profile (flag off) is measured, then it is recorded as the documented baseline (not asserted as a ceiling — that path is unchanged-by-design).
- [ ] **AC-05** — Given the success metric reframing, when documented, then the budget test's rationale notes that out-of-scope host/runtime residual (gpt-oss-20b overflow on host system prompt) is NOT what this asserts.
- [ ] **AC-ERR** — Given a future command added without budget consideration, when it pushes core `tools/list` over the ceiling, then the ratchet test fails in CI with a clear message naming the regression.

## Implementation Notes

Key files:
- Per-command artifacts per AGENTS.md "Required MCP targets" + "Required documentation targets" (8 surfaces).
- New `clio.tests/Command/McpServer/McpToolBudgetTests.cs` — build from scratch (NOT #624's; absent here per ADR).
- `docs/McpCapabilityMap.md`, `clio/Commands.md`.
- Use the `document-command`, `create-mcp-tool`, `test-mcp-tool` skills per AGENTS.md.

Pattern: budget test should measure the SAME `tools/list` payload the spike measured (direct stdio), with a recorded ceiling constant + comment linking the ADR target. Resolve verb aliases to canonical names for filenames (AGENTS.md).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | budget ratchet: core profile size ≤ ceiling; FULL baseline recorded; regression message names offender | `clio.tests/Command/McpServer/McpToolBudgetTests.cs` |
| Integration `[Category("Integration")]` | no dangling flat-name references in prompts/resources (grep-style guard) | `clio.tests/Command/McpServer/ToolNameReferenceTests.cs` |
| E2E `[Category("E2E")]` | full discover→describe→run on claude/codex/copilot post-migration (NOT in CI — manual gate) | `clio.mcp.e2e/` |

Test naming + AAA + `because` + `[Description]`.

## Definition of Done

- [ ] All migrated commands' 8 artifact types updated/verified (per-command checklist closed)
- [ ] No CLIO* warnings; no dangling flat-name references (grep-clean, test-guarded)
- [ ] Budget ratchet test built from scratch, green, with documented ceiling + FULL baseline
- [ ] `docs/McpCapabilityMap.md` + `Commands.md` reflect the new surface
- [ ] Used document-command / create-mcp-tool / test-mcp-tool skills (AGENTS.md)
- [ ] Manual MCP e2e on 3 hosts run + recorded (NOT in CI)
- [ ] PR references this story file + Story 9 inventory

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
