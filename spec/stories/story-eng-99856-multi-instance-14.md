# Story 14: Retract the 19 statements that promise multi-instance is refused, and publish the guidance

**Feature**: eng-99856-multi-instance
**FR coverage**: FR-21
**AC coverage**: AC-20
**PRD**: [prd-eng-99856-multi-instance.md](../prd/prd-eng-99856-multi-instance.md) — G5, *Delivery cost*
**ADR**: [adr-eng-99856-multi-instance.md](../adr/adr-eng-99856-multi-instance.md) — *Files to modify*, last rows
**Contract**: [eng-99856-multi-instance-contract-answers.md](../eng-99856-multi-instance/eng-99856-multi-instance-contract-answers.md) — *Corrections this pass made*
**Jira**: ENG-99856
**Status**: review
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

- [x] **AC-01** (AC-20/FR-21) — Given the shipped change, when the 19 retraction targets are grepped
  across clio, CrtProcessBuilder and clio-knowledge, then **none remains**: 8 in clio, 11 in
  CrtProcessBuilder. The PR description lists all 19 with `file:line` **before** and the replacement text
  **after** — an inventory, not a claim.
- [x] **AC-02** (AC-20/FR-21) — Given the two **user-visible run-time messages** among them, when each is
  read after the change, then it describes current behaviour and names the supported route. These two are
  the ones a user actually sees; they are not documentation and must not be triaged as such.
- [x] **AC-03** (FR-21) — Given the clio-knowledge pull request, when it is reviewed, then it spans the
  **five** files — `sub-process.md`, `parameters.md`, `element-catalog.md`, `process-modeling.md` and the
  `bundle-source.json` description — and carries the **ten** statements.
  `parameters.md`'s "Nothing CONSUMES a collection yet" is the closing claim of the *collection-parameter*
  contract, not a sub-process aside: this feature makes it false and it is in scope.
- [x] **AC-04** (FR-21) — Given that pull request, when it is built, then `libraryVersion` is **bumped**
  and `sequence` is **not hand-authored** — `BundleBuilder.DeriveSequence`
  (`automation/Clio.Knowledge.Bundle/BundleBuilder.cs:183-210`) computes it from `libraryVersion` at build
  time. clio rejects a library whose content changed under a reused sequence.
- [x] **AC-05** (AC-20) — Given `clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json`, when
  it is re-pinned, then it names the **new** generation; the single hand-written number is line 4. Verify
  the current value before editing — this worktree is level with `origin/master`, so the stale `1.15.23`
  the ADR warns about should already read the master value.
- [x] **AC-06** (AC-20) — Given `WorkspaceTemplateGuidanceDriftTests`, when it runs, then it is **green**:
  no guide is orphaned and no shipped template (`clio/tpl/**`) names a tool that is not resident or
  bridged.
- [x] **AC-07** — Given the guidance content, when it describes the new capability, then it states: the
  one member and its three fields; that the collection to iterate is bound with plain `addMapping`; the
  dotted item path; the output-collection refusal **with its map-from alternative**; the
  mode-without-`enabled` refusal; the `Internal`-callee notice; and — this one matters — the asymmetry
  that the applier **writes** In/Out and all five parameters but **validates** only the two collection
  UIds and their `CompositeObjectList` type. The asymmetry looks like an oversight unless it is explained.
- [x] **AC-08** — Given `docs/knowledge/` in clio, when the diff is reviewed, then the two records whose
  `applies-to` files this feature changed —
  `platform/subprocess-sync-flattens-a-multi-instance-element.md` and
  `platform/subprocess-value-survives-only-under-two-stamps.md` — are **updated or deleted** in this same
  pull request. A fact that stopped being true is deleted, not hedged.
- [x] **AC-09** — Given the same directory, when a new record is written, then it is written **only** for
  what the code does not say: the compiled-vs-metadata instance fork and its consequence for describe
  (ADR F3/F4/F8), and the caller-stamp run-time filter. One file is one fact; nothing about the PR, the
  merge or the review rounds goes in.
- [x] **AC-10** — Given the release order, when the guidance lands, then it is **before or with** the clio
  release that emits the new behaviour. State in the PR that an environment holding an older generation
  needs `update-knowledge`, and that a failed update keeps serving the OLD article with no error.
- [x] **AC-ERR** — Given any statement in the inventory that turns out **not** to be a retraction target
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

- Implementation started: 2026-09-22
- Implementation completed: 2026-09-22
- clio-knowledge PR branch: `feature/ENG-99856-multi-instance` @ `25c013f`, libraryVersion
  **1.15.51 -> 1.15.52** (sequence 1015052000, DERIVED at build time, not hand-authored)
- Tests passing: clio-knowledge `automation/Clio.Knowledge.Bundle.Tests` — **202 passed, 0 failed**;
  clio `Category=Unit&(Module=McpServer|Module=ProcessModel)` — **5872 passed, 0 failed**;
  `WorkspaceTemplateGuidanceDriftTests` — **11 passed**.

### The inventory, and why the count moved (AC-01, AC-ERR)

The story predicted 19 targets: 8 in clio, 11 in CrtProcessBuilder, plus 10 in clio-knowledge. What the
sweep actually found:

| Repository | predicted | retracted here | already retracted | still true, removed from the inventory |
|---|---|---|---|---|
| clio | 8 | **3** | 3 (story 13) | 2 |
| CrtProcessBuilder | 11 | **0** | 11 | — |
| clio-knowledge | 10 | **10** | — | — |

**CrtProcessBuilder's eleven were retracted by the behaviour stories as the code was rewritten**, not
left for this one — `EnsureNotMultiInstance`'s message now names the working route, `SubProcessBlockShape`
says outright that a multi-instance element is no longer a reason to route differently, and both
user-visible run-time messages (AC-02) already describe current behaviour and name
`subProcess: {resync: true}`. Verified by reading each, not by trusting the story.

**Three retracted in clio here:**

1. `clio/Command/ProcessModel/IProcessDescriber.cs` — the `MultiInstance` doc said "What stays refused is
   a RETARGET ... and a re-synchronization". Both are available; the file CONTRADICTED ITSELF, because
   the `InSync` doc twenty lines below already said the re-synchronization exists. Replaced with what the
   applier does: de-convert, let the ordinary applier work with every guard, re-convert around the same
   five parameter objects.
2. `IProcessDescriber.cs` — `ItemProperties` said "one entry per column the collection carries (name,
   type, **tag = the column UId**)", unscoped. True where the items are COLUMNS; false where they are
   another process's PARAMETERS, which is exactly a multi-instance element's two collections —
   0 of the 407 in the shipped corpus carry a tag. Scoped, with the consequence stated: a consumer that
   requires the tag silently rejects every multi-instance collection.
3. `docs/knowledge/platform/subprocess-sync-flattens-a-multi-instance-element.md` — said CrtProcessBuilder
   "therefore refuses such an element". Updated rather than deleted: its first four paragraphs are the
   platform mechanism, still true and now load-bearing for the applier that DRIVES that rebuild.

**Two removed from the inventory as still true** (AC-ERR — a true statement is not edited to make a
number come out right): `CreateBusinessProcessTool` and `ModifyBusinessProcessTool` carry nothing false
about multi-instance — the `subProcess` block is wholly delegated to guidance — and
`process-activity-connections`'s R16 ("collection mapping => multi-instance") is a correct statement about
the platform, not a refusal claim.

### The clio-knowledge pull request (AC-03, AC-04, AC-07)

Five files, ten statements: `sub-process.md` (the section, replacing NOT SUPPORTED), `parameters.md`
(two — "Nothing CONSUMES a collection yet", and `inSync` ending at "permanent and meaningless"),
`element-catalog.md`, `process-modeling.md`, and `bundle-source.json`'s two descriptions (one of which
said "no iterator consumes a read collection").

AC-07's list is all present, including the two that read as defects unless explained: the
`Internal`-direction callee parameter DROPPED and reported rather than refused (refusing would make 12 of
the 327 shipped single-instance elements permanently unconvertible), and the asymmetry that the applier
WRITES all five parameters but VALIDATES only the two collection UIds and their `CompositeObjectList`
type — the counters self-heal, the collections carry the callee's contract and every mapping against it.

One guard fired and was obeyed rather than raised: `EveryProcessArticle_ShouldFitInOneGetGuidanceResponse`
put `process-parameters` 147 characters over budget. My own additions were trimmed by 156 characters. The
budget exists because an over-budget article spills to a single line that Read cannot page, which is the
ENG-96212 defect verbatim.

### AC-05 / AC-08 / AC-09, honestly

- **AC-05** — `curated-knowledge-names.json` re-pinned 1.15.46 -> 1.15.52. `availableNames` is
  UNCHANGED: this change adds and renames no article, so the drift oracle's input did not move. The two
  version fields are pinned to the generation this PR publishes, which couples the two pull requests —
  see AC-10.
- **AC-08** — both named records updated, neither deleted, because in each case the fact that stopped
  being true was one paragraph and the rest is the platform mechanism the new code depends on.
  `subprocess-value-survives-only-under-two-stamps.md` gained the write-path consequence: every
  conversion hop must carry `CreatedInSchemaUId` and `SourceValue.ModifiedInSchemaUId` across UNCHANGED,
  which is why the applier re-converts around the same five objects instead of minting new ones.
- **AC-09** — **no new record was written, deliberately.** Both facts the story asks for are already
  recorded: the compiled-vs-metadata instance fork by
  `subprocess-insync-depends-on-the-schema-instance.md` and
  `describe-itemproperties-reflect-the-worker-instance.md`, the caller-stamp run-time filter by
  `subprocess-value-survives-only-under-two-stamps.md`. The policy is to write only what is not already
  recorded.

### Release order (AC-10)

The guidance must land **before or with** the clio release. An environment holding an older generation
needs `update-knowledge`, and a FAILED update keeps serving the OLD article with no error at all — so a
reader can be told multi-instance is unsupported by a library that silently never updated. Check
`info-knowledge`'s reported Library version before trusting a guidance-dependent answer.

### Carried in from story 13

Two items the description budget pushed out of the tool texts and into the guidance, both now landed:
the `multiInstanceOptions` write contract, and the pointer reconciliation — `create-business-process`
still says `process-element-catalog` owns the `subProcess` block while `modify-business-process` says
`process-sub-process`. The catalog article now defers explicitly ("go there before writing any of it"),
which resolves the divergence from the guidance side without touching either tool's byte budget.
