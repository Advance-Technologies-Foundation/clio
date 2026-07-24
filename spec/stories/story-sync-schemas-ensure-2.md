# Story 2: ensure-update-entity — per-column reconcile

**Feature**: sync-schemas-ensure-semantics
**ADR unit**: U2
**FR coverage**: FR-04, FR-05, FR-06
**PRD**: [prd-sync-schemas-ensure-semantics.md](../prd/prd-sync-schemas-ensure-semantics.md)
**ADR**: [adr-sync-schemas-ensure-semantics.md](../adr/adr-sync-schemas-ensure-semantics.md)
**Jira**: ENG-93807
**Status**: ready-for-dev
**Size**: M (half day)

---

## As a

developer / AI agent replaying a `sync-schemas` batch after an ambiguous network failure

## I want

`update-entity` to reconcile the requested column set against current server state per-column —
add-if-absent / modify-if-different / remove→ensure-absent — touching only the delta

## So that

an already-applied column change on replay is reported as success (not a failure), and columns I did
not name are never disturbed

---

## Acceptance Criteria

- [ ] **AC-05** — Given an `update-entity` op requesting columns that are already present and identical, when the batch runs, then no mutation is issued for those columns and the op is `success: true` with `outcome: already-satisfied`.
- [ ] **AC-06** — Given an `update-entity` op with a `remove` for a column that is already absent, when the batch runs, then the op treats it as "ensure absent" and returns `success: true`.
- [ ] **AC-07** — Given an `update-entity` op, when it runs, then columns not named in the request remain unchanged (additive per-column ensure; no full-reconcile deletion — OQ-02 out of scope).
- [ ] **AC-FR04** — Given a requested column absent on the server, it is added; given a requested column present but different (e.g. type differs), it is modified; the emitted mutation set equals exactly the computed delta (`outcome: reconciled`).
- [ ] **AC-FR06** — Given the reconcile path, when it runs, then the column detail read goes through `IApplicationClient` server-side inside the single batch call under the held lock — no new MCP round-trip, no new backend endpoint (1 read/op for `update-entity`).
- [ ] **AC-ERR** — Given an incompatible per-column modify-conflict, the per-op result carries `success: false` with a user-friendly `Error: {message}` and stop-on-first-failure is preserved.

## Implementation Notes

Wire `SchemaSyncTool.ExecuteUpdateEntity` through the `ISchemaConvergenceService` column read
introduced in Story 1 (this story depends on Story 1).

- Read column detail once via `GetEntitySchemaPropertiesCommand.GetSchemaProperties`
  (`IRemoteEntitySchemaColumnManager`) → `EntitySchemaPropertiesInfo` with
  `EntitySchemaPropertyColumnInfo` (`Name`, `Type`, `Required`, `ReferenceSchemaName`, `Source`).
- Per-column reconcile:
  - requested column absent → add (existing add path / `CreateEntitySchemaColumnArgs`);
  - requested column present but different → modify (`UpdateEntitySchemaOperationArgs`);
  - requested column present and identical → no-op;
  - requested `remove` and column absent → success (ensure-absent); present → issue remove.
- Emit ONLY the delta. Columns not named in the request are left untouched — do NOT compute a
  full-reconcile "make column set exactly equal request" (A-04; would delete user columns = data loss).
- Set the `outcome` discriminator on the per-op result (`reconciled` / `already-satisfied`).
- A per-column type/shape conflict is a modify-conflict here, surfaced as `Error: {message}` — NOT a
  whole-schema collision (that gate is Story 1, package-match only).

Key file: `clio/Command/McpServer/Tools/SchemaSyncTool.cs` (`ExecuteUpdateEntity`), reusing
`ISchemaConvergenceService` (Story 1) and `UpdateEntitySchemaCommand`.
Pattern to follow: Story 1's classify→delta→apply flow; existing `UpdateEntitySchemaCommand` usage.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | per-column add / modify / no-op / remove-already-absent / remove-present; unlisted columns untouched (AC-07); delta-only emission; modify-conflict → `success:false` + `Error:` | `clio.tests/Command/McpServer/SchemaSyncToolTests.cs` |
| E2E `[Category("E2E")]` | Deferred to Story 5 (existing-in-package reconcile → only-missing-added; real `mcp-server`, NOT in CI) | `clio.mcp.e2e/` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`. `[Property("Module","McpServer")]`; NSubstitute;
AAA with `because`; `[Description(...)]` per method; `[Category("Unit")]`.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001–CLIO005).
- [ ] `outcome` remains additive on the wire (`WhenWritingNull`); no full-reconcile deletion path added.
- [ ] MCP reviewed: `ExecuteUpdateEntity` result-shape change recorded ("MCP reviewed"); guidance/docs are Story 4.
- [ ] Error messages are user-friendly `Error: {message}`; stop-on-first-failure preserved.
- [ ] Unit tests added with `[Category("Unit")]` (never `UnitTests`).
- [ ] PR description references this story file.

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
  - **Add-shape type-only contract (intended):** the `update-entity` add / `columns` shape reconciles by
    column TYPE only. A present column with a matching type is treated as satisfied and dropped; to change any
    NON-type attribute (`required`, `reference-schema-name`, flags, caption/title-localizations) the caller must
    send an explicit `modify` op, which is always forwarded. This is deliberate — the column read does not expose
    every attribute, so a modify cannot be proven a no-op; a re-run to the same value is a backend no-op, never a
    failure.
  - **Type comparison is ordinal-normalized:** `create-lookup` reconcile and `update-entity` reconcile both compare
    column types through `EntitySchemaDesignerSupport.AreColumnTypesEquivalent` (resolves both the requested token
    and the server read-back to the canonical `DataValueType` ordinal, with a case-insensitive string fallback for
    unresolved tokens). This keeps replay idempotent for types whose read-back friendly name diverges from the
    request vocabulary (e.g. `phoneNumber`→`"42"`, `text50`→`ShortText`, `Float`).
