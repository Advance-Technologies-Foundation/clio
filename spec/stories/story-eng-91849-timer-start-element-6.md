# Story 6: Re-pin the knowledge, review docs and record the change-summary statements

**Feature**: eng-91849-timer-start-element
**Jira**: ENG-91849 Timer start element (start by time / schedule) (Story; epic ENG-92704 Create BP via AI Toolkit)
**Spec**: [spec-eng-91849-timer-start-element.md](../prd/spec-eng-91849-timer-start-element.md) - R10
**ADR**: [adr-eng-91849-timer-start-element.md](../adr/adr-eng-91849-timer-start-element.md) - D7
**Test plan**: [tp-eng-91849-timer-start-element.md](../test-plans/tp-eng-91849-timer-start-element.md)
**Status**: in-progress
**Repo**: clio (same PR as story 1)
**Depends on**: the clio-knowledge PR (the `process-timer-start` guide with the confirm-before-save gate, its routing
entry, the read-only sweep) merged and released
**Blocks**: [story 4](story-eng-91849-timer-start-element-4.md) (the guide pointer)

---

## As a

clio maintainer and reviewer

## I want

clio pinned to the knowledge generation that publishes the timer guide, the docs and the knowledge records reviewed,
and the PR to state what was reviewed

## So that

the tool pointers name a guide that exists, and the reviewer can audit the docs, MCP and ClioRing reviews

---

## Acceptance Criteria

- AC1: `clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json` is re-pinned to the clio-knowledge release
  that carries `process-timer-start`; the guidance-name oracle and `WorkspaceTemplateGuidanceDriftTests` pass.
- AC2: `docs/McpCapabilityMap.md` lists the timer start and `setTimer`.
- AC3: `make check-knowledge` is run; every record whose `applies-to` lists a changed file is updated or deleted; a new
  record is written only for a fact the code does not say (for example: a timer job is keyed by element UId and its
  group is shared by every version of the family).
- AC4: the PR description states "docs reviewed, no update required" for the CLI help and command docs (the changed
  verbs are MCP-only), "MCP reviewed" with the list of touched tools and prompts, "ClioRing compatibility reviewed, no
  Ring-consumed contract changed" with the inspected paths (`clio-ring/ClioRing.Ipc`, `clio-ring/ClioRing`,
  `clio-ring/ClioRing.Desktop/actions.json`), and that telemetry is not touched.

## Definition of Done

- [ ] MERGE GATE: knowledge PR merged and released; re-pin committed. The clio PR does not leave draft or merge
  before it (story 4's guide pointer depends on it).
- [ ] Guidance-name oracle and `WorkspaceTemplateGuidanceDriftTests` green.
- [ ] `docs/McpCapabilityMap.md` updated.
- [ ] `make check-knowledge` report reviewed; records updated in the same PR.
- [ ] The four statements of AC4 in the PR description.
