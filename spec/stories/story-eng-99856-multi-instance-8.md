# Story 8: De-conversion — `enabled: false` turns a multi-instance element back into a single one

**Feature**: eng-99856-multi-instance
**FR coverage**: FR-25
**AC coverage**: none of the numbered ACs — this story is entirely OQ-01-contingent and must be dropped
whole if the owner says no
**PRD**: [prd-eng-99856-multi-instance.md](../prd/prd-eng-99856-multi-instance.md)
**ADR**: [adr-eng-99856-multi-instance.md](../adr/adr-eng-99856-multi-instance.md) — OQ-01
**Platform facts**: [eng-99856-multi-instance-platform-facts.md](../eng-99856-multi-instance/eng-99856-multi-instance-platform-facts.md) — §1.7, §4
**Jira**: ENG-99856
**Status**: ready-for-dev
**Size**: M (half day)
**Repo**: crt-process-builder — `packages/CrtProcessBuilder/Files/src/cs/Elements/`
**Depends on**: story 3 (the applier and its interface)
**Blocks**: stories 12, 14 (the guidance and the tool text either document de-conversion or do not)

---

## Owner decision this story assumes

**OQ-01 — does de-conversion ship in v1?** Working assumption: **yes, it ships** (the architect's
recommendation: the alternative route back is strictly more destructive).
*If the owner says no*, **this whole story is deleted**, `enabled: false` becomes an explicit refusal
naming `removeElement` + `addElement` as the only route (one small AC folded into story 4), story 7's
retarget message loses its "de-convert first" branch, and the guidance in story 14 says de-conversion is
not offered. Nothing else in the feature moves.

## As a

Creatio developer who converted an element and needs to undo it

## I want

`multiInstanceOptions.enabled: false` to turn the element back into a single-instance one

## So that

I do not have to delete and re-add the element, which changes its UId and drops its flows and mappings

---

## Acceptance Criteria

- [ ] **AC-01** — Given a multi-instance element, when a `setElement` carries
  `subProcess.multiInstanceOptions.enabled = false`, then the saved element carries **no** `BP6`, the five
  root parameters are gone, and the callee's parameters are back as flat element parameters.
- [ ] **AC-02** — Given that de-conversion, when the restored parameters are inspected, then **all** input
  item properties return and **only** `Out`-direction output item properties return — the platform's own
  rule (`LoadCollectionParameters`, `ProcessSchemaActivity.cs:356-368`, filters the output side to
  `Direction == Out` at `:362`), which is what the designer's `convertToSingleInstance` also does
  (`process-activity-schema.js:601-609`).
- [ ] **AC-03** — Given a `Variable`-direction parameter that was routed to **both** collections on
  conversion, when the element is de-converted, then the XOR-derived output twin is **discarded** and the
  original comes back from the input side with its `SourceValue` intact. This is lossy in exactly the way
  the designer is lossy, and the tool text says so (story 13).
- [ ] **AC-04** — Given de-conversion, when it completes, then the element **UId is unchanged** and its
  sequence flows are untouched. That is the whole reason this operation exists instead of
  `removeElement` + `addElement`.
- [ ] **AC-05** — Given an element that is **not** multi-instance, when `enabled: false` is sent, then the
  operation is a no-op reported as such — not an error, and not a silent success that hides a typo'd
  element name. (The element-name miss itself is still the existing refusal.)
- [ ] **AC-06** — Given `enabled: false` in the same block as `executionMode` or `ignoreErrors`, when
  applied, then clio prints `Error: {message}` and exits non-zero: the caller is asking to remove the
  options and configure them at once.
- [ ] **AC-07** — Given de-conversion is a destructive write, when the tool contract is read (story 13),
  then it is marked and described as one, under the same explicit-intent rule as every other destructive
  write.
- [ ] **AC-ERR** — Given any refusal above, when the batch runs, then clio prints `Error: {message}`,
  exits non-zero and **nothing is persisted**.

## Implementation Notes

Key file: `Files/src/cs/Elements/MultiInstanceApplier.cs` (`IMultiInstanceApplier` from story 3 — this is
the third member of convert / update / de-convert).

The reference behaviour is the designer's: `convertToSingleInstance()`
(`process-activity-schema.js:601-609`) runs `_convertToSingleInstanceElementParameters()` and then sets
`multiInstanceOptions = null`, and it is pinned by the platform's own Jasmine spec
(`tests/process-activity-schema.unit.spec.js:406-437`). There is **no server-side conversion API** —
conversion and de-conversion are both client behaviours the package reproduces.

Order matters here as much as on the way in: restore the flat parameters **first**, then null
`MultiInstanceOptions`, and do not touch `SchemaUId` afterwards unless you intend the rebuild to run — an
assignment to it re-enters `SynchronizeParametersInternal` (`ProcessSchemaSubProcess.cs:63-71`), and on an
element that is no longer multi-instance that is the **ordinary** callee diff, which calls
`ClearParameters()` when `SchemaUId.IsEmpty()` and also removes the mappings of everything it clears
(`ProcessSchemaActivity.cs:508-514`, `:587-599`) — unlike the two bare `Clear()` calls in the rebuild.

Write the removal of a parameter the way the platform expects: `MetaItemCollection.RemoveItem` /
`ClearItems` **reset** `CreatedInSchemaUId` to `Guid.Empty` (`MetaItemCollection.cs:123`, `:133`), which
changes whether a later prune arm considers the parameter dynamic. Assert the surviving stamps.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` (package) | `BP6` gone and five roots gone; all input items restored; only `Out` output items restored; the XOR twin discarded; element UId and flows unchanged; no-op on a single-instance element; the `enabled:false` + mode-field refusal | `tests/UnitTests/CrtProcessBuilder.Tests/MultiInstanceApplierTests.cs` |
| E2E | convert → de-convert → describe round-trip | `clio.mcp.e2e/SubProcessMultiInstanceToolE2ETests.cs` (story 13) |

Test naming: `Apply_ShouldRestoreOnlyOutDirectionOutputItems_WhenDeconverting`.

## Definition of Done

- [ ] `enabled: false` de-converts; the element UId and flows survive
- [ ] The lossy `Variable`-twin behaviour is covered by a test and stated in the tool text (story 13)
- [ ] De-conversion is classified and described as a destructive write
- [ ] OQ-01's assumption and the "delete this story" consequence are restated in the PR description
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
