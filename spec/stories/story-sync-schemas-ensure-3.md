# Story 3: seed-data contract decision — Name-keyed dedup (docs + regression test)

**Feature**: sync-schemas-ensure-semantics
**ADR unit**: U3
**FR coverage**: FR-08
**PRD**: [prd-sync-schemas-ensure-semantics.md](../prd/prd-sync-schemas-ensure-semantics.md)
**ADR**: [adr-sync-schemas-ensure-semantics.md](../adr/adr-sync-schemas-ensure-semantics.md)
**Jira**: ENG-93807
**Status**: ready-for-dev
**Size**: S (< 2h)

---

## As a

QA engineer / CI pipeline author re-running a `sync-schemas` batch that includes `seed-data`

## I want

an explicit, documented seed-data contract stating that `Name`-bearing rows (on a schema with a `Name`
column) are replay-safe and rows without a `Name` are not

## So that

I can test against a stated expectation and know when a seed row is safe to re-submit

---

## Acceptance Criteria

- [ ] **AC-08** — Given the resolved seed-data decision (OQ-01: keep `DataBindingDbCommand` / `CreateBinding`→`ProcessRows`, no upsert-by-key in committed scope), when a `seed-data` op with `Name`-bearing rows (target schema has a `Name` column) is re-run, then no duplicate rows are created (already-present-by-`Name` rows are reported as skipped), and when rows lack a `Name` (or the schema has no `Name` column) the contract documents them as non-convergent (a stable-`Id`, no-`Name` row PK-conflicts on replay).
- [ ] **AC-DOC** — The tool contract/docs/guidance state the `Name`-keyed dedup contract verbatim: "a row is replay-safe only when the target schema has a `Name` column AND the row carries a `Name`; rows without a `Name` (or schemas without a `Name` column) are non-convergent — a stable-`Id`, no-`Name` row PK-conflicts on replay." (The doc/contract-text edits themselves land in Story 4; this story provides the decision text and the regression proof.)

## Implementation Notes

No code change to the seed path. The seed path (`DataBindingDbCommand` → `CreateBinding`→`ProcessRows`)
dedups by `Name` (`SkippedRows`), so seed-data is *already* convergent for rows that carry a `Name`
when the target schema has a `Name` column; rows without a `Name` (or schemas without a `Name` column)
are NOT convergent — a stable-`Id`, no-`Name` row falls to the `InsertEntityRow` branch with the
explicit `Id` and causes a primary-key conflict on replay. This story resolves OQ-01 as the
"explicitly deferred" path SM-01 permits:

- Committed scope: keep `seed-data` on `DataBindingDbCommand` unchanged (the seed path calls
  `CreateBinding`→`ProcessRows`, which dedups by `Name`, NOT `IDataBindingDbService.UpsertRow`).
- Recommendation for a future additive story (NOT this one): route value-updating rows through
  `IDataBindingDbService.UpsertRow` (which keys on `Id`) or add an optional kebab `uniqueness-key` arg.
- Add a regression test proving replay with `Name`-bearing rows yields `SkippedRows` (no duplicates)
  and documenting rows without a `Name` as non-convergent (PK-conflict on replay).

Key file: `clio.tests/Command/McpServer/SchemaSyncToolTests.cs` (seed-replay regression);
seed path under test: `DataBindingDbCommand` (`CreateBinding`→`ProcessRows`) / `IDataBindingDbService`.
Pattern to follow: existing `DataBindingDbCommand` `CreatedRows`/`SkippedRows` (skip-by-`Name`) behavior.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | re-run with `Name`-bearing rows (schema has a `Name` column) → `SkippedRows` populated, zero duplicate mutation; no-`Name` row non-convergence (PK-conflict on replay) asserted/documented | `clio.tests/Command/McpServer/SchemaSyncToolTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`. `[Property("Module","McpServer")]`; NSubstitute;
AAA with `because`; `[Description(...)]` per method; `[Category("Unit")]`.

## Definition of Done

- [ ] Regression test proves `Name`-keyed replay (schema has a `Name` column) produces no duplicate rows; no-`Name` non-convergence is documented.
- [ ] The `Name`-keyed decision text is captured for Story 4 to stamp into contract/docs/guidance.
- [ ] Code compiles without Roslyn analyzer warnings (CLIO001–CLIO005).
- [ ] Unit test added with `[Category("Unit")]` (never `UnitTests`).
- [ ] PR description references this story file.

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
