# Story 5: inline-contract-on-error for clio-run (self-correct in one round)

**Feature**: mcp-lazy-schema
**FR coverage**: ADR Decision 3 (inline-contract-on-error), Risks ("Model ignores discover→describe→run")
**PRD**: _none — spike-driven feature_
**ADR**: [adr-mcp-lazy-schema.md](../adr/adr-mcp-lazy-schema.md)
**Status**: ready-for-dev
**Size**: M (half day)
**Risk**: MEDIUM — behavioral mitigation for a known failure mode (passive instructions ignored)
**Blocked by**: story-mcp-lazy-schema-4 (clio-run), story-mcp-lazy-schema-6 (curated contract source)

---

## As a

clio MCP client (model) that skipped `get-tool-contract`

## I want

a failed `clio-run` (invalid/missing args) to return the command's full contract inline, not just an error

## So that

I self-correct and compose a valid `clio-run` call in one round, instead of looping on a bare error (passive discover→describe→run instructions are a known failure mode here — ENG-91134)

---

## Acceptance Criteria

- [ ] **AC-01** — Given `clio-run` with missing/invalid args, when it fails binding, then the result contains the **full curated contract** for that command (the same payload `get-tool-contract` returns), in addition to the parse error.
- [ ] **AC-02** — Given a model receives the inline contract, when it retries with corrected args, then the second `clio-run` succeeds (verified by e2e/integration), demonstrating one-round self-correction.
- [ ] **AC-03** — Given the command name itself is unknown, when `clio-run` fails, then the result includes a pointer to the compact index (Story 7) rather than a (non-existent) contract.
- [ ] **AC-04** — Given inline-contract behavior, when a command has only a lossy reflection fallback contract (no curated one), then the response flags the contract as best-effort (drives Story 6 coverage) — it does not present lossy data as authoritative.
- [ ] **AC-ERR** — Given the contract lookup itself fails, when `clio-run` errors, then it degrades gracefully to the parse error + index pointer (never an unhandled exception).

## Implementation Notes

Key files:
- `clio/Command/McpServer/Tools/ClioRunTool.cs` (Story 4) — error path enriched.
- `ToolContractCatalog` / `ToolContractGetTool` (the `get-tool-contract` source, Story 6) — reuse to fetch the inline contract; do not duplicate the lookup.
- `McpToolSchemaCatalog.cs:91-178` — lossy fallback; AC-04 must mark fallback as best-effort.

Pattern: reuse the curated-contract service from Story 6; the inline payload must be the SAME shape `get-tool-contract` returns (one source of truth).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | failed bind returns full curated contract; unknown command → index pointer; fallback flagged best-effort; contract-lookup failure degrades | `clio.tests/Command/McpServer/ClioRunInlineContractTests.cs` |
| Integration `[Category("Integration")]` | fail→inline-contract→retry-succeeds round-trip | `clio.tests/Command/McpServer/ClioRunSelfCorrectTests.cs` |
| E2E `[Category("E2E")]` | claude/codex/copilot self-correct in one round after a bad call (NOT in CI — manual; this is the core Risk mitigation) | `clio.mcp.e2e/` |

Test naming + AAA + `because` + `[Description]`.

## Definition of Done

- [ ] No CLIO* warnings
- [ ] Inline contract reuses the Story 6 curated-contract source (no duplicate lookup)
- [ ] Lossy fallback never presented as authoritative
- [ ] MCP e2e self-correction scenario added (mandatory) — flagged NOT in CI, this is the primary risk gate
- [ ] PR references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
