# Story 10: Report the callee's contract — the shape selected by story 1's gate

**Feature**: eng-99856-multi-instance
**FR coverage**: FR-18 (its second half)
**AC coverage**: AC-15 — **outcome-dependent**; under Outcome 2 it cannot be met as written and is
renegotiated with the owner
**PRD**: [prd-eng-99856-multi-instance.md](../prd/prd-eng-99856-multi-instance.md)
**ADR**: [adr-eng-99856-multi-instance.md](../adr/adr-eng-99856-multi-instance.md) — Decision 0, D0-b, D0-c, D1
**Jira**: ENG-99856
**Status**: deferred — **un-defer when story 1's gate outcome is written**. Do not start it on a guess.
**Size**: L as recorded (Outcome 2). **S if the gate answers Outcome 1.**
**Repo**: crt-process-builder (+ clio DTO mirror under Outcome 2)
**Depends on**: stories 1 (the gate), 9 (the block this hangs under)
**Blocks**: story 12 under Outcome 2 only — a new wire member must land **before** the rebundle or force a
second version bump

---

## As a

AI agent authoring mappings into a multi-instance element

## I want

to be told what the called process declares, and told plainly whether that is stored state or derived

## So that

I can write per-item mappings without opening the callee separately, and without round-tripping derived
data back into a write the platform erases

---

## The two shapes — pick by the gate, do not average them

### Outcome 1 — a cold instance reports `itemProperties`

Then FR-18 is **not** new projection code. Deliver:

- [ ] **AC-01** — A regression test pinning nested `itemProperties` reporting through the **real** describe
  path (not only the unit projection pinned in story 1).
- [ ] **AC-02** — A documented statement, in the tool text and in the guidance, that a described
  `itemProperties` reflects **the instance the server holds**, which an earlier write in the same process
  lifetime can have flattened (`ProcessSchemaActivity.cs:387-388` empties both collections in place, on
  the cached object graph; `SchemaManager.cs:3245-3254` caches for the process lifetime).
- [ ] **AC-03** — AC-15 is met as written: `ExpireLicenseNotificationProcess` / `SubProcess2` reports
  **4** item properties on `InputRecordCollection` and **6** on `OutputRecordCollection`, measured on a
  cold pool and recorded.
- [ ] **AC-04** — No load-path change, no cache invalidation from a read path (D0-a stands).

### Outcome 2 — the reachable load path never reports them

Then deliver the contingent view, and get it signed off first:

- [ ] **AC-05** — `multiInstanceOptions` gains a `calleeContract` member reporting the callee's declared
  parameters, split the way the platform routes them: `In` → input, `Out` → output, `Variable` → **both**
  (`FillCollectionParameters`, `ProcessSchemaActivity.cs:336-355`).
- [ ] **AC-06** — It is obtained **without touching the element**: `ProcessSchemaSubProcess.Schema`
  resolves the callee through `FindProcessSchemaByUId` (`ProcessSchemaSubProcess.cs:164-178`).
- [ ] **AC-07** — It is **labelled as derived**, in the member's XML doc, in the tool text and in the
  guidance — and it is **never accepted as a write**. Reporting derived data as stored state invites a
  caller to round-trip it into `ItemProperties`, which the platform owns and erases on every
  synchronization.
- [ ] **AC-08** — AC-15 is renegotiated in writing before implementation: "the multi-instance block
  reports the callee's contract, 4 input-side and 6 output-side", with the owner's sign-off recorded in
  the gate document.
- [ ] **AC-09** — The clio DTO mirrors the member typed, with XML docs, exactly as story 9 mirrors the
  block.
- [ ] **AC-10** — The member lands **before** story 12's rebundle, or story 12 bumps the package version a
  second time. A wire member that ships after the archive it belongs to is invisible to every installed
  environment.

### Both outcomes

- [ ] **AC-ERR** — Given an unresolvable callee, when describe runs, then the block reports the callee as
  unresolved (or omits the derived view) and the call **succeeds**. Describe has no refusals.

## Implementation Notes

Read the gate document first; it names the outcome and carries the evidence. Then read ADR D1's rejected
options and do not re-propose them:

| Rejected | Why |
|---|---|
| force the design-instance fallback in `LoadForDescribe` | both branches route through the same `CreateSchemaInstance` fork; it cannot recover what the instance lacks |
| force a metadata instance | `ForceUseInstanceFromMetaData` and `Find/GetInstanceFromMetaData` are `internal` to `Terrasoft.Core`; `FindRuntimeInstanceByUId` prefers the compiled instance and mutates manager state |
| invalidate the manager cache before a describe read | gives a read a write's blast radius on shared, process-lifetime state; races every concurrent caller |
| synthesise `itemProperties` from the callee into the parameter's own member | reports derived data as stored state; the caller round-trips it and the platform erases it |

The projection itself is at `Files/src/cs/Parameters/ProcessParameterService.cs:152-154`; the wire member
exists at `Contracts/DescribeContracts.cs:1391-1392` and clio declares it at
`clio/Command/ProcessModel/IProcessDescriber.cs:1750-1751`. Nothing downstream drops the field — that
half of the chain is already proven, so do not spend time re-proving it.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` (package) | Outcome 1: nested reporting through the describe path. Outcome 2: the derived view's routing by direction, including the `Variable`-into-both case, and its absence on an unresolvable callee | `tests/UnitTests/CrtProcessBuilder.Tests/SubProcessElementHandlerTests.cs` |
| Unit `[Category("Unit")]` (clio) | Outcome 2 only: the typed mirror deserializes; the derived label is present in the XML doc | `clio.tests/Command/McpServer/DescribeProcessMultiInstanceTests.cs` |
| Manual (stand, sequential) | AC-03's 4 and 6 on a cold pool | recorded in the gate document |

Test naming: `Describe_ShouldReportCalleeContractAsDerived_WhenElementIsMultiInstance`.

## Definition of Done

- [ ] The gate document is quoted in the PR description, naming the outcome this story implemented
- [ ] Under Outcome 2: the owner's AC-15 renegotiation is recorded **before** the code was written
- [ ] Derived data is labelled as derived on every surface that carries it, and never accepted as a write
- [ ] No load-path change, no cache invalidation, no synthesis into a stored member
- [ ] Tests: AAA, `because` on every assertion, `[Description]` on every method, `[Category("Unit")]`
- [ ] No new CLI flag (kebab-case rule vacuous)
- [ ] Validated locally: package —
      `dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-nf` in the
      **main checkout**; clio (Outcome 2) —
      `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=ProcessModel|Module=McpServer)" --no-build`
- [ ] PR description references this story file and states the AC-15 disposition

## Dev Agent Record

- Gate outcome this story was built against:
- Implementation started:
- Implementation completed:
- Tests passing:
- AC-15 disposition (met / renegotiated, with the owner's decision date):
- Notes:
