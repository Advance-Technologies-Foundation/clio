# Story 2: Declared item shape for Collection process parameters

**Feature**: eng-95984-file-parameter-type (follow-up)
**Jira**: ENG-103162 - Sub-task of ENG-103304 File and collection process parameters: follow-ups (created 2026-10-14
under ENG-95984 File process parameter type and moved there the same day, so that ENG-95984 can close)
**Spec**: [spec-eng-95984-file-parameter-type.md](../prd/spec-eng-95984-file-parameter-type.md) (non-goals)
**ADR**: [adr-eng-95984-file-parameter-type.md](../adr/adr-eng-95984-file-parameter-type.md) - D2, rejected option C
**Status**: in-progress (criteria refined with the owner on 2026-10-21; see "Owner decisions")
**Size**: M (inference from D2's costing of option C: about 150 LOC and 12 tests in the package, plus nested-type
rules, a clio surface and guidance; re-sized when refined)
**Repo**: crt-process-builder, clio, clio-knowledge (its own PR triple)
**Depends on**: story 1 (ENG-95984 File process parameter type) merged; **earliest slot: after ENG-96506 Generated
report + process parameter modes** (its package PR merged)
**Blocks**: nothing; it does not gate ENG-95984's closure

---

## Goal

Let `parameters[]` (create) and `addParameter` (modify) declare the item shape of a `Collection` process parameter
(generic `itemProperties` on write: item name, type and direction), so that a callee input or a script-filled list of
records can be declared with its columns. `FileCollection` then becomes sugar for a Collection with one item
`File: File`.

## Why it is out of ENG-95984

- ENG-95984 needs only one shape (one FileLocator item), which the `FileCollection` alias covers loudly: an older
  server refuses the unknown type name. A generic `itemProperties` member is silently DROPPED by an older server, which
  then saves a SHAPELESS collection green, so this story needs its own floor raise and a capability probe.
- About twice the package cost of the alias (nested-type rules, refusals for item types a process cannot hold), and
  about +100 B in each of the create and modify descriptions, whose budget was measured full on 2026-10-13.
- Owner decision of 2026-10-07 (D2): option A in ENG-95984, option C as this follow-up.

## Owner decisions (2026-10-21)

| # | Question | Decision |
|---|---|---|
| O-1 | Item types and depth | The designer's nested-item menu, which is the top-level type menu (`ProcessSchemaPropertiesPage._fillAddButtonMenuCollection`): the scalars, Lookup with `referenceSchema`, File, and Collection / FileCollection as an item of a top-level collection. Two collection levels at most: the items of a nested collection are not collections (the 7.8.0 corpus has 116 process-level collections with items, 617 items, at most two levels). Binary / Blob and a third level are refused, naming the item path |
| O-2 | Direction default of `type: Collection` with items | Unchanged Out; an item defaults to Variable, as the designer's nested Add writes it (614 of 617 shipped items) |
| O-3 | Write shape | `itemProperties: [{name, type, direction?, caption?, referenceSchema?, itemProperties?}]`; the members describe adds (`uid`, `source`, `isRequired`, `isResult`, `tag`, ...) are ignored, so a described collection replays as it is. A described binding (a value whose `source` is not None / ConstValue) is passed over, since bindings replay through mappings; a constant item value, a mirror on an item, `itemProperties` beside `typeFromElement` or on a non-collection are refused. `type: FileCollection` beside its OWN one-file shape is accepted (describe emits both); any other shape beside the alias is refused |
| O-4 | `setParameter` with `itemProperties` | Out of scope, refused loudly ("remove the parameter and add it again") instead of being dropped as an unknown member |

## Acceptance criteria (refined 2026-10-21; the test plan cites these numbers)

- [ ] **AC-1** A declared Collection with `itemProperties` creates exactly those items (name, type, direction; no Tag;
      `ContainerUId` of the process).
- [ ] **AC-2** `type: FileCollection` produces metadata identical to a Collection declared with one item `File: File`
      (FileLocator, Variable) at the same direction, so the alias and the general form cannot drift.
- [ ] **AC-3** Describe reads the shape back, and its output rebuilds the same shape (an unbound declared collection
      as described; a described FileCollection by its alias).
- [ ] **AC-4** An item type a process parameter cannot hold (Binary, BLOB) and a nested collection beyond the supported
      depth are refused with a message that names the item.
- [ ] **AC-5** An older server cannot silently drop the shape: the clio floor moves, and the e2e proves the shape
      survives a describe read-back on the cut.
- [ ] **AC-6** The `parameters` guide states the general form; the description bytes are paid by a reviewed swap.

## Test sketch

Package unit cases in the shape of the ENG-95984 test plan's FileCollection cases (build, describe, rebuild); one clio
unit case for the DTO pass-through of `itemProperties` on write; one MCP e2e create plus read-back case. Test ids are
added to [tp-eng-95984-file-parameter-type.md](../test-plans/tp-eng-95984-file-parameter-type.md) when the criteria are
refined.

## Definition of Done

- [x] Criteria refined with the owner (2026-10-21); the status moved straight to in-progress in
      `spec/sprint-status.yaml`, since implementation started the same day.
- [ ] The repository rules of story 1 apply unchanged (package fixture categories, AAA, `because:`, `[Description]`,
      floor = the final cut, MCP and docs review statements, review gates, only a human merges).
