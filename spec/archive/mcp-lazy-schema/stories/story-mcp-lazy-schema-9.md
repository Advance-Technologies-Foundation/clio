# Story 9: Migration + consumer inventory (CAADT / adaclio / e2e)

**Feature**: mcp-lazy-schema
**FR coverage**: ADR Risks ("Breaking consumers", "Migration scale ×74") — inventory prerequisite
**PRD**: _none — spike-driven feature_
**ADR**: [adr-mcp-lazy-schema.md](../adr/adr-mcp-lazy-schema.md)
**Status**: review
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

- Implementation started: 2026-06-19
- Implementation completed: 2026-06-19
- Tests passing: n/a — documents-only (empty code diff; spec/** only)
- Notes:
  - Output: [spec/mcp-lazy-schema/mcp-lazy-schema-migration-inventory.md](../mcp-lazy-schema/mcp-lazy-schema-migration-inventory.md).
    All counts extracted from the spike branch @ `f594e913` (no invented data); the
    extraction method is in the doc's Appendix.
  - **Real numbers (corrects ADR estimates):**
    - Total MCP tools = **126** (ADR's "~124" was approximate). 39 ReadOnly, 62 Destructive.
    - Proposed flat **core = 20** (Story 7 finalises); **long-tail = 106** → 44 safe
      (`clio-run`), 62 destructive (`clio-run-destructive`).
    - Curated contracts = **61** in `ToolContractCatalog.Contracts` (47 in the default
      `CanonicalToolNames` — that is the ADR's "~46"); **63** non-executor tools are
      **uncovered** (Story 6 gap), including 3 that are in the proposed core
      (`find-app`, `list-environments`, `list-packages`).
  - **AC-01** — §1/§3: full 126-tool catalog with flags + per-command 8-artifact matrix +
    repo-wide artifact denominators; Story 11 migration estimate ≈ 300–400 file edits.
  - **AC-02** — §4: consumer references across CAADT, adaclio, clio e2e with repo+path; one
    vendored-pyyaml false positive excluded. adaclio allow-lists by `mcp__clio` prefix (not
    per-tool) so its allow-list survives; e2e/unit `CallToolAsync("<flat-name>")` are hard
    bindings that break on the move.
  - **AC-03** — §5 (104-row alias list, 75 consumer-backed) + §3 (per-command checklist).
  - **AC-04** — §3: Prompts (~19 files / ≥24 long-tail names) and Resources (28 files)
    that reference moved tool names are inventoried.
  - **AC-05** — §1 `Tier` column assigns every tool to core/long-tail (none unassigned);
    Story 1's profile config remains authoritative.
  - **AC-ERR** — §4: all three named repos present and grepped (no unverified entries);
    integrations outside the three named repos flagged as residual/unverified risk.
  - MCP review: documents-only spec change; no MCP tool/prompt/resource source touched ⇒
    **MCP reviewed, no update required**. Docs: no command behavior changed ⇒
    **docs reviewed, no update required**.
