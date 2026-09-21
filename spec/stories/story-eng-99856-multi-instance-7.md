# Story 7: The latent guard, the retarget refusal, and two comments that are now false

**Feature**: eng-99856-multi-instance
**FR coverage**: FR-16, FR-20
**AC coverage**: AC-16
**PRD**: [prd-eng-99856-multi-instance.md](../prd/prd-eng-99856-multi-instance.md)
**ADR**: [adr-eng-99856-multi-instance.md](../adr/adr-eng-99856-multi-instance.md) — OQ-02, *Files to modify*
**Platform facts**: [eng-99856-multi-instance-platform-facts.md](../eng-99856-multi-instance/eng-99856-multi-instance-platform-facts.md) — §8.2, §9
**Jira**: ENG-99856
**Status**: ready-for-dev
**Size**: S (< 2h — one parameter type, one refusal, two comments; the value is in the tests and the
wording, not the volume)
**Repo**: crt-process-builder — `packages/CrtProcessBuilder/Files/src/cs/`
**Depends on**: story 3
**Blocks**: stories 12, 14 (the corrected comments are part of the retraction inventory)

---

## As a

developer maintaining the sub-process appliers

## I want

the multi-instance guard typed where the platform declares the property, a retarget on a multi-instance
element refused rather than resolved two different ways, and the two comments this change makes false
corrected in the same commit

## So that

a shape nobody designed for cannot reach an applier that silently discards its parameters, and the next
reader is not misled by text that was true only while multi-instance was refused

---

## Owner decision this story assumes

**OQ-02 — retargeting the callee on a multi-instance element.** Working assumption: **refuse**, naming
both supported routes.
The two behaviours are genuinely different: the designer **de-converts unconditionally** when the callee
changes (`process-activity-schema.js` `resetParameters` `:560-595` — de-convert, re-sync, re-convert),
while the server would retarget and stay multi-instance. The same caller intent yields two different
elements depending on which is chosen, so silently picking one is the wrong move.
*If the owner decides to reproduce the designer instead*, AC-03/AC-04 invert: the retarget succeeds and
de-converts, that behaviour becomes a documented destructive write (and it then depends on OQ-01/story 8
shipping), and the guidance in story 14 says so.

## Acceptance Criteria

- [ ] **AC-01** (AC-16/FR-16) — Given an activity that is **not** a Sub-process but carries a `BP6`, when
  any applier processes it, then it is refused rather than reaching an applier that discards its flat
  parameters. `MultiInstanceOptions` is declared on the **abstract** `ProcessSchemaActivity`
  (`Terrasoft.Core/Process/ProcessSchemaActivity.cs:122`), while the guard takes a
  `ProcessSchemaSubProcess` (`Elements/SubProcessApplier.cs:393`) — so a multi-instance user task reaches
  the other appliers unguarded today.
- [ ] **AC-02** (FR-16) — Given the re-typing, when the diff is reviewed, then `EnsureNotMultiInstance` is
  unchanged **byte-for-byte apart from its parameter type**, and its call sites compile without a cast
  that would re-narrow it.
- [ ] **AC-03** (OQ-02) — Given a multi-instance element and a `subProcess` block that names a **different**
  callee (`processName` or `processUId`), when applied, then clio prints `Error: {message}` and exits
  non-zero, and the element is unchanged.
- [ ] **AC-04** (OQ-02) — Given that message, when a caller reads it, then it names **both** supported
  routes: de-convert first and then retarget (story 8, subject to OQ-01), or remove and re-add the element
  accepting that its UId, flows and mappings change.
- [ ] **AC-05** (FR-20) — Given `SubProcessApplier`'s class summary, when it is read after this change,
  then the stale T-27 clause "this contract produces no dynamic ones" is **gone**, replaced by what is
  true: `CreateIntegerParameter` stamps the **caller's** schema (`ProcessSchemaActivity.cs:443-444`) and
  `MetaItemCollection.InsertItem` backfills `CreatedInSchemaUId = ParentMetaSchema.UId` when it is empty
  (`MetaItemCollection.cs:99-100`), so all three counters are dynamic by construction and delivering this
  feature makes the old clause false.
- [ ] **AC-06** (FR-20) — Given the shipped `itemProperties` doc comment claiming each item carries "tag
  (the column UId)", when it is read after this change, then the claim is gone: **0 of the 407** shipped
  multi-instance item properties carry a tag. Corrected in **both** copies —
  `Contracts/DescribeContracts.cs` (near `:1391-1392`) and clio's
  `clio/Command/ProcessModel/IProcessDescriber.cs:1743-1751`.
- [ ] **AC-ERR** — Given a retarget refusal, when the batch runs, then nothing is persisted.

## Implementation Notes

Key files:

- `Files/src/cs/Elements/SubProcessApplier.cs` — `:393` the guard signature, `:88` its call site, the
  class summary at the top of the file
- `Files/src/cs/Contracts/DescribeContracts.cs` — the `itemProperties` doc comment
- `clio/Command/ProcessModel/IProcessDescriber.cs:1743-1751` — the same comment, clio side

The guard's own text and refusal message do **not** change: only the parameter type widens to
`ProcessSchemaActivity`. Keep it a refusal, not a capability — `BP6` is declared on the abstract activity
but **no activity kind other than `ProcessSchemaSubProcess` ships as multi-instance** (0 of 61), and
offering the capability elsewhere is an explicit non-goal.

No shipped element is currently in the AC-01 state, so this is **latent rather than live**. Say so in the
PR: it is a one-line re-typing whose value is that the next contract change cannot create the state
silently.

The clio-side comment correction lands in the clio repo; if that is a separate PR, link the two in both
descriptions. It also belongs to the retraction inventory story 14 sweeps — do not let it be counted
twice or missed once.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` (package) | a non-sub-process activity carrying `BP6` is refused by every applier that can receive it | `tests/UnitTests/CrtProcessBuilder.Tests/SubProcessApplierTests.cs` |
| Unit `[Category("Unit")]` (package) | a retarget on a multi-instance element is refused; the message names both routes | `tests/UnitTests/CrtProcessBuilder.Tests/MultiInstanceApplierTests.cs` |
| Unit `[Category("Unit")]` (clio) | none required for the comment fix; if a doc-comment guard test exists for this DTO, update it | `clio.tests/Command/McpServer/` |

Test naming: `Apply_ShouldRefuseElement_WhenActivityIsNotSubProcessButCarriesMultiInstanceOptions`.

## Definition of Done

- [ ] `EnsureNotMultiInstance` takes `ProcessSchemaActivity`; otherwise byte-for-byte identical
- [ ] The retarget refusal exists and names both routes; OQ-02's assumption and its reversal consequence
      are in the PR description
- [ ] The T-27 clause is corrected, not hedged
- [ ] The false "tag (the column UId)" claim is corrected in **both** repositories
- [ ] Tests: AAA, `because` on every assertion, `[Description]` on every method, `[Category("Unit")]`
- [ ] No new CLI flag (kebab-case rule vacuous)
- [ ] Validated locally: `dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-nf`
      in the **main checkout, not a worktree**; for the clio-side comment,
      `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=ProcessModel" --no-build`
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
