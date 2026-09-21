# Story 5: Map the collection and each item — the dotted name path, and the caller stamp

**Feature**: eng-99856-multi-instance
**FR coverage**: FR-10, FR-11, FR-12
**AC coverage**: AC-07, AC-08, AC-09, AC-17 (its unit mirror)
**PRD**: [prd-eng-99856-multi-instance.md](../prd/prd-eng-99856-multi-instance.md)
**ADR**: [adr-eng-99856-multi-instance.md](../adr/adr-eng-99856-multi-instance.md) — D4
**Contract**: [eng-99856-multi-instance-contract-answers.md](../eng-99856-multi-instance/eng-99856-multi-instance-contract-answers.md) — Q2
**Platform facts**: [eng-99856-multi-instance-platform-facts.md](../eng-99856-multi-instance/eng-99856-multi-instance-platform-facts.md) — §5, §6
**Jira**: ENG-99856
**Status**: ready-for-dev
**Size**: M (half day — one resolver method, one reuse, and the stamp test that guards the most expensive
failure mode in this feature)
**Repo**: crt-process-builder — `packages/CrtProcessBuilder/Files/src/cs/`
**Depends on**: story 2 (typed fixtures). Story 3 is not strictly required — the resolver change is
independent of the applier — but the happy-path test reads better over a converted element.
**Blocks**: stories 6, 11, 12

---

## As a

Creatio developer (or agent) binding data into a multi-instance element

## I want

to bind the collection to iterate with the `addMapping` operation I already use, and to address one item
property by naming a path

## So that

the callee receives per-item values without my learning `L18`, `IL2` and the XOR rule

---

## Acceptance Criteria

- [ ] **AC-01** (AC-07/FR-11) — Given a designer-converted multi-instance element, when `addMapping`
  targets `elementParameter: "InputRecordCollection"` with a `CompositeObjectList` source, then the
  mapping is persisted on that parameter's `SourceValue` and `describe-business-process` reports it —
  **with no code change on that path**. This AC is a confirmation test, not an implementation: the whole
  chain already resolves (`MappingOperations.cs:28-33` → `ProcessMappingService.ApplyMapping:47-61` →
  `ResolveTargetParameter:103-126` → `ProcessSchemaElementLocator.ResolveElementParameter:107-121`), the
  direction guard passes it (`In`), and `ParameterTypeCompatibility` admits it by the exact-UId
  fall-through (`:188`).
- [ ] **AC-02** (AC-08/FR-12) — Given the same element, when `addMapping` targets
  `"InputRecordCollection.OrderId"` from `"ReadOrders"` / `"ResultCompositeObjectList.Id"`, then **both**
  sides resolve by descending `ItemProperties`, each segment compared `OrdinalIgnoreCase`.
- [ ] **AC-03** (AC-09, the counter-metric) — Given an element parameter whose **flat** name itself
  contains a `.` and resolves flat today, when `addMapping` targets it, then flat resolution still wins and
  the result is byte-identical to the pre-change behaviour. Flat is tried first, always.
- [ ] **AC-04** (FR-12) — Given a dotted path of depth 3, when it is resolved, then it resolves; depth is
  **unbounded** (shipped content reaches three levels and the platform's reader has no depth counter).
- [ ] **AC-05** (FR-12) — Given a dotted path whose segment does not resolve, when applied, then clio
  prints `Error: {message}` naming **which segment** failed and exits non-zero. No name is ever
  synthesised — the runtime binds item properties by **case-sensitive** name
  (`ProcessSchemaParameterUtils.cs:67-70`, `p.Name == name`), so inventing a name would produce a mapping
  that resolves at design time and binds to nothing at run time.
- [ ] **AC-06** (AC-08/AC-17/FR-10) — Given any mapping written into an item property, when the written
  `SourceValue` is inspected, then `ModifiedInSchemaUId == element.ParentMetaSchema.UId` — the **caller's**
  schema. Asserted by its own test, named so a reviewer can find it.
- [ ] **AC-07** (FR-10) — Given the implementation, when it writes an item-property mapping, then it does
  so through `ProcessMappingService.BuildSourceValue` (`:152-154`) and **not** through a second write path.
  A grep in the PR description shows there is exactly one construction site.
- [ ] **AC-ERR** — Given an unresolvable dotted segment, a source that does not resolve, or an incompatible
  type, when the batch runs, then clio prints `Error: {message}`, exits non-zero and **nothing is
  persisted**.

## Implementation Notes

### The resolver — one method, both sides

`ProcessSchemaElementLocator.ResolveElementParameter` (`:107-121`) gains: try the whole string **flat**
first (today's behaviour, unchanged); on a miss, split on `.` and descend `ItemProperties` recursively,
each segment `OrdinalIgnoreCase` (matching the existing comparison at `:112`).

That one method is called for the **target** at `ProcessMappingService.cs:124-125` and for the **source**
at `:289-290`. This is the argument that settled the design against a `collection: "input"|"output"`
discriminator: a discriminator can only address the target, while the canonical per-item *source* is a
column of another element's collection output. The dotted form serves both, costs **zero** new wire
members, and needs no clio mirror — `ProcessMappingDescriptor` has no `[JsonExtensionData]`
(`ProcessDescriptorContracts.cs:2023-2070`), so any new member there would have been a two-sided change.

**Rejected, do not reintroduce**: `collectionFromElement` / `collectionFromElementParameter` — two new
DataMembers renaming `sourceElement` / `sourceElementParameter` for an operation that already exists.

### The caller stamp is a RUNTIME requirement, not bookkeeping

`ElementParametersReaderOptions.UseOnlyModifiedParameters` defaults to `true`
(`IProcessParameterValueProvider.cs:85`), `ProcessComponentSet.InternalStartAsSubprocess` leaves it there
(`:1289-1296`), and `CreateParameterValueReader` filters the substituted item properties by exactly
`SourceValue.ModifiedInSchemaUId == element.ParentMetaSchema.UId`
(`ProcessParameterValueProvider.cs:739-742`); no match yields `Option.None` (`:743-745`) and the
sub-process **receives no values at all** — while the element saves, describes and renders perfectly.
That is the most expensive failure mode in this feature (PRD A-09), which is why the stamp gets AC-06 and
AC-07 to itself and why `BuildSourceValue` is reused rather than re-implemented.

### Two platform facts that bound the work

- At run time the input collection's item properties **are** the element's parameters: the provider
  substitutes the whole set (`ProcessParameterValueProvider.cs:719-723`) and pulls the per-iteration value
  out of the iteration's `CompositeObject` **by name**. Keys with no matching output item property are
  silently skipped.
- An **absent** `ProcessSchemaMapping` (`BK15`) row is not harmless bookkeeping even though nothing at run
  time reads one: `GetRemovedSchemaParameters` (`ProcessSchemaActivity.cs:253-270`) deletes a flattened
  parameter that has no row and whose `CreatedInSchemaUId == SchemaUId`, and the platform re-creates it
  with a **new UId**. Write the row the way the existing path writes it; do not "optimise" it away.

## Owner decision this story assumes

**OQ-07 — a collection SHAPE check.** Working assumption: **out of v1**.
`ParameterTypeCompatibility` compares only `DataValueTypeUId`, so two `CompositeObjectList` parameters
with entirely different item properties validate today, and whether the platform then fails at run time or
silently yields empty items is **not established**.
*If the owner decides the other way*, this story grows a shape comparison between source and target item
properties plus a refusal, and the unknown has to be measured on a stand first — do not add a check whose
failure mode nobody has measured. Record the known unknown in the PR either way.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` (package) | flat-first precedence (AC-03); dotted target; dotted source; depth 3; unresolvable segment names the segment; case-insensitive segment matching | `tests/UnitTests/CrtProcessBuilder.Tests/MultiInstanceMappingTests.cs` |
| Unit `[Category("Unit")]` (package) | **the caller stamp** on every written item-property `SourceValue` (AC-06), and the single-construction-site pin (AC-07) | same |
| Unit `[Category("Unit")]` (package) | the collection-level binding still works with no code change (AC-01) | same |
| E2E | map the collection, then map one item, then describe — story 13's fixture | `clio.mcp.e2e/SubProcessMultiInstanceToolE2ETests.cs` |

Test naming: `ResolveElementParameter_ShouldPreferFlatMatch_WhenNameContainsADot`,
`ApplyMapping_ShouldStampCallerSchemaUId_WhenTargetIsAnItemProperty`.

## Definition of Done

- [ ] Flat resolution is tried first and is byte-identical to today (AC-09 pinned)
- [ ] One resolver serves target and source; no new wire member anywhere
- [ ] Every item-property mapping goes through `BuildSourceValue`; the stamp has its own named test
- [ ] The unresolvable-segment message names the failing segment
- [ ] OQ-07's known unknown is recorded in the PR description
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
