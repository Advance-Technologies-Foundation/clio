# Story 6: #910 integration — rebase, preserve resume-plan shape, shrink heuristics

**Feature**: sync-schemas-ensure-semantics
**ADR unit**: U6
**FR coverage**: FR-09 (SM-01c, SM-02c)
**PRD**: [prd-sync-schemas-ensure-semantics.md](../prd/prd-sync-schemas-ensure-semantics.md)
**ADR**: [adr-sync-schemas-ensure-semantics.md](../adr/adr-sync-schemas-ensure-semantics.md)
**Jira**: ENG-93807
**Status**: ready-for-dev
**Size**: L (full day)

---

## As a

maintainer of the `sync-schemas` tool and its downstream consumers

## I want

this convergent work rebased onto the merged PR #910 branch, preserving the resume-plan / partial-result
output shape while deleting the now-redundant heuristic branches for the convergent ops

## So that

existing consumers are not broken and the heuristic surface shrinks (not grows) as convergence subsumes
the guesswork

---

## Acceptance Criteria

- [ ] **AC-SHAPE (FR-09)** — Given the resume-plan / partial-result output shape introduced by PR #910, when this work is rebased on top, then that shape is preserved (existing consumers not broken).
- [ ] **AC-SHRINK (SM-01c/SM-02c)** — Given convergence now subsumes the #910 resume special-cases for the convergent ops (the reactive `TryGetCollisionInfo` probe was already replaced by the pre-emptive classification AND deleted in Story 1), when those #910 special-cases are removed here, then the heuristic branch count for the convergent ops shrinks, not grows — verified by the ambiguous-failure re-run class (Story 5) staying green after removal.
- [ ] **AC-GATE (A-06)** — This story is gated on PR #910 / ENG-93374 being merged; do not land the heuristic removal until the #910 resume-plan baseline is present.

## Implementation Notes

Sequencing depends on A-06 (this branch has the collision-probe but not #910's resume-plan yet).
Depends on Stories 1 and 2 (the convergent create/update paths must exist to know which heuristic
branches are now dead).

**Double-rewrite risk (why this is L, not M):** #910 independently rewrites `ExecuteCreateSchema` /
`ExecuteUpdateEntity` (per-op transient retry + resume-plan). This branch also rewrites those two
methods for convergence. Story 6 must therefore reconcile **two overlapping rewrites** of the same
methods and re-verify the preserved result shape — a substantial rebase, not a mechanical merge.

- Rebase onto the merged #910 branch; reconcile the two overlapping rewrites of `ExecuteCreateSchema` /
  `ExecuteUpdateEntity` (convergence vs. #910's per-op retry + resume-plan) and re-verify the preserved
  result shape.
- Preserve the resume-plan / partial-result result shape (additive `outcome` field must coexist with it).
- Delete the now-redundant #910 resume special-cases specific to `create-lookup` / `update-entity` that
  convergence makes unnecessary. Do NOT remove resume/retry machinery still needed by non-convergent ops
  (e.g. seed-data). **`TryGetCollisionInfo` is already deleted in Story 1** (its last caller is removed
  there) — Story 6 does NOT re-delete it; Story 6 owns ONLY the #910 resume special-case reconciliation.
- Keep Story 5's ambiguous-failure re-run class green after the removal (regression guard).

Key file: `clio/Command/McpServer/Tools/SchemaSyncTool.cs` (resume-plan / partial-result paths,
`TryGetCollisionInfo`).
Pattern to follow: the #910 resume-plan/result-shape code as the baseline to preserve.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | resume-plan/partial-result shape preserved after rebase; removed heuristic branches no longer reachable; re-run class still green | `clio.tests/Command/McpServer/SchemaSyncToolTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`; `[Property("Module","McpServer")]`; AAA + `because`;
`[Description(...)]` per method; `[Category("Unit")]`.

## Definition of Done

- [ ] Rebased onto merged #910; the two overlapping rewrites of `ExecuteCreateSchema`/`ExecuteUpdateEntity` reconciled; resume-plan / partial-result output shape preserved and re-verified.
- [ ] The redundant convergent-op #910 resume special-cases removed; heuristic surface shrinks (`TryGetCollisionInfo` was already removed with its last caller in Story 1 — not re-deleted here).
- [ ] Story 5 ambiguous-failure re-run class stays green after removal.
- [ ] ClioRing: additive-only confirmed; no Ring-consumed contract changed (per ADR — inspected, no `sync-schemas` reference).
- [ ] Code compiles without Roslyn analyzer warnings (CLIO001–CLIO005).
- [ ] Unit tests updated/added with `[Category("Unit")]` (never `UnitTests`).
- [ ] PR description references this story file.

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
