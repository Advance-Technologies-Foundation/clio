# Story 3: Resolve a caption to the active version, in one place

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-10, FR-11
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio
**Status**: ready-for-dev
**Size**: M

---

## As a

CI pipeline author

## I want

caption-based process resolution to pick the active version

## So that

describe stops answering for a random family member and generate-process-model stops failing on versioned processes

---

## Acceptance Criteria

- [ ] **AC-01** — Given a versioned family, when describe runs with `--process-caption`, then the active version is described
- [ ] **AC-02** — Given a versioned family, when `generate-process-model` runs with the shared caption, then it succeeds against the active version instead of reporting `Multiple processes match caption`
- [ ] **AC-03** — Given two processes that genuinely share a caption, when either command runs, then the existing ambiguity error is returned unchanged
- [ ] **AC-04** — Given candidates whose active-version fact is null, when the filter empties the set, then the existing ambiguity error is returned rather than an arbitrary pick
- [ ] **AC-ERR** — Given no process matches the caption, when either command runs, then the existing not-found error is unchanged

## Implementation Notes

Two call sites, one policy. `IProcessDescriber.cs:99` currently does `FirstOrDefault(p => p.Caption == identity.Caption)` — no ambiguity error exists there today, so routing it through the resolver introduces one — recorded in the PRD's CLI Impact table as a new describe failure mode.
`ProcessModelGenerator.cs:84-88` collects caption matches and hands them to `ProcessLibResolver.Resolve` (**source: `clio/Command/ProcessModel/ProcessLibResolver.cs:13`; the fixture, unlike the source, is at `clio.tests/Command/ProcessLibResolverTests.cs` — not under `ProcessModel/`**).
Put the filter in the resolver: it is deliberately the unit-testable seam, and `IsActiveVersion` is `bool?` after story 1, so filter on `== true` and fall back to the ambiguity error when nothing remains.
`generate-process-model` is a public `[Verb]` (`GenerateProcessModelCommand.cs:11`): its behaviour change moves `clio/help/en/generate-process-model.txt`, `clio/docs/commands/generate-process-model.md`, `clio/Commands.md` and `clio/Wiki/WikiAnchors.txt`, plus a `RELEASE.md` note.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | active-version filtering, genuine ambiguity preserved, null-flag fallback, not-found unchanged | `clio.tests/Command/ProcessLibResolverTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [ ] Both call sites go through the same resolver — no second copy of the policy
- [ ] The four `generate-process-model` doc targets updated in this PR
- [ ] Story 2 owns the shared knowledge record; this PR states that and does not re-edit it
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
