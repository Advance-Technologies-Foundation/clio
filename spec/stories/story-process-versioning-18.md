# Story 18: Guidance for saving a new version and rolling back

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-13
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio-knowledge
**Status**: ready-for-dev
**Size**: S

---

## As a

developer (AI agent acting for a no-code builder)

## I want

guidance that tells the agent how to run the save-as-new-version then set-active sequence and what to ask

## So that

the agent asks once, then behaves predictably for the rest of the session

---

## Acceptance Criteria

- [ ] **AC-01** — Given `process-versions`, when it is read, then it states the two-step sequence: `modify-business-process-as-new-version` carries the edits and produces the version, then `set-active-business-process-version` only if the user asked for that
- [ ] **AC-02** — Given the article, when it is read, then it tells the agent to ask once whether edits go to the current version or a new one and whether each new version becomes actual, and to say what it did in every reply
- [ ] **AC-03** — Given the article, when it is read, then it states that the session policy is agent behaviour rather than a request field, so every call is explicit
- [ ] **AC-04** — Given the article, when it is read, then it states that a rejected edit saves nothing — there is no half-created version to clean up — and that deleting a version does not exist
- [ ] **AC-ERR** — Given the article names a tool, when the guide-content test runs, then the name matches the shipped constant

## Implementation Notes

Extends `guidance/mcp/guides/processes/versions.md` from story 6 with the write half. The sequence and its rationale come from ADR choices 3 and 13.
Do NOT describe a separate create step: there is none. One call carries the edits and produces the version; an empty edit list is how an agent takes a plain snapshot.
Also state what rollback does and does not do: only NEW instances; running instances stay on their version; deleting a version does not exist.
`libraryVersion` bump here too; the clio-side re-pin rides story 7's mechanism.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | guide content assertions on contiguous phrases | `automation/Clio.Knowledge.Bundle.Tests/` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [ ] Counted claims in the `process-modeling` routing index stay consistent
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
