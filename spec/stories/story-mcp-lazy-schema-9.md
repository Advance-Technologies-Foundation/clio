# Story 9: Migration + consumer inventory (CAADT / adaclio / e2e)

**Feature**: mcp-lazy-schema
**FR coverage**: ADR Risks ("Breaking consumers", "Migration scale ×74") — inventory prerequisite
**PRD**: _none — spike-driven feature_
**ADR**: [adr-mcp-lazy-schema.md](../adr/adr-mcp-lazy-schema.md)
**Status**: ready-for-dev
**Size**: M (half day) — documents-only inventory; empty code diff (spec/** only)
**Risk**: HIGH — **BLOCKER for flipping any default (Story 10); estimates are unrealistic without it**
**Blocked by**: story-mcp-lazy-schema-0
**Blocks**: story-mcp-lazy-schema-10, story-mcp-lazy-schema-11

---

## As a

clio maintainer about to move 74 commands behind clio-run

## I want

a complete inventory of (a) every long-tail command's MCP/doc/test artifacts and (b) every external consumer that hardcodes a flat long-tail tool name

## So that

the migration (Story 11) and the deprecation aliases (Story 10) are scoped from evidence, and no consumer (CAADT, creatio-adaclio-testing orchestrator, e2e) breaks on a default flip

---

## Acceptance Criteria

- [ ] **AC-01** — Given the ~74 long-tail commands, when inventoried, then a spec doc lists for each: tool file, prompt(s)/resource(s) referencing the tool name, `clio.tests` test(s), `clio.mcp.e2e` test(s), `help/en/<verb>.txt`, `docs/commands/<verb>.md`, `Commands.md` section, `McpCapabilityMap.md` entry.
- [ ] **AC-02** — Given external consumers, when inventoried, then the doc lists every place CAADT, creatio-adaclio-testing orchestrator, and clio's own e2e hardcode a flat long-tail tool name, with repo + path references.
- [ ] **AC-03** — Given the inventory, when complete, then it produces the deprecation-alias list Story 10 consumes (flat name → clio-run proxy) and the per-command checklist Story 11 consumes.
- [ ] **AC-04** — Given Prompts/Resources reference tool names, when inventoried, then every such reference that must change is listed (AGENTS.md: moved command touches Prompts/Resources too).
- [ ] **AC-05** — Given the inventory, when reviewed, then it classifies each command core-vs-long-tail consistently with Story 1's profile config (no command unassigned).
- [ ] **AC-ERR** — Given a consumer that cannot be reached/verified, when inventoried, then it is flagged "unverified — risk" rather than assumed safe.

## Implementation Notes

No production code. Output: `spec/mcp-lazy-schema/mcp-lazy-schema-migration-inventory.md` (per AGENTS.md feature-doc naming).

Sources to grep:
- `clio/Command/McpServer/Tools/*`, `Prompts/*`, `Resources/*` — tool-name references.
- `clio.tests/Command/McpServer/*`, `clio.mcp.e2e/*`.
- `clio/help/en/*.txt`, `clio/docs/commands/*.md`, `clio/Commands.md`, `docs/McpCapabilityMap.md`.
- External: CAADT repo, creatio-adaclio-testing orchestrator (per MEMORY: hardcoded flat tool names).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit | n/a — documents-only | — |
| Integration | n/a | — |
| E2E | n/a | — |

SM = empty code diff (spec/** only).

## Definition of Done

- [ ] Inventory doc covers all ~74 long-tail commands × all 8 artifact types
- [ ] External-consumer inventory complete with repo/path references; unverified ones flagged
- [ ] Deprecation-alias list (Story 10) + per-command migration checklist (Story 11) produced
- [ ] Core-vs-long-tail classification reconciled with Story 1 config
- [ ] PR references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
