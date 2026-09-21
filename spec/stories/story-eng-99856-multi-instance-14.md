# Story 14: Retract the 19 statements that promise multi-instance is refused, and publish the guidance

**Feature**: eng-99856-multi-instance
**FR coverage**: FR-21
**AC coverage**: AC-20
**PRD**: [prd-eng-99856-multi-instance.md](../prd/prd-eng-99856-multi-instance.md) — G5, *Delivery cost*
**ADR**: [adr-eng-99856-multi-instance.md](../adr/adr-eng-99856-multi-instance.md) — *Files to modify*, last rows
**Contract**: [eng-99856-multi-instance-contract-answers.md](../eng-99856-multi-instance/eng-99856-multi-instance-contract-answers.md) — *Corrections this pass made*
**Jira**: ENG-99856
**Status**: ready-for-dev
**Size**: L (full day — three repositories, 19 statements, a five-file guidance PR with a
`libraryVersion` bump, and a fixture re-pin whose drift test must stay green)
**Repo**: clio + crt-process-builder + **clio-knowledge** (a pull request in another repository)
**Depends on**: story 13 (the tool texts must be final — the guidance quotes them) and, through it, every
behaviour story
**Blocks**: the clio release. A stale knowledge cache fails **silently**: it keeps serving the article
that says multi-instance is refused.

---

## As a

Creatio developer or agent reading the shipped guidance

## I want

every statement that says multi-instance is refused to be gone, and the published guidance to describe
what the toolkit now does

## So that

the product does not keep telling me a capability does not exist while the server accepts it

---

## Acceptance Criteria

- [ ] **AC-01** (AC-20/FR-21) — Given the shipped change, when the 19 retraction targets are grepped
  across clio, CrtProcessBuilder and clio-knowledge, then **none remains**: 8 in clio, 11 in
  CrtProcessBuilder. The PR description lists all 19 with `file:line` **before** and the replacement text
  **after** — an inventory, not a claim.
- [ ] **AC-02** (AC-20/FR-21) — Given the two **user-visible run-time messages** among them, when each is
  read after the change, then it describes current behaviour and names the supported route. These two are
  the ones a user actually sees; they are not documentation and must not be triaged as such.
- [ ] **AC-03** (FR-21) — Given the clio-knowledge pull request, when it is reviewed, then it spans the
  **five** files — `sub-process.md`, `parameters.md`, `element-catalog.md`, `process-modeling.md` and the
  `bundle-source.json` description — and carries the **ten** statements.
  `parameters.md`'s "Nothing CONSUMES a collection yet" is the closing claim of the *collection-parameter*
  contract, not a sub-process aside: this feature makes it false and it is in scope.
- [ ] **AC-04** (FR-21) — Given that pull request, when it is built, then `libraryVersion` is **bumped**
  and `sequence` is **not hand-authored** — `BundleBuilder.DeriveSequence`
  (`automation/Clio.Knowledge.Bundle/BundleBuilder.cs:183-210`) computes it from `libraryVersion` at build
  time. clio rejects a library whose content changed under a reused sequence.
- [ ] **AC-05** (AC-20) — Given `clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json`, when
  it is re-pinned, then it names the **new** generation; the single hand-written number is line 4. Verify
  the current value before editing — this worktree is level with `origin/master`, so the stale `1.15.23`
  the ADR warns about should already read the master value.
- [ ] **AC-06** (AC-20) — Given `WorkspaceTemplateGuidanceDriftTests`, when it runs, then it is **green**:
  no guide is orphaned and no shipped template (`clio/tpl/**`) names a tool that is not resident or
  bridged.
- [ ] **AC-07** — Given the guidance content, when it describes the new capability, then it states: the
  one member and its three fields; that the collection to iterate is bound with plain `addMapping`; the
  dotted item path; the output-collection refusal **with its map-from alternative**; the
  mode-without-`enabled` refusal; the `Internal`-callee notice; and — this one matters — the asymmetry
  that the applier **writes** In/Out and all five parameters but **validates** only the two collection
  UIds and their `CompositeObjectList` type. The asymmetry looks like an oversight unless it is explained.
- [ ] **AC-08** — Given `docs/knowledge/` in clio, when the diff is reviewed, then the two records whose
  `applies-to` files this feature changed —
  `platform/subprocess-sync-flattens-a-multi-instance-element.md` and
  `platform/subprocess-value-survives-only-under-two-stamps.md` — are **updated or deleted** in this same
  pull request. A fact that stopped being true is deleted, not hedged.
- [ ] **AC-09** — Given the same directory, when a new record is written, then it is written **only** for
  what the code does not say: the compiled-vs-metadata instance fork and its consequence for describe
  (ADR F3/F4/F8), and the caller-stamp run-time filter. One file is one fact; nothing about the PR, the
  merge or the review rounds goes in.
- [ ] **AC-10** — Given the release order, when the guidance lands, then it is **before or with** the clio
  release that emits the new behaviour. State in the PR that an environment holding an older generation
  needs `update-knowledge`, and that a failed update keeps serving the OLD article with no error.
- [ ] **AC-ERR** — Given any statement in the inventory that turns out **not** to be a retraction target
  (it says something still true), when it is triaged, then it is removed from the inventory with a reason
  — the count moves and the PR says why. Do not edit a true statement to make a number come out right.

## Implementation Notes

The retraction inventory is the deliverable, not a side effect. Build it with a grep across the three
repositories and record the count before and after; `docs/knowledge/` is internal repository knowledge and
is **not** the shipped guidance library, so keep the two sweeps separate in the description.

Known members of the inventory, each already located by the research (confirm `file:line` at
implementation time, they move):

- clio: `clio/Command/ProcessModel/IProcessDescriber.cs:1105`; the
  `DescribeProcessTool` "a re-sync is REFUSED on it" clause (retracted in story 13 — count it once);
  a statement in `clio.tests/Common/BundledProcessBuilderPackageTests.cs`; the rest across the
  process-designer tool texts and `docs/`.
- CrtProcessBuilder: `EnsureNotMultiInstance`'s neighbourhood, the `SubProcessApplier` class summary
  (story 7), the describe handler, and the **two user-visible run-time messages**.
- clio-knowledge: the ten statements across the five files.

Two corrections that ride along because the same sentences carry them (FR-20, also touched by story 7):
the stale T-27 clause, and the false `itemProperties` "tag (the column UId)" claim — **0 of the 407**
shipped multi-instance item properties carry a tag.

**Guidance changes are a pull request in `clio-knowledge`**, one Markdown file per article under
`guidance/`, indexed by `bundle-source.json`. Never duplicate guide content into
`McpServerInstructions.cs`, which carries only the mandatory pointer to the `routing` guide. If an article
is added or renamed, update the routing article that points at it.

## Owner decisions this story assumes

The guidance text is a direct function of four of them, so it cannot be written before they are settled —
or it must be written against the working assumptions and revisited:

| OQ | Working assumption the text states | If reversed |
|---|---|---|
| OQ-01 | de-conversion ships: `enabled: false` is documented as a destructive write | the text says de-conversion is not offered and names `removeElement` + `addElement` |
| OQ-02 | a retarget on a multi-instance element is refused, naming both routes | the text documents an unconditional de-convert-on-retarget instead |
| OQ-04 | an output-collection target is refused | the text documents a warning and says the value will be erased on the next synchronization |
| OQ-03 | `inSync` is frozen; no meaning change | the text documents the redefinition loudly and names the describe floor |

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` (clio) | the re-pinned `curated-knowledge-names.json` generation; `WorkspaceTemplateGuidanceDriftTests` green | `clio.tests/Command/McpServer/` |
| Unit (clio-knowledge) | the bundle tests in that repository pass with the bumped `libraryVersion` | clio-knowledge `automation/` suite |
| Manual | a grep across all three repositories returns zero of the 19 statements | recorded in the PR |

Test naming: `CuratedKnowledgeNames_ShouldMatchPublishedGeneration_WhenLibraryVersionIsBumped`.

## Definition of Done

- [ ] The before/after inventory of all 19 statements is in the PR description with `file:line`
- [ ] The two user-visible run-time messages are replaced, not merely documented around
- [ ] clio-knowledge PR: five files, ten statements, `libraryVersion` bumped, `sequence` not hand-authored
- [ ] `curated-knowledge-names.json` re-pinned; `WorkspaceTemplateGuidanceDriftTests` green
- [ ] `docs/knowledge/` records whose `applies-to` changed are updated or deleted in the same PR; new
      records only for facts the code does not say
- [ ] The guidance lands **before or with** the clio release; the `update-knowledge` caveat is stated
- [ ] Tests: AAA, `because` on every assertion, `[Description]` on every method, `[Category("Unit")]`
- [ ] No new `CLIO*` diagnostics in touched files; no new CLI flag (kebab-case vacuous)
- [ ] Validated locally:
      `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer" --no-build`,
      plus the clio-knowledge bundle suite, plus the package suite if a package message changed
- [ ] All three PRs reference this story file and link each other; **package first, guidance before or
      with the clio release**

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Retraction count before / after:
- clio-knowledge PR + libraryVersion:
- Tests passing:
- Notes:
