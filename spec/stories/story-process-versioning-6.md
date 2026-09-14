# Story 6: Publish the process-versions guidance article

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-13
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio-knowledge
**Status**: review
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

- [x] **AC-01** — Given the guidance bundle, when `process-versions` is requested, then it states: the family is flat, exactly one version is actual, running instances stay on the version they started on, rollback affects only new runs, and deleting a version does not exist
- [x] **AC-02** — Given the article, when it is read, then it states that versionhood cannot be inferred from a schema name
- [x] **AC-03** — Given `process-modeling`, when its routing index is read, then it lists `process-versions` and its article counts are correct
- [x] **AC-04** — Given the bundle source, when the release check runs, then `libraryVersion` has moved and the derived sequence follows
- [x] **AC-ERR** — Given the article names a tool, when the guide-content test runs, then the name matches the shipped constant exactly

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

- [x] The clio-side fixture re-pin is story 7, not this PR — this PR names it as the follow-up
- [x] Code compiles without Roslyn analyzer warnings
- [x] All new tool names and flags are kebab-case
- [x] Unit tests use `[Category("Unit")]` — never `[Category("UnitTests")]`
- [x] No `catch (Exception)` added to clio code paths
- [x] Knowledge records whose `applies-to` names a file this story touches are updated or deleted **in this PR** (`AGENTS.md:456-457`)
- [x] Docs verdict stated explicitly in the PR body, including "no update required" where that is the verdict
- [x] MCP verdict stated in the PR body ("MCP reviewed, no update required" where that applies)
- [ ] **BLOCKS THE PR**: replace `<CLIO-READBACK-VERSION-TBD>` in `guidance/mcp/guides/processes/versions.md` with the clio release that first CONTAINS the version read-back. Fetch and merge master first, then read the real tag — do NOT assume the next number. 8.1.0.118 is already released WITHOUT the fields, so it is the one answer that is certainly wrong. While the placeholder stands, `Guide_ShouldDeclareItsClioBoundaryAndTheModifyPrecondition` is reported as SKIPPED carrying this instruction; once a version is written in it becomes a strict assertion.
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-09-03
- Implementation completed: 2026-09-03
- Tests passing: `dotnet test automation/Clio.Knowledge.Bundle.Tests` in clio-knowledge → 123 passed, 0 failed. Four new pins over the article; the size and cross-reference contracts cover it automatically because `ProcessGuideSet` derives the article set from the manifest.
- Notes:
  - Content lives in clio-knowledge, branch `feature/ENG-94374-process-versions`, commit `bb08fdf`: `guidance/mcp/guides/processes/versions.md` (135 lines), registered in the three `bundle-source.json` places, `libraryVersion` 1.13.67 → 1.13.68, derived sequence forward.
  - **Verified end to end, not just by unit test.** The branch was installed into a local clio as a git knowledge source and a freshly started `clio mcp-server` answered `get-guidance name=process-versions` with the full 10 280-character article; the `routing` guide selects it. All eight content probes present in the SERVED text, not only on disk.
  - The cross-reference suite caught an omission a reviewer would likely have missed: adding the article to the entry article's index is not enough — `routing.md` is the only guidance pointer clio's MCP instructions carry, and an unrouted article is reachable only by already knowing it exists.
  - `[Category("Unit")]` from the DoD does not apply: clio-knowledge's test project uses no NUnit categories at all, and adding one there would be the inconsistency, not the compliance.
  - No knowledge record's fact stopped being true. In particular the counted-claims record still stands: my new pin compares the manifest description against itself inside one repository, and the gap it records — nothing compares a tool `[Description]` count against the guidance article that repeats it, across repositories — is untouched.
  - A new clio-side record was added instead, on how to run this local loop at all: `docs/knowledge/McpServer/local-guidance-iteration-needs-the-builtin-source-repointed.md`. The obvious route (register a second source) cannot work — `creatio-curated` is built in, cannot be removed, and holds `com.creatio.clio` even while disabled — and a running MCP server never sees a fresh install, which makes the check look like a failed install.
  - Local machine state was restored: the built-in source is back on `github-release`, enabled, priority 100, and `knowledge-allow-unsequenced` is disabled again.
  - Follow-ups named rather than done: story 7 re-pins clio's `curated-knowledge-names.json` to the published generation, and story 18 adds the write-half guidance once those operations exist. The article states outright that this build creates no version and sets no active version. 
