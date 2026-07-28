# Story 3: Inherited-column caption override — relax the read-only guard (caption/description only)

**Feature**: entity-schema-authoring-gaps
**FR coverage**: FR-03 (caption-only modify of inherited column persisted on child), FR-04 (relax `FindOwnColumnForMutation`; name/type/flags stay rejected), FR-10 (clear error on disallowed inherited mutation)
**PRD**: [prd-entity-schema-authoring-gaps.md](../prd/prd-entity-schema-authoring-gaps.md)
**ADR**: [adr-entity-schema-authoring-gaps.md](../adr/adr-entity-schema-authoring-gaps.md)
**Jira**: [ENG-93040](https://creatio.atlassian.net/browse/ENG-93040) (epic ENG-85256)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: Story 2 (`story-entity-schema-authoring-gaps-2`) — reuses the shared save/publish/verify pipeline extracted there and the inherited-verify plumbing

---

## As a

Creatio developer (toolkit user) / AI no-code agent doing a base-object rebrand (e.g. Case→Tickets: Symptoms→Description, Solution→Resolution Notes, ClosureDate→Closed Date/Time)

## I want

`modify-entity-schema-column` / `update-entity-schema` to accept a caption-only change to an inherited column on a replacing/child schema

## So that

I can rebrand a base object without redefining columns or touching the parent schema

---

## Acceptance Criteria

- [ ] **AC-03** (G2) — Given a replacing/child schema with inherited column `<C>`, when `modify-entity-schema-column` (or `update-entity-schema`) overrides `<C>`'s caption with new title-localizations, then the override is persisted keyed `<Schema>.Columns.<C>.Caption` on the child schema (sent in `inheritedColumns` with `isInherited:true`, unchanged `uId`/`name`/`type`), readback shows the new caption, and reading the parent schema shows its caption unchanged.
- [ ] **AC-04** (G2/FR-04) — Given an inherited column, when the caller attempts to change its **name, type, or flags** (any of `NewName`, `Type`, `ReferenceSchemaName`, `Required`, `Indexed`, `Cloneable`, `TrackChanges`, default-value*, `MultilineText`, `LocalizableText`, `AccentInsensitive`, `Masked`, `FormatValidated`, `UseSeconds`, `SimpleLookup`, `Cascade`, `DoNotControlIntegrity`), then clio prints `Error: Column '<C>' is inherited; only its caption and description can be overridden. Its name, type, and flags are read-only.` and exits non-zero.
- [ ] **AC-ERR** (FR-10) — Given a disallowed inherited-column mutation, clio prints `Error: {message}` and exits non-zero (as AC-04).
- [ ] **Counter-metric (G2)** — Given existing unit tests asserting inherited immutability of non-caption properties, then none start failing (the guard still rejects name/type/flag changes).
- [ ] **Verify fix** — Given an inherited caption override, when `VerifyColumnMutation` runs for a `Modify`, then it accepts the column in `Columns` **OR** `InheritedColumns` and asserts the reloaded inherited column's caption equals the requested value in the effective culture (en-US fallback).

## Implementation Notes

From ADR "Inherited caption-override guard" + "Verify fix" (OQ-03 resolved — no new localizable shape; reuse `ApplyColumnCaptionAndDescription`).

- `clio/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManager.cs`:
  - Replace the hard throw in `FindOwnColumnForMutation` (~L751 `"...inherited and read-only in v1."`) with an inherited-aware resolver: own column wins; else inherited column; else not-found.
  - In `ModifyColumn`: if the target is **inherited** and the requested modify is **caption/description-only** (`Title`/`TitleLocalizations`/`Description`/`DescriptionLocalizations` set and none of the mutating fields listed in AC-04 present) → apply **only** `ApplyColumnCaptionAndDescription` **in place** on the `InheritedColumns` entry (keep `uId`/`name`/`type`; do NOT move it to `Columns`). Reuse the existing `NormalizeTitleLocalizations` → `ApplyColumnCaptionAndDescription` path verbatim.
  - If the target is inherited and the modify is NOT caption-only → throw the AC-04 message.
  - Fix `VerifyColumnMutations`/`VerifyColumnMutation`: a `Modify` must accept the column in `Columns` **OR** `InheritedColumns` (currently checks `Columns` only — would falsely fail an inherited override); additionally assert the reloaded inherited column caption matches the requested value in the effective culture with en-US fallback.
  - Reuse the shared save/publish/verify helper extracted in Story 2 (do not re-inline the pipeline).
- `clio/Command/ModifyEntitySchemaColumnCommand.cs` — no validation change (inherited handling lives in the manager); note inherited caption-override acceptance in `HelpText`/docs.
- `clio/Command/McpServer/Tools/EntitySchemaTool.cs` — note inherited caption-override support in the modify/update column tool `[Description]`.
- Docs: `clio/help/en/{modify-entity-schema-column,update-entity-schema}.txt`, `clio/docs/commands/{modify-entity-schema-column,update-entity-schema}.md`, `clio/Commands.md`, `clio/Wiki/WikiAnchors.txt`.

Key file: `clio/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManager.cs`
Pattern to follow: existing `NormalizeTitleLocalizations` → `ApplyColumnCaptionAndDescription`; the `InheritedColumns` round-trip already in place.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Inherited caption-only modify → allowed, applied in place on `InheritedColumns`, `uId`/`name`/`type` unchanged | `clio.tests/Command/RemoteEntitySchemaColumnManagerTests.cs` |
| Unit `[Category("Unit")]` | Inherited name/type/flag modify → rejected with the AC-04 message (counter-metric) | `clio.tests/Command/RemoteEntitySchemaColumnManagerTests.cs` |
| Unit `[Category("Unit")]` | `VerifyColumnMutation` accepts inherited caption override (checks `InheritedColumns`, effective-culture caption match, en-US fallback) | `clio.tests/Command/RemoteEntitySchemaColumnManagerTests.cs` |
| Unit `[Category("Unit")]` | Modify/update column tool `[Description]` mentions inherited caption override | `clio.tests/Command/McpServer/EntitySchemaToolTests.cs` |
| E2E `[Category("E2E")]` (manual — not in CI) | inherited caption override + parent unchanged; deferred to Story 4's consolidated E2E suite | `clio.mcp.e2e/EntitySchemaToolE2ETests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`. AAA + `because` + `[Description]`; NSubstitute mocks.

Targeted run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer)"`

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO005); no new `CLIO*` warnings in modified files
- [ ] No new CLI flags; existing caption arguments now apply to inherited columns (widening, no rename)
- [ ] Inherited immutability of name/type/flags preserved — existing counter-metric tests still pass
- [ ] Unit tests added with `[Category("Unit")]`; AAA + `because` + `[Description]`
- [ ] Shared save/publish/verify pipeline from Story 2 reused (not re-inlined)
- [ ] All Creatio HTTP via `IApplicationClient` (no raw `HttpClient`)
- [ ] MCP surface updated: modify/update tool `[Description]` notes inherited caption override + unit test; `clio.mcp.e2e` coverage consolidated in Story 4 (note this dependency in the PR)
- [ ] Docs updated: `help/en`, `docs/commands`, `Commands.md`, `Wiki/WikiAnchors.txt` for the touched commands
- [ ] Targeted unit filter passes; command recorded in PR description
- [ ] Agentic code review (parallel quality/correctness/security) run before opening the PR; Blocker/High findings resolved
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-07-09
- Implementation completed: 2026-07-09
- Tests passing: yes — `dotnet test --filter "Category=Unit&(Module=Command|Module=McpServer)"` → 3728 passed, 0 failed, 15 skipped
- Notes: Replaced the hard `FindOwnColumnForMutation` throw with `FindColumnForMutation` (own→inherited, returns IsInherited; throws only on not-found). `ModifyColumn` branches: inherited target → `ApplyInheritedColumnCaptionOverride` (rejects any non-caption change via `HasNonCaptionInheritedMutation` with the AC-04 message; requires a caption/description change; applies `ApplyColumnCaptionAndDescription` in place on the InheritedColumns entry — uId/name/type unchanged, not moved to own). `RemoveColumn` now rejects inherited with "inherited and cannot be removed." Fixed `VerifyColumnMutations`/`VerifyColumnMutation` to accept the reloaded column from Columns OR InheritedColumns, and added `VerifyInheritedCaptionOverride` + `ResolveExpectedCaption` (effective-culture match, en-US fallback) so a silent no-op → clear error. Threaded effectiveCultureName into verification. MCP: modify + update tool `[Description]`s note inherited caption override. Docs: help/en + docs/commands for modify/update (replaced the stale "own columns only; inherited read-only" note). Tests: rewrote the obsolete `ModifyColumn_Throws_WhenColumnIsInherited` into caption-allowed / non-caption-rejected / remove-rejected / verify-mismatch; added tool-description assertion. Reused Story 2's `SaveAndReloadSchema`. Not committed yet.
