# Story 7: Re-pin clio's guidance fixture to the published generation

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-13
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio
**Status**: ready-for-dev
**Size**: S

---

## As a

developer

## I want

clio's curated guidance names to match the published bundle

## So that

publishing the new article does not leave a red drift test in clio

---

## Acceptance Criteria

- [ ] **AC-01** — Given the published bundle generation, when `curated-knowledge-names.json` is read, then `libraryVersion` and `sequence` match it and the sequence follows `major*1e9 + minor*1e6 + patch*1e3`
- [ ] **AC-02** — Given the fixture, when it is read, then it knows `process-versions` and the process article names introduced by the guidance split
- [ ] **AC-03** — Given the drift tests, when they run, then the shipped templates and the ungated tool descriptions resolve every guidance name they mention
- [ ] **AC-ERR** — Given a tool description names an article absent from `availableNames`, when the drift test runs, then it fails naming the article

## Implementation Notes

File: `clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json` (`libraryVersion` `:3`, `sequence` `:4`, `availableNames` from `:5`, `featureGatedNames` `:161-164`).
The fixture currently pins 1.13.54 and knows three of the nine process article names, so this is a catch-up on the guidance split, not a routine bump.
Oracle: `WorkspaceTemplateGuidanceDriftTests.cs:279` (formula), `:503` (shipped templates), `:537` (ungated tools).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | generation formula, name presence, drift over shipped templates | `clio.tests/Command/McpServer/WorkspaceTemplateGuidanceDriftTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [ ] Depends on story 6 having been published — the pin must reference a real generation
- [ ] Code compiles without Roslyn analyzer warnings
- [ ] All new tool names and flags are kebab-case
- [ ] Unit tests use `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] No `catch (Exception)` added to clio code paths
- [ ] Knowledge records whose `applies-to` names a file this story touches are updated or deleted **in this PR** (`AGENTS.md:456-457`)
- [ ] Docs verdict stated explicitly in the PR body, including "no update required" where that is the verdict
- [ ] MCP verdict stated in the PR body ("MCP reviewed, no update required" where that applies)
- [ ] PR description references this story file

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started: 
- Implementation completed: 
- Tests passing: 
- Notes: 
