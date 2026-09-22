# Story 6: The refusals that stop a write from reporting success and vanishing

**Feature**: eng-99856-multi-instance
**FR coverage**: FR-13, FR-14, FR-15
**AC coverage**: AC-10, AC-11, AC-12
**PRD**: [prd-eng-99856-multi-instance.md](../prd/prd-eng-99856-multi-instance.md)
**ADR**: [adr-eng-99856-multi-instance.md](../adr/adr-eng-99856-multi-instance.md) — D6, D7
**Contract**: [eng-99856-multi-instance-contract-answers.md](../eng-99856-multi-instance/eng-99856-multi-instance-contract-answers.md) — Q4
**Platform facts**: [eng-99856-multi-instance-platform-facts.md](../eng-99856-multi-instance/eng-99856-multi-instance-platform-facts.md) — §1.6, §3, §5
**Jira**: ENG-99856
**Status**: review
**Size**: M (half day)
**Repo**: crt-process-builder — `packages/CrtProcessBuilder/Files/src/cs/Mappings/`
**Depends on**: stories 2, 5 (the refusal has to recognise a target *at any depth*, which needs the dotted
resolver)
**Blocks**: stories 11, 12, 14 (the guidance text quotes these messages)

---

## As a

AI agent writing mappings into a multi-instance element

## I want

to be refused, loudly and with the working alternative named, when I target the output collection

## So that

I do not report success on a write the platform erases on the next synchronization

---

## Owner decision this story assumes

**OQ-04 — output-collection strictness.** Working assumption: **refuse** (the architect's recommendation,
and the load-bearing refusal of the feature).
*If the owner decides accept-and-warn instead*, AC-01/AC-02 become a warning path: the write proceeds, the
notice must say the value will be erased on the next synchronization, and the E2E fixture (story 13)
changes from "refused" to "warned". Nothing else in the feature moves.

**OQ-05 — does `validate-process-graph` grow a rule?** Working assumption: **no rule in v1**. These
refusals live where the write happens; `ProcessGraphNode` is `(string Name, string Type)`
(`clio/Command/ProcessModel/IProcessGraphValidator.cs:10`) with no multi-instance field, there is no
package-side validator behind the tool, and all 869 lines of rules live in clio's `ProcessGraphValidator`
— so any rule there is **new surface**, not an update, and it would duplicate these refusals in a
validator that cannot see the element's real state.
*If the owner decides otherwise*, a separate story is needed in clio (not here), and it must first extend
the node model to carry multi-instance at all.

## Acceptance Criteria

- [ ] **AC-01** (AC-10/FR-13) — Given a mapping whose target is inside `OutputRecordCollection` **at any
  depth and of any direction**, when applied, then clio prints `Error: {message}`, exits non-zero and
  nothing is written.
- [ ] **AC-02** (AC-10/FR-13) — Given that message, when a caller reads it, then it names the shape ("the
  called process produces these; the caller reads them") **and** the working alternative: map **from** it,
  by naming this element and this path as the mapping **source**.
- [ ] **AC-03** (FR-13) — Given a target at depth 2 or 3 inside the output collection whose direction is
  `Variable` (the default when `L12` is absent), when applied, then it is **still refused**. This is the
  case that makes the refusal necessary: 133 shipped `Variable` entries sit in the output collection and
  they are precisely the XOR twins `FillCollectionParameters` wipes in place
  (`ProcessSchemaActivity.cs:346`) and `LoadCollectionParameters` never reads back (`:362`, it filters the
  output side to `Direction == Out`). The existing direction guard returns early for `Variable` and would
  accept every one of them.
- [ ] **AC-04** (AC-11/FR-14) — Given a mapping targeting an `Out`- or `Internal`-direction item property
  of the **input** collection, when applied, then the refusal message is **string-equal** to the existing
  `EnsureSubProcessTargetCanHoldAValue` text (`Mappings/ProcessMappingService.cs:76-96`). Reuse it
  verbatim; it is already correct and already names the map-from alternative.
- [ ] **AC-05** (AC-12/FR-15, the anti-refusal) — Given a callee declaring an `Internal`-direction
  parameter, when the element is converted, then conversion **succeeds** and a notice names the dropped
  parameter. Refusing would make 12 of 327 shipped single-instance sub-process elements permanently
  unconvertible, including production Copilot flows.
- [ ] **AC-06** (FR-15) — Given that notice, when its text is reviewed, then it does **not** claim what
  happens to the parameter afterwards beyond what is established: `FillCollectionParameters` has no
  `Internal` branch (`:336-355`), so the parameter is routed nowhere and one orphan mapping row is leaked
  per synchronization, against a 3.0 % corpus-wide orphan baseline from unrelated causes, and nothing
  reads those rows. The `BulkDeleteOldFiles` counter-example (the designer client apparently re-adding it)
  is **inference, medium confidence** (A-08) — the notice must not repeat it as fact.
- [ ] **AC-07** (G4 counter-metric) — Given the refusals, when the existing single-instance suites run,
  then **no currently-writable element becomes unwritable** except the deliberate output-collection case;
  a test enumerates what changed.
- [ ] **AC-ERR** — Given any of these refusals, when the batch runs, then clio prints `Error: {message}`,
  exits non-zero, and **nothing is persisted**.

## Implementation Notes

Key file: `Files/src/cs/Mappings/ProcessMappingService.cs` — `ApplyMapping` (`:47-61`),
`EnsureSubProcessTargetCanHoldAValue` (`:76-96`), `ResolveTargetParameter` (`:103-126`).

**How to recognise "inside the output collection".** The element's `MultiInstanceOptions.OutputCollectionParameterUId`
names the root; resolution already descends from a named root (story 5), so the refusal is a property of
the **root segment that was resolved**, not of a name match. Do not string-match
`"OutputRecordCollection"` — the name is conventional, not enforced, and an element built by another tool
can carry a different one.

**Why the existing guard cannot be reused for this.** An absent `L12` means `Variable`
(`ProcessSchemaParameter.cs:1009`; enum `In=0, Out=1, Variable=2, Internal=3` at `:34-55`), so 278 of 391
shipped item properties are Variable by absence and the direction test admits 353 of 391 — and
`EnsureSubProcessTargetCanHoldAValue` returns early for `Variable`. The guard reused unchanged
**accepts a write the platform erases**. Corpus check behind the refusal: 0 of 170 shipped output item
properties carry a value.

**A depth-1 hole in the existing guard, for context and not for fixing here.** 12 588 depth-1 parameters
corpus-wide (222 on single-instance sub-process elements) ship with no `IL2`, i.e. `Guid.Empty`, and
`EnsureSubProcessTargetCanHoldAValue` (`:78-80`) returns early on exactly that — silently treating an
element parameter as a process parameter and skipping the refusal. Nested parameters are unaffected
(508/508 carry a correct `IL2`). If you fix it here, say so explicitly in the PR and prove the blast
radius; otherwise record it and leave it.

**Notices, not exceptions, for FR-15.** Follow the package's existing notice/warning collector (the same
surface the implicit-parallel-split notice uses); do not invent a second reporting channel.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` (package) | output-collection refusal at depth 1, 2 and 3; `Variable`-direction case (AC-03); message names shape + map-from alternative | `tests/UnitTests/CrtProcessBuilder.Tests/MultiInstanceMappingTests.cs` |
| Unit `[Category("Unit")]` (package) | input-collection `Out`/`Internal` refusal is **string-equal** to the existing constant (assert against the constant, not a copied literal) | same |
| Unit `[Category("Unit")]` (package) | `Internal`-callee conversion succeeds and emits a notice naming the parameter; notice text carries no unproven claim | `tests/UnitTests/CrtProcessBuilder.Tests/MultiInstanceApplierTests.cs` |
| E2E | one refusal round-trip over the real MCP protocol | `clio.mcp.e2e/SubProcessMultiInstanceToolE2ETests.cs` (story 13) |

Test naming: `ApplyMapping_ShouldRefuseTarget_WhenItIsInsideTheOutputCollectionAtDepthThree`.

## Definition of Done

- [ ] The output-collection refusal is rooted in the options' UId, not a name string
- [ ] The input-collection refusal asserts against the existing message constant
- [ ] The `Internal`-callee path converts and notices; no over-claim in the text
- [ ] OQ-04 and OQ-05 assumptions restated in the PR with their one-line consequences if reversed
- [ ] No currently-writable element becomes unwritable except the deliberate case; the diff is enumerated
- [ ] Tests: AAA, `because` on every assertion, `[Description]` on every method, `[Category("Unit")]`
- [ ] No new CLI flag (kebab-case rule vacuous)
- [ ] Validated locally: `dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-nf`
      in the **main checkout, not a worktree**
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
