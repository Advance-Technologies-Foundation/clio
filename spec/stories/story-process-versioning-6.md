# Story 6: Publish the process-versions guidance article

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-13
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio-knowledge
**Status**: ready-for-dev
**Size**: M

---

## As a

developer (AI agent acting for a no-code builder)

## I want

guidance that explains the version model and what this build can and cannot do

## So that

the agent stops inventing version semantics and asks the right question before editing a live process

---

## Acceptance Criteria

- [ ] **AC-01** — Given the guidance bundle, when `process-versions` is requested, then it states: the family is flat, exactly one version is actual, running instances stay on the version they started on, rollback affects only new runs, and deleting a version does not exist
- [ ] **AC-02** — Given the article, when it is read, then it states that versionhood cannot be inferred from a schema name
- [ ] **AC-03** — Given `process-modeling`, when its routing index is read, then it lists `process-versions` and its article counts are correct
- [ ] **AC-04** — Given the bundle source, when the release check runs, then `libraryVersion` has moved and the derived sequence follows
- [ ] **AC-ERR** — Given the article names a tool, when the guide-content test runs, then the name matches the shipped constant exactly

## Implementation Notes

New file: `guidance/mcp/guides/processes/versions.md` → item id `process-versions` (file `naming.md` = item `process-naming`).
Registration is three places in `bundle-source.json`: the `itemIds` list (~`:93`), the `docs://knowledge/com.creatio.clio/<item>` list (~`:228`), and the full item block (~`:1223`). Plus `libraryVersion`.
There is no `sequence` field — the build derives it (`CONTRIBUTING.md:79-82`, command at `:97`).
The `process-modeling` item description counts the articles ("the six articles", "each of the seven") — both numbers move here.
Sized to be read whole through `get-guidance`, like the other six.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | guide content assertions on contiguous phrases (line wraps break longer assertions) | `automation/Clio.Knowledge.Bundle.Tests/` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [ ] The clio-side fixture re-pin is story 7, not this PR — this PR names it as the follow-up
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
