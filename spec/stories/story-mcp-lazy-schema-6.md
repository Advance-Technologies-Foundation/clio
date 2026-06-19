# Story 6: Curated get-tool-contract coverage for the full long tail

**Feature**: mcp-lazy-schema
**FR coverage**: ADR Decision 2 (`get-tool-contract` = lazy schema; curated contracts mandatory for long tail)
**PRD**: _none — spike-driven feature_
**ADR**: [adr-mcp-lazy-schema.md](../adr/adr-mcp-lazy-schema.md)
**Status**: ready-for-dev
**Size**: L (full day) — high-count, mechanical-but-broad
**Risk**: MEDIUM — volume risk; the lossy fallback makes "skip a few" tempting and wrong
**Blocked by**: story-mcp-lazy-schema-0

---

## As a

clio MCP client (model)

## I want

every long-tail command to have a **curated** tool contract returned by `get-tool-contract(command)`, not the lossy reflection fallback

## So that

on-demand schemas are accurate (correct enums, nested args, required flags) and `clio-run` (Stories 4/5) has a trustworthy contract source

---

## Acceptance Criteria

- [ ] **AC-01** — Given the current ~46 curated contracts (`CanonicalToolNames`) vs ~124 commands, when this story closes, then every long-tail command (not in the flat core) has a curated contract entry.
- [ ] **AC-02** — Given a curated contract, when compared to the lossy fallback (`McpToolSchemaCatalog.cs:91-178`, first-param-only, enum→"string", nested→"object", required only from `[Required]`), then the curated entry corrects each lossy aspect: all params, real enum values, nested structure, `Required=true` from `[Option(Required=true)]`.
- [ ] **AC-03** — Given any long-tail command, when `get-tool-contract(command)` is called, then it returns the curated contract and NOT the reflection fallback (fallback is a last-resort safety net only).
- [ ] **AC-04** — Given a new long-tail command added later, when no curated contract exists, then a test fails (coverage guard), preventing silent fallback regressions.
- [ ] **AC-05** — Given contract field shapes, when used by Story 5 inline-on-error, then the payload shape is identical (one source of truth).
- [ ] **AC-ERR** — Given an unknown command passed to `get-tool-contract`, when looked up, then it returns `Error: unknown command 'X'` + index pointer, exits/returns non-success.

## Implementation Notes

Key files:
- `clio/Command/McpServer/.../ToolContractCatalog` + `ToolContractGetTool` — curated source + the tool.
- `CanonicalToolNames` (~46 entries) — extend to full long-tail coverage.
- `McpToolSchemaCatalog.cs:91-178` — the lossy fallback; document why it stays only as a net.

Approach: drive curated entries from `[Option]`/`[Value]`/`[Verb]` metadata where faithful, hand-correcting enums/nested/required. A coverage test (AC-04) enumerates long-tail commands and asserts each has a curated entry.

Pattern: no scattered literals; contract data centralized. DI for any new service.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | every long-tail command has a curated contract (coverage guard); curated corrects enum/nested/required vs fallback on sampled commands; unknown→error | `clio.tests/Command/McpServer/ToolContractCoverageTests.cs` |
| Integration | n/a (pure metadata) | — |
| E2E `[Category("E2E")]` | `get-tool-contract` returns curated (not fallback) for sampled long-tail commands over stdio (NOT in CI — manual) | `clio.mcp.e2e/` |

Test naming + AAA + `because` + `[Description]`.

## Definition of Done

- [ ] No CLIO* warnings
- [ ] Coverage guard test (AC-04) is green and would fail on a new uncovered command
- [ ] Curated contracts demonstrably correct the lossy fallback (sampled assertions)
- [ ] Contract payload shape matches Story 5 inline usage
- [ ] MCP e2e added (mandatory) — flagged NOT in CI
- [ ] PR references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
