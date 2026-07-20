# Story 7: Compact command index (list-clio-commands) + migrate anti-patterns/flow-hints

**Feature**: mcp-lazy-schema
**FR coverage**: ADR Decision 3-index ("Compact index"), Decision 4 (index carries anti-patterns/flow-hints)
**PRD**: _none — spike-driven feature_
**ADR**: [adr-mcp-lazy-schema.md](../adr/adr-mcp-lazy-schema.md)
**Status**: ready-for-dev
**Size**: M (half day)
**Risk**: MEDIUM — must not lose flow-hints that today live in heavy descriptions
**Blocked by**: story-mcp-lazy-schema-0; consumes the migration list from story-mcp-lazy-schema-2

---

## As a

clio MCP client (model) on the reduced core profile

## I want

a compact `list-clio-commands` index (or reuse `get-guidance`) — command names + one-line summaries by category, plus the critical anti-patterns and flow-hints

## So that

discoverability and the guidance that used to live in heavy tool descriptions are preserved on the core path, supporting discover→describe→run

---

## Acceptance Criteria

- [ ] **AC-01** — Given the reduced core profile, when `tools/list` is requested, then it includes a compact index tool returning command names + one-line summaries grouped by category — and the index itself is small (does not reintroduce the schema bulk it replaces).
- [ ] **AC-02** — Given the anti-patterns/flow-hints removed from heavy descriptions in Story 2, when this story closes, then each one is present in the index/guidance output (no guidance lost — cross-checked against Story 2's migration list).
- [ ] **AC-03** — Given an existing `get-guidance` surface, when evaluated, then the story records reuse-vs-new-tool and does not duplicate guidance across two surfaces.
- [ ] **AC-04** — Given a long-tail command in the index, when a model wants to call it, then the index entry points to `get-tool-contract` + `clio-run` (reinforces the pattern).
- [ ] **AC-05** — Given the index by category, when a new long-tail command is added, then it appears in the index automatically (or a test guards manual omission).
- [ ] **AC-ERR** — Given an invalid category filter (if supported), when requested, then `Error: unknown category 'X'` + the valid set, non-success.

## Implementation Notes

Key files:
- New `list-clio-commands` tool OR extend `get-guidance` — decide in AC-03, gated by Story 1 feature-key.
- Story 2 migration list — the authoritative set of anti-patterns/flow-hints to host here.
- `ServerInstructions` — references the discover→describe→run pattern; keep aligned.

Pattern: reuse `ICommandOptionsRegistry` (Story 3) for the command/category enumeration to avoid a second hand-maintained list.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | index lists all commands by category; every Story-2-migrated hint present; long-tail entries point to get-tool-contract/clio-run; no duplication with get-guidance | `clio.tests/Command/McpServer/CommandIndexTests.cs` |
| Integration | n/a | — |
| E2E `[Category("E2E")]` | model uses index→get-tool-contract→clio-run on 3 hosts (NOT in CI — manual) | `clio.mcp.e2e/` |

Test naming + AAA + `because` + `[Description]`.

## Definition of Done

- [ ] No CLIO* warnings; index tool gated by Story 1 feature-key (CLI + MCP attribute)
- [ ] Story 2 migrated hints fully accounted for (cross-checked, test-guarded)
- [ ] Index stays small (budget test, Story 11, not regressed)
- [ ] MCP e2e added (mandatory) — flagged NOT in CI
- [ ] Docs: `docs/McpCapabilityMap.md` updated for the new index tool
- [ ] PR references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
