# Story 2: Slim core-tool descriptions + dedup environment-name parameter

**Feature**: mcp-lazy-schema
**FR coverage**: ADR Alternative A ("Description slimming only"), "adopted as the complementary first step"
**PRD**: _none — spike-driven feature_
**ADR**: [adr-mcp-lazy-schema.md](../adr/adr-mcp-lazy-schema.md)
**Status**: ready-for-dev
**Size**: M (half day)
**Risk**: LOW — no protocol change, no new types; description text + shared param only
**Blocked by**: story-mcp-lazy-schema-0 (only to confirm scope; technically independent of gating)

---

## As a

clio MCP server author

## I want

the verbose inline-instruction descriptions on the core flat tools trimmed and the `environment-name` parameter description deduplicated (it is repeated 184×)

## So that

clio's `tools/list` shrinks ~14-16k tokens as a low-risk standalone win before any executor work, and the flat core itself is smaller

---

## Acceptance Criteria

- [ ] **AC-01** — Given the `environment-name` description repeated ~184× across tool schemas, when this story lands, then the description is sourced from one shared constant/reference, not re-inlined per tool, and `tools/list` byte count drops measurably.
- [ ] **AC-02** — Given heavy descriptions (`sync-schemas` 14.4k, `create-entity-business-rule` 12.5k, `update-page` 11.2k), when slimmed, then inline procedural instructions move to the index/`ServerInstructions` (Story 7) or are removed, and each tool keeps a one-line purpose + arg docs.
- [ ] **AC-03** — Given slimming must not lose semantics, when a description is trimmed, then no anti-pattern / flow-hint is silently dropped — each is either kept inline if load-bearing or migrated (tracked, handed to Story 7).
- [ ] **AC-04** — Given the budget ratchet (Story 11), when this story lands, then the recorded `tools/list` baseline is updated to the new smaller number.
- [ ] **AC-ERR** — Given a tool whose behavior depends on description text being parsed by a client, when verified, then no such dependency exists (descriptions are advisory) — documented in the PR.

## Implementation Notes

Key files:
- `clio/Command/McpServer/Tools/*.cs` — `[Description(...)]` on tool methods/args.
- `environment-name` shared description — introduce one constant (e.g. in a `McpToolDescriptions` static) and reference it everywhere.
- Heaviest tools named in ADR Context table.

Pattern: no hardcoded user-facing strings scattered — use a constant (project-context.md Code Style). This is a pure-text refactor; arg contracts (names, required, enums) MUST NOT change.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | `environment-name` description resolves to the single shared constant on a sample of tools; arg names/required unchanged | `clio.tests/Command/McpServer/CoreToolDescriptionTests.cs` |
| Integration | n/a | — |
| E2E `[Category("E2E")]` | `tools/list` byte delta vs pre-slim baseline (NOT in CI — manual) | `clio.mcp.e2e/` |

Test naming + AAA + `because` + `[Description]` per policy.

## Definition of Done

- [ ] No CLIO* warnings; descriptions via constants (no scattered literals)
- [ ] Arg contracts (names/required/enums) unchanged — only description text
- [ ] Anti-patterns/flow-hints accounted for (kept or handed to Story 7 with a list)
- [ ] Budget baseline updated
- [ ] Docs/MCP review: per AGENTS.md, confirm `docs/commands/*` + `help/en/*` unaffected (description-only) — state "docs reviewed, no update required" if so
- [ ] PR references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
