# Story 2: Declared item shape for Collection process parameters

**Feature**: eng-95984-file-parameter-type (follow-up)
**Jira**: ENG-103162 - Sub-task of ENG-103304 File and collection process parameters: follow-ups (created 2026-10-14
under ENG-95984 File process parameter type and moved there the same day, so that ENG-95984 can close)
**Spec**: [spec-eng-95984-file-parameter-type.md](../prd/spec-eng-95984-file-parameter-type.md) (non-goals)
**ADR**: [adr-eng-95984-file-parameter-type.md](../adr/adr-eng-95984-file-parameter-type.md) - D2, rejected option C
**Status**: deferred (moves to ready-for-dev when its slot opens and its criteria are refined)
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

## Acceptance sketch (to be refined)

- [ ] A declared Collection with `itemProperties` creates exactly those items (name, type, direction; no Tag;
      `ContainerUId` of the process), and describe reads them back so the output rebuilds the same shape.
- [ ] `type: FileCollection` produces metadata identical to a Collection declared with one item `File: File`
      (FileLocator, Variable), so the alias and the general form cannot drift.
- [ ] An item type a process parameter cannot hold (Binary, BLOB) and a nested collection beyond the supported depth
      are refused with a message that names the item.
- [ ] An older server cannot silently drop the shape: the clio floor moves, and the e2e proves the shape survives a
      describe read-back on the cut.
- [ ] The `parameters` guide states the general form; the description bytes are paid by a reviewed swap.

## Test sketch

Package unit cases in the shape of the ENG-95984 test plan's FileCollection cases (build, describe, rebuild); one clio
unit case for the DTO pass-through of `itemProperties` on write; one MCP e2e create plus read-back case. Test ids are
added to [tp-eng-95984-file-parameter-type.md](../test-plans/tp-eng-95984-file-parameter-type.md) when the criteria are
refined.

## Definition of Done

- [ ] Criteria refined with the owner and the status moved to ready-for-dev in `spec/sprint-status.yaml`.
- [ ] The repository rules of story 1 apply unchanged (package fixture categories, AAA, `because:`, `[Description]`,
      floor = the final cut, MCP and docs review statements, review gates, only a human merges).
