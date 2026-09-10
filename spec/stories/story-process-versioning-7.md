# Story 7: Re-pin clio's guidance fixture to the published generation

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-13
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio
**Status**: done
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

- [x] **AC-01** — Given the published bundle generation, when `curated-knowledge-names.json` is read, then `libraryVersion` and `sequence` match it and the sequence follows `major*1e9 + minor*1e6 + patch*1e3`
- [x] **AC-02** — Given the fixture, when it is read, then it knows `process-versions` and the process article names introduced by the guidance split
- [x] **AC-03** — Given the drift tests, when they run, then the shipped templates and the ungated tool descriptions resolve every guidance name they mention
- [x] **AC-ERR** — Given a tool description names an article absent from `availableNames`, when the drift test runs, then it fails naming the article

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

- [x] Depends on story 6 having been published — the pin must reference a real generation
- [x] Code compiles without Roslyn analyzer warnings
- [x] All new tool names and flags are kebab-case
- [x] Unit tests use `[Category("Unit")]` — never `[Category("UnitTests")]`
- [x] No `catch (Exception)` added to clio code paths
- [x] Knowledge records whose `applies-to` names a file this story touches are updated or deleted **in this PR** (`AGENTS.md:456-457`)
- [x] Docs verdict stated explicitly in the PR body, including "no update required" where that is the verdict
- [x] MCP verdict stated in the PR body ("MCP reviewed, no update required" where that applies)
- [x] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-09-09
- Implementation completed: 2026-09-09
- Tests passing: `Category=Unit` 11760 passed, 0 failed; the 11 guidance-drift tests green
- Notes:

The fixture is now **derived from the published manifest** rather than hand-curated. Generation
1.14.0 was installed through the stock consumer path - `github-release`, asset
`clio-knowledge-bundle.zip` - and `availableNames` is every `itemId` plus every `topicId` it carries,
minus the two feature-gated names, sorted Ordinal. That makes the oracle reproducible: it cannot claim
a name the library does not publish, which is the failure mode AC-ERR describes.

The result is a strict SUPERSET of the previous 190 names (283), asserted in the generator before
writing, so no name that resolved before can stop resolving and AC-03 cannot regress. libraryVersion
1.13.98 -> 1.14.0 and sequence 1013098000 -> 1014000000, which satisfies the `major*1e9 + minor*1e6 +
patch*1e3` formula AC-01 names.

AC-ERR was mutation-checked rather than assumed: removing `process-modeling` reddens
`UngatedMcpTools_ShouldNameOnlyUngatedGuidance_WhenDirectingAgentsToRead`, and the failure names the
article and all six call sites.

One correction to this story's own Implementation Notes: they say the fixture pins 1.13.54 and knows
three of the nine process article names. It pinned 1.13.98 by the time this ran - an earlier catch-up
had already happened - so the gap was smaller than the note describes. 
