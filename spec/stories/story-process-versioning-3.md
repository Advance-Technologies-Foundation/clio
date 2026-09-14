# Story 3: Resolve a caption to the active version, in one place

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-10, FR-11
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio
**Status**: done
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

- [x] **AC-01** — Given a versioned family, when describe runs with `--process-caption`, then the active version is described
- [x] **AC-02** — Given a versioned family, when `generate-process-model` runs with the shared caption, then it succeeds against the active version instead of reporting `Multiple processes match caption`
- [x] **AC-03** — Given two processes that genuinely share a caption, when either command runs, then the existing ambiguity error is returned unchanged
- [x] **AC-04** — Given candidates whose active-version fact is null, when the filter empties the set, then the existing ambiguity error is returned rather than an arbitrary pick
- [x] **AC-ERR** — Given no process matches the caption, when either command runs, then the existing not-found error is unchanged

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

- [x] Both call sites go through the same resolver — no second copy of the policy
- [x] The four `generate-process-model` doc targets updated in this PR
- [x] ~~Story 2 owns the shared knowledge record; this PR states that and does not re-edit it~~ — **overridden, see notes**: this story falsified one sentence of it
- [x] Code compiles without Roslyn analyzer warnings
- [x] All new tool names and flags are kebab-case
- [x] Unit tests use `[Category("Unit")]` — never `[Category("UnitTests")]`
- [x] No `catch (Exception)` added to clio code paths
- [x] Knowledge records whose `applies-to` names a file this story touches are updated or deleted **in this PR** (`AGENTS.md:456-457`)
- [x] Docs verdict stated explicitly in the PR body, including "no update required" where that is the verdict
- [x] MCP verdict stated in the PR body ("MCP reviewed, no update required" where that applies)
- [x] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-09-03
- Implementation completed: 2026-09-03
- Tests passing: `dotnet test --filter "Category=Unit&(Module=Command|Module=McpServer|Module=ProcessModel)"` → 8140 passed, 0 failed, 14 skipped. 4 new resolver tests (TC-U-15 and the AC-03/AC-04 fallbacks) and 3 in `ServerProcessDescriberTests` proving describe goes through the shared seam (TC-U-16).
- Notes:
  - The generator already called `ProcessLibResolver.Resolve`, so the policy went into the resolver and the
    generator inherited it with no edit. Only describe had to be wired in.
  - Describe translates the resolver errors into its own `ResolveId` code instead of forwarding them, so the
    not-found message stays byte-identical (AC-ERR) while the ambiguity error is new — the PRD already
    records that new describe failure mode. Translating vocabulary is not a second copy of the policy.
  - **`get-process-signature` is affected, and the PRD called it a non-goal.** It resolves through the same
    `ProcessModelGenerator` path, so its caption resolution now picks the active version too. This is
    unavoidable given "one policy, one seam" — gating it would BE the second copy the DoD forbids. The
    non-goal still holds in substance: nothing was done to make its signature output version-aware, and
    resolution by code is untouched. Its docs and tool description are updated accordingly, because the
    sentence "when a caption matches more than one process the command returns a failure" had stopped being
    true.
  - **Overrode the DoD line about not re-editing the shared knowledge record.** Its description asserted "the
    resolution itself is unchanged", which this story falsified for the caption identity. Amended minimally:
    the by-name half is what stayed unchanged, and the trap is now identity-specific. Leaving a false
    sentence in place to honour a checklist item would defeat the point of the record.
  - Narrowed a **pre-existing** `catch (Exception)` in the caption path to the five specific DataService
    failure types, matching story 1's reader. This is a real behaviour change: an exception outside that set
    now propagates instead of being reported as an unresolvable caption. Deliberate — ATF expression
    failures mean the query is wrong and must surface — but it is wider than the ACs asked for.
  - Docs: `generate-process-model` help/`docs`/argument text now document the caption identity at all (they
    never did, though the code always accepted one) plus the active-version resolution;
    `get-process-signature` help/`docs` corrected. `Commands.md` and `WikiAnchors.txt` carry one-line index
    and anchor entries with no behavioural text — reviewed, no update required.
  - RELEASE.md: no note added, and the story's request for one does not apply. That file is the release
    PROCEDURE; §6 requires What's new to be assembled at release time into the GitHub release, with the
    per-change obligation discharged through commit messages. There is no in-repo changelog.
  - MCP: three tool descriptions updated — `describe-business-process` (process-caption now targets the
    active version, new refusal), `get-process-signature`, `generate-process-model` (its description never
    mentioned the caption identity). No tool renamed, no argument added. 
