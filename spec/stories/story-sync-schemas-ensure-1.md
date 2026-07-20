# Story 1: ensure-create — convergent create-lookup (+ shared create-entity)

**Feature**: sync-schemas-ensure-semantics
**ADR unit**: U1
**FR coverage**: FR-01, FR-02, FR-03, FR-06, FR-10
**PRD**: [prd-sync-schemas-ensure-semantics.md](../prd/prd-sync-schemas-ensure-semantics.md)
**ADR**: [adr-sync-schemas-ensure-semantics.md](../adr/adr-sync-schemas-ensure-semantics.md)
**Jira**: ENG-93807
**Status**: ready-for-dev
**Size**: L (full day)

---

## As a

developer / AI agent bootstrapping a Creatio application through the `sync-schemas` MCP tool

## I want

`create-lookup` (and the shared `create-entity` create path) to read the target schema's current
server state once, then create-if-absent and add only the missing requested columns

## So that

a pre-existing schema is reconciled instead of being silently accepted (dropping my columns) or
recreated, and a cross-package name collision is surfaced as an explicit failure rather than masked
as success

---

## Acceptance Criteria

- [ ] **AC-01** — Given a `create-lookup` op for a schema that does not exist, when the batch runs, then the lookup schema is created, its Lookups registration is ensured, and the per-op result is `success: true` with `outcome: created`.
- [ ] **AC-02** — Given a `create-lookup` op for a schema that already exists in the target package with a subset of the requested columns, when the batch runs, then only the missing columns are added, no recreation is attempted, `EnsureLookupRegistration` still runs, and the result is `success: true` with `outcome: reconciled` (or `already-satisfied` when nothing is missing).
- [ ] **AC-04** — Given a `create-lookup` op whose schema name collides with a schema in a DIFFERENT package (package-match gate, OQ-05) — OR a same-package schema whose parent/kind is incompatible with the requested lookup (e.g. a `BaseEntity`-derived entity vs. a `BaseLookup`) — when the batch runs, then the op fails with an explicit machine-readable collision error carrying `outcome: collision` and `collision-info` (owning package), and is NOT reported as success (never silently reconciled with lookup columns + a forced `EnsureLookupRegistration`).
- [ ] **AC-FR02** — Given a schema that already exists in the target package but whose Lookups registration is missing, when the op runs, then `EnsureLookupRegistration` runs unconditionally (not gated on the freshly-created branch) and the registration is reconciled.
- [ ] **AC-FR06** — Given any of the above paths, when it runs, then all state-reads go through `IApplicationClient` server-side inside the single existing batch call under the already-held `McpToolExecutionLock` — no new MCP round-trip and no new backend endpoint are introduced.
- [ ] **AC-ERR** — Given a durable collision (or otherwise invalid target), the per-op result carries `success: false` with a user-friendly `Error: {message}` string, and the batch preserves the existing stop-on-first-failure contract.

## Implementation Notes

Route `ExecuteCreateSchema` through a new DI service `ISchemaConvergenceService`; do NOT keep the
current unconditional `CreateEntitySchemaCommand.Execute` + reactive `TryGetCollisionInfo` probe.

- New file `clio/Command/McpServer/Tools/SchemaConvergenceService.cs`: `ISchemaConvergenceService`
  + impl, behavior class → interface, registered in DI (CLIO001; must have a real consumer wired in
  this same story so CLIO005 does not flag a dead registration — do NOT land the service and the
  wiring in separate stories).
- `Classify(SchemaConvergenceTarget)` returns a `SchemaConvergencePlan` record
  (`Outcome = Create | Reconcile | AlreadySatisfied | Collision`, `ColumnsToAdd`, `ColumnsToModify`,
  `CollisionPackageName`, `Error`). Data-only carrier → `record`, may use `new`.
- Existence + owning package via `FindEntitySchemaCommand.FindSchemas` → `EntitySchemaSearchResult`
  (`SchemaName`, `PackageName`, `ParentSchemaName`). This global read is the ONLY surface that sees a
  cross-package collision (AC-04) — `get-app-info` is app-scoped and blind to it.
- Column detail via `GetEntitySchemaPropertiesCommand.GetSchemaProperties`
  (`IRemoteEntitySchemaColumnManager`) → `EntitySchemaPropertiesInfo` for the add-missing-columns delta.
- Collision gate = package match on `EntitySchemaSearchResult.PackageName` **plus a parent/kind check
  on the same-package branch** using `EntitySchemaSearchResult.ParentSchemaName` (OQ-05). Different
  package ⇒ collision. A same-package schema whose parent/kind is incompatible with the requested
  lookup (e.g. a `BaseEntity`-derived entity vs. a `BaseLookup`) is ALSO a collision — never silently
  reconciled with lookup columns + a forced `EnsureLookupRegistration`. Name/package comparison uses
  `OrdinalIgnoreCase` (matches the existing `TryGetCollisionInfo` behavior, `SchemaSyncTool.cs:294`).
  Column-shape incompatibility is a per-column concern (handled in Story 2), not a schema-level
  collision here. **Known limitation:** `FindSchemas` filters `ManagerName == "EntitySchemaManager"`,
  so a same-named schema under a different manager (e.g. a source-code schema) is invisible to this gate.
- Reconcile column-add write path: adding the missing columns to an *existing* schema reuses
  `UpdateEntitySchemaCommand`'s add-column operation (`CreateEntitySchemaColumnArgs`) — the same
  additive column-add mechanism `update-entity` uses. `CreateEntitySchemaCommand` is create-only and
  must NOT recreate (AC-02). **Story 1 introduces (and owns) this shared additive column-add helper;
  Story 2 extends it** with the modify/remove per-column paths — the helper lands here so Story 2's
  reconcile has a mechanism to build on (resolves the "reconcile needs Story 2's mechanism" ordering).
- **Delete `TryGetCollisionInfo`** in this story: the pre-emptive classification removes its last
  caller, so leaving the method would trip CLIO dead-code / analyzer warnings. Story 6 does NOT
  re-delete it (Story 6 owns only the #910 resume special-case reconciliation).
- In `SchemaSyncTool.ExecuteCreateSchema`: move `EnsureLookupRegistration`
  (`ILookupRegistrationService`, already idempotent by name) OUT of the `exitCode == 0`
  (freshly-created) branch so it runs on the already-exists path too (FR-02).
- Add `[JsonPropertyName("outcome")] string? Outcome` to `SchemaSyncOperationResult` with
  `JsonIgnoreCondition.WhenWritingNull` — additive, preserves the current wire shape.
- Constructor-inject `ISchemaConvergenceService` into `SchemaSyncTool`; register in `BindingsModule.cs`.
- FR-10: `create-entity` reuses the shared convergent create path where it shares logic — only to the
  extent that falls out naturally; no dedicated `ensure-entity` scope.

Key files: `clio/Command/McpServer/Tools/SchemaSyncTool.cs` (`ExecuteCreateSchema`,
`SchemaSyncOperationResult`), `clio/Command/McpServer/Tools/SchemaConvergenceService.cs` (new),
`clio/BindingsModule.cs`.
Pattern to follow: existing DI service + interface registration in `BindingsModule.cs`; existing
`SchemaSyncTool` per-op result construction.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | classifier outcomes: absent → Create; in-target-package-subset → Reconcile(ColumnsToAdd); in-target-package-identical → AlreadySatisfied; cross-package → Collision; column-type mismatch surfaced to Story 2's modify path (not a schema collision) | `clio.tests/Command/McpServer/SchemaConvergenceServiceTests.cs` (new) |
| Unit `[Category("Unit")]` | `ExecuteCreateSchema` wiring: created/reconciled/already-satisfied/collision outcomes; `EnsureLookupRegistration` runs on already-exists path; collision → `success:false` + `Error:` + stop-on-first-failure | `clio.tests/Command/McpServer/SchemaSyncToolTests.cs` |
| Unit (recompile sweep) | The new `ISchemaConvergenceService` ctor dependency breaks compilation of ALL existing `SchemaSyncToolTests` cases (~40). Story 1 explicitly OWNS updating/recompiling that fixture (target-typed `new()` or DI-resolved ctor + a default fake convergence service), not just adding new tests. | `clio.tests/Command/McpServer/SchemaSyncToolTests.cs` |
| E2E `[Category("E2E")]` | Deferred to Story 5 (consolidated `SchemaSyncToolE2ETests`, real `mcp-server`, NOT in CI) | `clio.mcp.e2e/` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`. Fixtures carry `[Property("Module","McpServer")]`
(match existing `SchemaSyncToolTests`); NSubstitute; AAA with `because`; `[Description(...)]` per method.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001–CLIO005); no new `new` for behavior classes; no dead DI registration.
- [ ] `TryGetCollisionInfo` deleted (its last caller is removed here) — no dead-code / analyzer warning left behind; Story 6 does not re-delete it.
- [ ] Existing `SchemaSyncToolTests` fixture (~40 cases) recompiled against the new ctor (default fake convergence service) — this story owns the recompile sweep, not only the new tests.
- [ ] `ISchemaConvergenceService` registered in `BindingsModule.cs` and consumed via constructor injection (has a real consumer in this PR).
- [ ] `outcome` is additive on the wire (`[JsonPropertyName("outcome")]` + `JsonIgnoreCondition.WhenWritingNull`) — existing result shape unchanged when null.
- [ ] MCP reviewed: `SchemaSyncOperationResult` is an MCP result-contract change — result-shape review recorded ("MCP reviewed"); guidance/docs/contract-text belong to Story 4.
- [ ] Error messages are user-friendly `Error: {message}`; stop-on-first-failure preserved.
- [ ] Unit tests added with `[Category("Unit")]` (never `UnitTests`).
- [ ] PR description references this story file.

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
