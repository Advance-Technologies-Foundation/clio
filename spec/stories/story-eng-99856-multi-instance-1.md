# Story 1: Diagnose why `describe` reports no item properties — and write the gate answer down

**Feature**: eng-99856-multi-instance
**FR coverage**: FR-18 (its first half — the diagnosis). AC-15 is **outcome-dependent** and is NOT asserted here.
**PRD**: [prd-eng-99856-multi-instance.md](../prd/prd-eng-99856-multi-instance.md)
**ADR**: [adr-eng-99856-multi-instance.md](../adr/adr-eng-99856-multi-instance.md) — Decision 0 / D0-c
**Contract**: [eng-99856-multi-instance-contract-answers.md](../eng-99856-multi-instance/eng-99856-multi-instance-contract-answers.md) — Q3
**Platform facts**: [eng-99856-multi-instance-platform-facts.md](../eng-99856-multi-instance/eng-99856-multi-instance-platform-facts.md) — §1.1
**Jira**: ENG-99856
**Status**: ready-for-dev
**Size**: M (half day — the stand half needs a cold app pool and must run sequentially)
**Repo**: crt-process-builder (one test) + the stand + `spec/` (the written gate)
**Depends on**: nothing. **This story runs first.**
**Blocks**: stories 9, 10, 12 (the whole describe half, and the rebundle that would ship it)

---

## This is a diagnosis with a written gate, not a feature

Nothing in this story ships behaviour. It exists because ADR Decision 0 eliminated three of the four
candidate causes **in source** and the survivor is environmental — and because the shape of two later
stories, plus the wording of AC-15, is selected by the answer. Committing describe code before this is
written down is committing to a mechanism nobody has measured.

What D0 already settled, so it is not re-derived here:

- **A-01 is refuted as stated.** `LoadForDescribe` really does prefer the manager instance
  (`packages/CrtProcessBuilder/Files/src/cs/Schema/ProcessSchemaRepository.cs:239-282`), but both of its
  branches funnel into **one** factory — `Manager.FindInstanceByUId` (`Terrasoft.Core/Manager.cs:317-325`)
  → `SchemaManager.InitializeSafeSchema` (`:4023-4039`) and `GetDesignInstance` (`:4715-4721`) →
  `InitializeSchema` (`:4002-4013`) — both reaching `CreateSchemaInstance`.
- **The content fork sits BELOW both branches**, in
  `BaseProcessSchemaManager.CreateSchemaInstance` (`Terrasoft.Core/Process/BaseProcessSchemaManager.cs:751-757`),
  on `UseInstanceFromMetaData` (`ProcessSchemaManagerItem.cs:56-73`). A compiled instance can never carry
  item properties — the generator excludes them at `ProcessSchemaGenerator.cs:1637`, `:1725`, `:1783` and
  `ProcessSchemaGeneratorNew.cs:1852`, `:2057`. A metadata instance always reads them
  (`ProcessSchemaParameter.ApplyMetaDataValue:855-858`).
- **No package-reachable lever forces a metadata instance.** `ForceUseInstanceFromMetaData` (`:49`) and
  `SchemaManager.FindInstanceFromMetaData` / `GetInstanceFromMetaData` (`SchemaManager.cs:3245`, `:3277`)
  are `internal` to `Terrasoft.Core`; `FindRuntimeInstanceByUId` (`:4730-4733`) prefers `SafeInstance` and
  **mutates** `managerItem.Instance` (`:1729-1742`).
- **The measured element cannot have been compiled**: describe answered `multiInstance: true`, derived off
  the returned instance at `Elements/SubProcessElementHandler.cs:98`, and no generator emits
  `MultiInstanceOptions`.

So for the measured element the instance *should* have carried `L18`, and the two remaining shapes are:
a **stale cached instance** (`FindInstanceFromMetaData` caches in `MetaItems` for the process lifetime,
`SchemaManager.cs:3245-3254`, and the rebuild empties both `ItemProperties` **in place** at
`Terrasoft.Core/Process/ProcessSchemaActivity.cs:387-388`), or a **stored blob that differs from the file
the 4/6 counts were read from** (`PackageStore/CrtBase/branches/7.8.0/…/metadata.json` vs the
environment's own `SysSchema.MetaData`, `ProcessSchemaManager.cs:331-336`).

## As a

developer about to build the describe half of this feature

## I want

the reason `describe-business-process` returns no `itemProperties` established by two instruments and
recorded in a committed document that names one of two outcomes

## So that

stories 9 and 10 commit to the right shape, and AC-15 is either confirmed or renegotiated with the owner
**before** anyone writes projection code

---

## Acceptance Criteria

- [ ] **AC-01** — Given a package unit test that deserializes a schema whose parameter carries `L18`,
  when `ToDescribeParameter` (`Files/src/cs/Parameters/ProcessParameterService.cs:152-154`) projects it,
  then the test asserts nested `itemProperties` end to end and is **committed either way** — it is the
  capability pin that separates a code defect from an environment state, so a red result is a finding, not
  a reason to delete the test.
- [ ] **AC-02** — Given the stand, when one read is run **sequentially** against a **cold** app pool on a
  schema untouched by any write in that process lifetime, then the reading is recorded with environment
  name, package version, clio commit, timestamp, and the exact element and parameter names.
- [ ] **AC-03** — Given the same schema, when its stored `SysSchema.MetaData` is read directly, then the
  presence or absence of `L18` on `InputRecordCollection` / `OutputRecordCollection` is recorded with the
  entry counts, alongside the 4 and 6 read from the shipped file.
- [ ] **AC-04** — Given both instruments, when the gate is written to
  `spec/eng-99856-multi-instance/eng-99856-multi-instance-describe-gate-outcome.md`, then it names
  **exactly one** of Outcome 1 (a cold instance reports them) or Outcome 2 (the package's reachable load
  path never reports them), and quotes the evidence that selects it.
- [ ] **AC-05** — Given the outcome, when the document states the AC-15 disposition, then it says either
  "AC-15 met as written (4 and 6)" **or** "AC-15 cannot be met as written", with the proposed renegotiated
  wording and the owner named as the person who must sign off on the added `calleeContract` member.
  **This story asserts neither in advance.**
- [ ] **AC-06** — Given Outcome 1 with a stale-cache cause, when the document is written, then it records
  which earlier operation flattens the cached instance, and restates D0-a: describe will **not** invalidate
  the manager cache, because that gives a read a write's blast radius on shared process-lifetime state.
- [ ] **AC-07** — Given this story, when its diff is reviewed, then the only production-code change is the
  committed test: no change to `LoadForDescribe`, no cache invalidation on a read path, no projection
  change.
- [ ] **AC-ERR** — Given a stand that is unreachable, or an app pool that cannot be cycled, when the
  measurement cannot be taken, then the story **stops** and records the blocker in the same document; a
  warm-pool reading is not substituted and the outcome is not guessed.

## Implementation Notes

**Instrument A — `tests/UnitTests/CrtProcessBuilder.Tests/` (no stand).** Build the schema through
`SubProcessTestSupport` *only if* story 2 has landed; otherwise construct the parameter directly, because
the helper types everything `Text` today (see story 2). What matters is that the parameter carries a
non-empty `ItemProperties`, so the assertion is about the projection, not about the platform.

**Instrument B — the stand, sequentially.** A parallel burst of schema operations trips IIS rapid-fail and
downs a .NET Framework stand's app pool. Recycle the pool first, then do nothing else against that
environment until the read is taken — an earlier operation that reached
`ProcessSchemaSubProcess.SchemaUId`'s setter (`:63-71`) re-enters
`SynchronizeParametersInternal` (`ProcessSchemaActivity.cs:373-395`), which empties both `ItemProperties`
at `:387-388` on the **cached object graph**. Deserialization alone does not: the metadata reader assigns
the backing field `_schemaUId` (`ProcessSchemaSubProcess.cs:214-216`), never the public setter.

Reference measurement to reproduce (2026-09-21, stand `Creatio`, `CrtProcessBuilder 1.6.3.31`, clio from
`origin/master` `01c8b677a`): `describe-business-process` on `ExpireLicenseNotificationProcess` →
`SubProcess2` returned five root parameters, `multiInstance: true`, and `itemProperties` on **none** of
them; the shipped metadata carries `L18` with 4 and 6 entries.

**Do not** attempt option B, C or D from ADR D1 (force a metadata instance, invalidate the cache from the
read path, synthesise item properties). All three are rejected with reasons; this story does not reopen
them.

**The gate table, reproduced so the document can be filled in against it:**

| Outcome | FR-18 becomes (story 10) | AC-15 |
|---|---|---|
| 1 — a cold instance reports them | a regression test pinning nested reporting through the real describe path, plus a documented statement that a described `itemProperties` reflects the instance the server holds, which an earlier write in the same process lifetime can have flattened | met as written |
| 2 — it never reports them on the reachable path | the contingent `calleeContract` view under `multiInstanceOptions`, derived from the callee (`ProcessSchemaSubProcess.Schema` → `FindProcessSchemaByUId`, `:164-178`; routing by `FillCollectionParameters`, `ProcessSchemaActivity.cs:336-355`) and **labelled as derived** | renegotiated; owner signs off on the added member |

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` (package) | `ToDescribeParameter` reports nested `itemProperties` for a parameter that has them — the capability pin | `tests/UnitTests/CrtProcessBuilder.Tests/ProcessParameterServiceItemPropertiesTests.cs` |
| Manual (stand, sequential) | cold-pool describe read + direct `SysSchema.MetaData` read, both recorded | `spec/eng-99856-multi-instance/eng-99856-multi-instance-describe-gate-outcome.md` |

Test naming: `MethodName_ShouldBehavior_WhenCondition` — e.g.
`ToDescribeParameter_ShouldReportNestedItemProperties_WhenParameterCarriesThem`.

## Definition of Done

- [ ] The gate document exists, names one outcome, and carries the evidence for it
- [ ] The AC-15 disposition is stated in one sentence and, under Outcome 2, addressed to the owner by name
- [ ] Instrument A is committed and runs in the package suite (green or red, with the finding recorded)
- [ ] No production code path changed: `git diff --stat` shows tests + `spec/` only
- [ ] Stand work was sequential and the app pool state at read time is recorded
- [ ] Tests: AAA with explicit Arrange/Act/Assert, a `because` on every assertion, `[Description]` on every
      method, `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] Validated locally: `dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-nf`
      run in the **main checkout, not a git worktree** (a worktree lacks the untracked core-bin tree and
      yields ~1355 spurious assembly-resolution failures that read as a code regression)
- [ ] PR description references this story file and quotes the gate outcome in its first paragraph

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Gate outcome (1 or 2):
- AC-15 disposition:
- Notes:
