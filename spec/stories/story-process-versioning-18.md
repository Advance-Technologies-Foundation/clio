# Story 18: Guidance for saving a new version and rolling back

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-13
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio-knowledge
**Status**: review
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

- [x] **AC-01** — Given `process-versions`, when it is read, then it states the two-step sequence: `modify-business-process-as-new-version` carries the edits and produces the version, then `set-active-business-process-version` only if the user asked for that
- [x] **AC-02** — Given the article, when it is read, then it tells the agent to ask once whether edits go to the current version or a new one and whether each new version becomes actual, and to say what it did in every reply
- [x] **AC-03** — Given the article, when it is read, then it states that the session policy is agent behaviour rather than a request field, so every call is explicit
- [x] **AC-04** — Given the article, when it is read, then it states that a rejected edit saves nothing — there is no half-created version to clean up — and that deleting a version does not exist
- [x] **AC-ERR** — Given the article names a tool, when the guide-content test runs, then the name matches the shipped constant

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

- [x] Counted claims in the `process-modeling` routing index stay consistent
- [x] Code compiles without Roslyn analyzer warnings
- [ ] All new tool names and flags are kebab-case
- [ ] Unit tests use `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] No `catch (Exception)` added to clio code paths
- [ ] Knowledge records whose `applies-to` names a file this story touches are updated or deleted **in this PR** (`AGENTS.md:456-457`)
- [ ] Docs verdict stated explicitly in the PR body, including "no update required" where that is the verdict
- [ ] MCP verdict stated in the PR body ("MCP reviewed, no update required" where that applies)
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-09-07
- Implementation completed: 2026-09-07
- Tests passing: 135 in `Clio.Knowledge.Bundle.Tests` (1 skipped), all green.
- Notes:

**Shipped.** `guidance/mcp/guides/processes/versions.md` gains the write half: four new sections replacing
the old "What this build cannot do" — the two-step write sequence, ask-once-then-behave-predictably, what a
rollback does and does not do, and what a rejected edit leaves behind. `libraryVersion` 1.13.78 -> 1.13.79 and
the `process-versions` resource description rewritten to cover the write half. Four new content-pin tests in
`ProcessVersionsGuidanceTests`, and the shipped-tool-name list extended with both new tools plus
`install-process-builder`.

**No separate create step, stated first.** The article says outright that looking for a "create a version"
tool is the first wrong turn: ONE call carries the edits and produces the version, and an EMPTY operations
array is the snapshot gesture. An agent that does not read this either invents a tool or reports the
capability as missing.

**AC-03 needed its own paragraph, not a sentence.** "The session policy is agent behaviour rather than a
request field" is the kind of statement an agent reads past, so the article says what it implies: no call
inherits anything from a previous one, neither tool takes a session mode, and "the user said new versions
from now on" changes which tool you reach for — never what a call means.

**Two stale claims in OTHER files had to move**, and both were pinned by story 6's own tests, which is how
they surfaced rather than shipping:
1. `ProcessVersionsGuidanceTests` pinned "There is no operation that CREATES a version." and "There is no
   operation that SETS the active version". Both are now FALSE. The assertions were REMOVED rather than
   softened, and replaced with the boundary that genuinely remains — nothing migrates a running instance,
   nothing deletes a version — with a comment saying the boundary moved in stories 16-17.
2. `process-modeling` told the agent an in-place edit of the active version had "no operation in this build
   that restores the previous graph. It is irreversible". The edit IS still irreversible and the word stays
   (a test pins it), but the paragraph now says the irreversibility is AVOIDABLE rather than inherent and
   points at `modify-business-process-as-new-version`. Leaving it would have had the entry article talk an
   agent out of the very path this feature adds.

**One measured limit.** The `process-versions` resource description is capped at 1000 characters by
`BundleBuilder.ValidateDiscoveryText`; the first rewrite came to 1065 and failed eight bundle tests at once.
Final length 996.

**Not done here.** The clio-side re-pin of `curated-knowledge-names.json` rides story 7's mechanism, per this
story's own implementation notes — not re-pinned in this commit.
