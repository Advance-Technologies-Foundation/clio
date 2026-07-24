# ADR: sync-schemas — convergent "ensure" semantics for schema operations

**Status**: Proposed
**Author**: Architect Agent
**PRD**: [prd-sync-schemas-ensure-semantics.md](../prd/prd-sync-schemas-ensure-semantics.md)
**Jira**: ENG-93807 (sub-task of ENG-93367; follow-up to PR #910 / ENG-93374)
**Created**: 2026-07-20
**stepsCompleted**: [1, 2, 3, 4]

---

## Context

`sync-schemas` (`clio/Command/McpServer/Tools/SchemaSyncTool.cs`, MCP-only, no CLI verb) batches
imperative, non-idempotent schema operations (`create-lookup`, `create-entity`, `update-entity`,
inline `seed-data`). After an ambiguous post-send failure — the request reached the server but the
response was lost — the client cannot know the server state. Today the create path calls
`CreateEntitySchemaCommand.Execute` unconditionally and only *reacts* to a non-zero exit code by
probing with `TryGetCollisionInfo` (a post-hoc `FindEntitySchemaCommand` read that guesses at the
failure cause). PR #910 layers per-operation retry and a resume-plan on top, but every branch is a
heuristic that infers server state from indirect signals (exit code, error text, existence probe),
and each guess leaves a residual correctness hole: a pre-existing cross-package collision is masked
as success, a replay of an already-applied change is reported as a failure, and seed rows without a
`Name` (or on a schema without a `Name` column) PK-conflict on replay.

This ADR chooses the systemic redesign the PRD asks for: move `create-lookup` and `update-entity`
to **read-current-state → apply-only-the-delta** ("ensure") semantics so that retry and resume are
safe *by construction* — re-running the identical batch IS the verification — rather than by
heuristic. The design must reuse existing read surfaces (no new backend endpoint, A-01) and must
not add MCP round-trips (FR-06, A-05).

## Decision

Rewrite the `create-lookup` / `create-entity` and `update-entity` execution paths in
`SchemaSyncTool` to **read current server state first, classify, then apply only the missing
delta**, all server-side inside the existing single batch call and under the already-held
per-tenant `McpToolExecutionLock`. Concretely:

- **ensure-create** (`create-lookup`, and `create-entity` where it shares the path): one global
  existence/package read (`FindEntitySchemaCommand`) classifies the target as *absent* /
  *present-in-target-package* / *present-in-other-package*. Absent → create + ensure Lookups
  registration. Present-in-target-package → skip create, read column detail
  (`GetEntitySchemaPropertiesCommand`), add only missing columns **via `UpdateEntitySchemaCommand`'s
  add-column operation** (the same additive column-add mechanism `update-entity` uses;
  `CreateEntitySchemaCommand` is create-only and must NOT recreate — AC-02), and **always** ensure the
  Lookups registration (FR-02). Present-in-other-package, or present-in-target-package with an
  incompatible parent/kind (a `BaseEntity`-derived entity vs. a `BaseLookup`, via
  `EntitySchemaSearchResult.ParentSchemaName`) → explicit machine-readable durable-collision failure
  (FR-03, AC-04, OQ-05). The pre-emptive read *replaces* the reactive `TryGetCollisionInfo` probe.
- **ensure-update-entity**: read column detail once, then per-column reconcile —
  add-if-absent / modify-if-different / remove→ensure-absent — emitting only the delta. Columns not
  named in the request are left untouched (additive per-column ensure; no full reconcile, A-04).
- **seed-data**: unchanged in committed scope. The seed path (`DataBindingDbCommand` →
  `CreateBinding`→`ProcessRows`) dedups by `Name`, so seed-data is *already* convergent only for rows
  that carry a `Name` when the target schema has a `Name` column (already-present-by-`Name` rows land
  in `SkippedRows`); rows without a `Name` (or schemas without a `Name` column) are NOT convergent — a
  stable-`Id`, no-`Name` row causes a primary-key conflict on replay. Document this
  deliberately-limited contract (OQ-01 resolution below).

Each per-op result gains an additive `outcome` discriminator (`created` | `reconciled` |
`already-satisfied` | `collision`). **Operation type names are NOT renamed** (OQ-03 → keep
`create-lookup` / `update-entity`; behavior is a pure superset, so no `McpToolCompatibilityCatalog`
or `WorkspaceTemplateGuidanceDriftTests` churn).

## Alternatives Considered

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| A: Keep #910 heuristics, add more special-case branches (verify-against-intent, richer collision text) | No new read surfaces; smallest diff | Every branch is still a guess; residual holes remain; heuristic surface *grows* (violates SM-01c / SM-02c) | Rejected: does not eliminate the residual holes the PRD targets |
| **B: Read-current-state → apply-delta (ensure), reusing `FindEntitySchemaCommand` + `GetEntitySchemaPropertiesCommand` (chosen)** | Correct by construction; retry/resume safe; heuristic surface *shrinks*; reuses existing reads; safe by convergence-on-retry (a stale delta re-converges on the next idempotent run), not reliant on lock-based mutual exclusion; no new endpoint, no new MCP round-trip | Adds one server-side read per op on the reconcile branch; requires touching the create path shared with `create-entity` | **Chosen** |
| C: New ClioGate `ensure-schema` backend endpoint that does read+delta atomically server-side | One round-trip; atomic on the server | Violates A-01 / FR-06 (new endpoint); large ClioGate + build/deploy scope; longer release path | Rejected: PRD non-goal ("will NOT add a new backend endpoint") |
| D: Rename ops to `ensure-lookup` / `ensure-entity` with compatibility aliases (OQ-03 alt) | Names advertise the new contract | Forces `McpToolCompatibilityCatalog` entries + drift-guard churn + re-education of every shipped template/guidance; behavior is a superset so the old names stay truthful | Rejected: churn without behavioral benefit (see OQ-03) |
| E: Make seed-data upsert-by-`uniqueness-key` now | Full seed convergence | PRD non-goal for committed scope; needs a reliable per-row key + seed-contract change; the existing `Name`-skip already covers the named-row case | Deferred: recommended as a follow-up story (OQ-01) |

## Grounding: existing read surfaces satisfy A-01 (no new endpoint)

| State needed | Existing surface | Returns | Scope |
|--------------|------------------|---------|-------|
| Existence + owning package + parent | `FindEntitySchemaCommand.FindSchemas` (single SysSchema DataService query) → `EntitySchemaSearchResult` (`SchemaName`, `PackageName`, `PackageMaintainer`, `ParentSchemaName`) | schema identity + package | **Global** (all packages) — the only surface that can see a cross-package collision (AC-04) |
| Column-level detail | `GetEntitySchemaPropertiesCommand.GetSchemaProperties` (via `IRemoteEntitySchemaColumnManager`) → `EntitySchemaPropertiesInfo` with `EntitySchemaPropertyColumnInfo` (`Name`, `Type`, `Required`, `ReferenceSchemaName`, `Source` own/inherited, …) | per-column type/required/reference/source | target schema (package-scoped when `package-name` supplied) |
| Lookups registration | `ILookupRegistrationService.EnsureLookupRegistration` (already idempotent by name) | registers if absent | target package |
| Seed idempotency | `DataBindingDbCommand` (`CreateBinding`→`ProcessRows`) dedups by `Name` — returns `CreatedRows` + `SkippedRows` (skip-existing-by-`Name`, only when the schema has a `Name` column and the row carries a `Name`); `IDataBindingDbService.UpsertRow` exists for single-row upsert-by-`Id` but the seed path does NOT call it | create-if-absent-by-`Name` (rows without a `Name` PK-conflict on replay) | target binding |

`get-app-info` bundles entities+columns+package for one application in a single payload but is
**app-scoped** — it is blind to a same-named schema in a *different* package, so it cannot serve the
cross-package collision check (AC-04). Therefore `FindEntitySchemaCommand` (global) remains the
existence/collision probe and `GetEntitySchemaPropertiesCommand` the column probe. All reads go
through `IApplicationClient` server-side within the single batch call — no added MCP round-trip.

## Open-question resolutions

- **OQ-01 (seed-data) — RESOLVED (committed) / RECOMMENDED (future).** Committed scope: keep
  `seed-data` on `DataBindingDbCommand` unchanged. That path (`CreateBinding`→`ProcessRows`) dedups by
  `Name`, so seed-data is convergent (replay-safe) only for rows that carry a `Name` when the target
  schema has a `Name` column (already-present-by-`Name` rows land in `SkippedRows`); rows without a
  `Name` (or schemas without a `Name` column) are NOT convergent — a stable-`Id`, no-`Name` row causes
  a primary-key conflict on replay. Document the contract explicitly (AC-08): *"a row is replay-safe
  only when the target schema has a `Name` column AND the row carries a `Name`; rows without a `Name`
  (or schemas without a `Name` column) are non-convergent — a stable-`Id`, no-`Name` row PK-conflicts
  on replay."* Recommendation for the follow-up: a later additive story may route value-updating rows
  through `IDataBindingDbService.UpsertRow` (which keys on `Id`, not just skip) or add an optional
  kebab `uniqueness-key` arg — deferred, not in this ADR's scope. This is the "explicitly deferred"
  path SM-01 permits and honors the PRD non-goal.
- **OQ-03 (naming/compat) — RESOLVED: no rename.** Keep `create-lookup` / `update-entity`; the new
  behavior is a pure superset (still creates-if-absent, additionally reconciles), so the names stay
  truthful. Benefit: zero `McpToolCompatibilityCatalog` and zero `WorkspaceTemplateGuidanceDriftTests`
  churn, and no re-education of shipped templates/guidance. Surface the new semantics through the
  additive `outcome` discriminator instead of a rename. (Matches the PRD's own "No (superset)"
  framing.)
- **OQ-04 (latency/read budget) — RESOLVED, with a required PRD-wording reconciliation (see OI-01).**
  Budget = **zero added MCP round-trips** and **no post-write verify read-back**; every state-read is
  a server-side DataService call inside the single existing batch call, under the held per-tenant lock.
  Read count per op depends on the path, and **no single existing surface returns both the authoritative
  owning-package (needed for the cross-package collision gate) AND the columns** — verified against the
  source: `GetEntitySchemaProperties` in merged mode returns a placeholder `MergedSchemaPackageName`
  (not the real owning package) and in single-package mode throws when the schema is absent or lives in
  another package, so it cannot serve the collision classification; that requires the global
  `FindEntitySchemaCommand`. Therefore:
  - **create-only / greenfield path (schema absent):** 1 read/op (`FindEntitySchemaCommand`), then create.
  - **reconcile path (schema already exists in the target package):** 2 reads/op — `FindEntitySchemaCommand`
    (existence + package + collision classification) followed by `GetEntitySchemaPropertiesCommand`
    (column delta).
  - **`update-entity`:** 1 read/op (column detail).

  This means AC-09 / SM-03 as literally worded ("at most **one** extra state-read per operation") cannot
  hold on the reconcile-existing path — and per SM-03c's own gloss a "clean (no-collision) batch" *includes*
  the reconcile-existing case (AC-02 is a clean, no-collision batch that legitimately does 2 reads/op). Do
  not paper over this: the honest budget that preserves G3's actual rationale (sync-schemas beats sequential
  calls by saving *round-trips and lock acquisitions*, not server-side reads) is **"zero added MCP round-trips;
  no verify read-back; server-side DataService reads within the single batch call are not counted against
  the round-trip budget (up to 2 server-side reads on the reconcile path)."** **OI-01 — RESOLVED (2026-07-20):**
  the PRD's AC-09 / SM-03 wording was reconciled to this round-trip formulation (see PRD Open Questions
  OI-01), so the ADR no longer contradicts its own acceptance criterion. SM-03c's p50 wall-time
  counter-metric stands unchanged and is the real regression guard.

- **OQ-05 (collision identity for FR-03) — RESOLVED: package-match gate + parent/kind check.** "The
  caller's intended schema" is identified by **package match** using
  `EntitySchemaSearchResult.PackageName`. A same-named schema in a *different* package is a durable
  collision → explicit failure (AC-04). A same-named schema in the *target* package is the caller's
  schema → reconcile columns additively — **but only if its parent/kind is compatible**: on the
  same-package branch the classifier also checks parent/kind compatibility using
  `EntitySchemaSearchResult.ParentSchemaName`, so a same-named schema in the target package whose
  parent/kind is incompatible with the requested lookup (e.g. a `BaseEntity`-derived entity vs. a
  `BaseLookup`) is surfaced as a collision (or explicit modify-conflict), NOT silently reconciled with
  lookup columns + a forced `EnsureLookupRegistration`. The name/package comparison uses
  `OrdinalIgnoreCase` to match the existing `TryGetCollisionInfo` behavior (`SchemaSyncTool.cs:294`).
  Column-shape is **not** used as a schema-level collision gate; instead, an incompatible existing
  column (e.g. a requested type that differs from the persisted type on a same-named column in the
  target package) is surfaced as a **per-column modify-conflict** inside the reconcile step, not as
  a whole-schema collision. This keeps the gate grounded in what the global read actually exposes
  (package + parent) and avoids over-classifying reconcilable differences as collisions.
  **Known limitation (manager scope):** `FindEntitySchemaCommand.FindSchemas` filters `ManagerName ==
  "EntitySchemaManager"`, so a same-named schema under a *different* manager (e.g. a source-code
  schema) is invisible to this gate.
  **Known limitation (reference-schema on create-path reconcile):** `ComputeColumnDelta` compares
  existing vs. requested columns by resolved `DataValueType` **ordinal** only (via
  `AreColumnTypesEquivalent`), not by `ReferenceSchemaName`. So on the `create-lookup`/`create-entity`
  reconcile path, an existing lookup column whose reference target differs from the request (e.g.
  existing `UsrCustomer → Contact`, requested `→ Account`) is treated as `AlreadySatisfied` and is
  NOT reconciled — and the `columns` create-shape cannot express a `modify`, so a replay cannot
  correct it. To change an existing lookup column's reference target, use `update-entity` with an
  explicit `modify` op (which is forwarded to the backend). Rationale: silently rewriting a live
  lookup's reference target is semantically heavy / potentially destructive, so it is deliberately
  not auto-reconciled. (OQ-02 — delete-unlisted full reconcile — remains permanently
  out of scope as too destructive for an "ensure" contract; noted, not implemented.)

## Correctness argument (why this is safe by construction)

- **Read-then-delta-write is safe by convergence-on-retry, not by mutual exclusion.** The read and
  the write happen under the same held `McpToolExecutionLock.GetLock(tenantKey)` for the batch, but
  that lock is an **in-process, in-memory** guard — it does NOT exclude a second clio process, a
  concurrent CLI invocation, a Freedom UI designer edit between read and write, or the concurrent
  per-tenant instances the mcp-http credential-passthrough work runs. The design is therefore **not**
  claimed TOCTOU-safe under the lock; instead it is correct because a stale delta simply **fails and
  re-converges on the next idempotent run** — re-running the identical batch IS the verification
  (FR-05).
- **Idempotent replay (AC-03, the thesis):** on replay, the read observes the already-applied
  mutation, the computed delta is empty, and the op reports `already-satisfied` → `success: true`
  with zero new mutations (SM-02). The masked-collision hole closes because the *pre-emptive* global
  read classifies a cross-package schema as a collision *before* any create attempt, instead of
  guessing after a failure.
- **Heuristic surface shrinks (SM-01c / SM-02c):** the reactive `TryGetCollisionInfo` probe and the
  #910 resume special-cases for the convergent ops become unnecessary — convergence subsumes them.

## Implementation Plan

`sync-schemas` is MCP-only (no CLI verb, no CLI flags). All new field names are kebab-case
(CLIO001 governs MCP argument/field naming): `outcome`, `reference-schema-name`, etc. Behavior
classes are DI-resolved via the existing `IToolCommandResolver` (no `new` for behavior — CLIO001).

### Files to create

| File | Purpose |
|------|---------|
| `clio/Command/McpServer/Tools/SchemaConvergenceService.cs` | New DI service (`ISchemaConvergenceService` + impl) that, given a target, performs the read+classify+delta computation (existence/package via `FindEntitySchemaCommand`, columns via `GetEntitySchemaPropertiesCommand`) and returns a `SchemaConvergencePlan` (create? add-columns? modify-columns? collision?). Behavior class → interface + DI registration (CLIO001/CLIO005). |
| `clio.tests/Command/McpServer/SchemaConvergenceServiceTests.cs` | Unit tests (NSubstitute) for the classifier: absent / in-target-package-subset / in-target-package-identical / cross-package-collision / column-type-conflict. |

### Files to modify

| File | Change description |
|------|-------------------|
| `clio/Command/McpServer/Tools/SchemaSyncTool.cs` | Route `ExecuteCreateSchema` and `ExecuteUpdateEntity` through `ISchemaConvergenceService`: read-classify-delta; add missing columns to an existing schema via `UpdateEntitySchemaCommand`'s add-column operation (NOT `CreateEntitySchemaCommand`, which is create-only — AC-02); move `EnsureLookupRegistration` out of the `exitCode==0` (freshly-created) branch so it also runs on the already-exists path (FR-02); replace reactive `TryGetCollisionInfo` with the pre-emptive collision classification and **delete `TryGetCollisionInfo` (its last caller is removed here — Story 1/U1 owns the deletion, not U6)** (FR-03/AC-04); add `Outcome` to `SchemaSyncOperationResult`. Constructor-inject the new service. |
| `clio/BindingsModule.cs` | Register `ISchemaConvergenceService` → `SchemaConvergenceService`. |
| `clio/Command/McpServer/Tools/ToolContractGetTool.cs` | Update `BuildSchemaSync()` (~line 3402): describe convergent superset semantics, the additive `outcome` discriminator, the collision failure shape, and the resolved seed-data (`Name`-keyed) contract (FR-07). |
| `clio/Command/McpServer/Tools/SchemaSyncTool.cs` `[Description]` + `SchemaSyncOperationResult` | Add `outcome` field (`created`/`reconciled`/`already-satisfied`/`collision`) with `[JsonPropertyName("outcome")]`; update the tool `[Description]` to state re-run safety (no hand-composed catch-up batch). |
| Guidance resources — `AppModelingGuidanceResource.cs`, `ExistingAppMaintenanceGuidanceResource.cs`, `DataBindingsGuidanceResource.cs`, `AgentExecutionGuidanceResource.cs` (the sync-schemas-touching guides) | Rewrite catch-up guidance: re-submitting the identical batch is the safe recovery path; remove any instruction to hand-compose a catch-up batch for the convergent ops (SM-04c); state the `Name`-keyed seed-data contract. |
| `docs/commands/sync-schemas.md` | Document convergent semantics, `outcome` values, collision failure, and the seed-data `Name` contract (FR-07). |
| `docs/McpCapabilityMap.md` | Update the `sync-schemas` long-running entry to reflect re-run-safe convergence. |
| `clio.tests/Command/McpServer/SchemaSyncToolTests.cs` | Add unit cases for all new outcomes and the ambiguous-failure re-run class (see Test strategy). |
| `clio.mcp.e2e/SchemaSyncToolE2ETests.cs` | Add E2E coverage exercising real `clio mcp-server` for absent-create, existing-reconcile, replay-idempotency, and cross-package collision. |
| `clio.mcp.e2e/ToolContractGetToolE2ETests.cs` | Assert the updated `BuildSchemaSync` contract text. |

`Commands.md`, `clio/help/en/*.txt`, `WikiAnchors.txt` — **no change** (MCP-only tool, no CLI verb;
state "docs reviewed, no CLI-help change required" in the PR).

### Key interfaces / contracts (illustrative — no MediatR; DI + services per project-context)

```csharp
// New DI service — reads current state and computes the delta (behavior class → interface).
public interface ISchemaConvergenceService {
    SchemaConvergencePlan Classify(SchemaConvergenceTarget target);
}

// Data-only carrier (record, may use `new`).
public sealed record SchemaConvergencePlan(
    SchemaConvergenceOutcome Outcome,      // Create | Reconcile | AlreadySatisfied | Collision
    IReadOnlyList<CreateEntitySchemaColumnArgs> ColumnsToAdd,
    IReadOnlyList<UpdateEntitySchemaOperationArgs> ColumnsToModify,
    string? CollisionPackageName,          // set only when Outcome == Collision
    string? Error);                        // user-friendly "Error: {message}" when Collision
```

`SchemaSyncOperationResult` gains an additive `[JsonPropertyName("outcome")] string? Outcome`
(omitted when null via `JsonIgnoreCondition.WhenWritingNull`, preserving the current wire shape).

### MCP tool-contract impact (no CLI flags — MCP-only tool)

| Change | Details | Breaking? |
|--------|---------|-----------|
| Operation semantics | `create-lookup` / `update-entity` become convergent supersets (create-if-absent + reconcile-delta) | No (superset — OQ-03) |
| Operation type names | Unchanged (`create-lookup`, `update-entity`, `create-entity`, `seed-data`) | No — no rename (OQ-03) |
| Result field | Additive `outcome` discriminator on each per-op result | No (additive) |
| Collision output | `collision-info` retained; collision now surfaced pre-emptively as `success: false` + `outcome: collision` | No (additive/behavioral superset) |
| seed-data | Contract documents `Name`-keyed dedup (rows without a `Name` PK-conflict on replay); no arg change in committed scope | No |

### FR / AC → work-unit traceability

| FR / AC | Covered by unit |
|---------|-----------------|
| FR-01, FR-03, AC-02, AC-04, OQ-05 | U1 (ensure-create) |
| FR-02, AC-01 | U1 (unconditional `EnsureLookupRegistration`) |
| FR-04, FR-05, AC-05, AC-06, AC-07 | U2 (ensure-update-entity) |
| FR-05, AC-03 (thesis) | U1 + U2 (idempotent replay) |
| FR-06, AC-09, OQ-04 | U1 + U2 (server-side reads, no round-trip; reconcile path = 2 reads, see OQ-04/OI-01) + U5 counter-metric test |
| FR-08, AC-08, OQ-01 | U3 (seed-data contract/docs) |
| FR-07, SM-04, SM-04c | U4 (contract/guidance/docs) |
| FR-09 | U6 (rebase onto #910; preserve result shape, shrink heuristics) |
| FR-10 | U1 (create-entity reuses the shared convergent create path where trivial) |

### Implementation units (for the story-writer)

1. **U1 — ensure-create (`create-lookup`, shared `create-entity`).** `ISchemaConvergenceService`
   + wire into `ExecuteCreateSchema`: read-classify-delta, create-if-absent, add-missing-columns
   **to an existing schema via `UpdateEntitySchemaCommand`'s add-column operation** (the shared
   additive column-add helper U2 also builds on; `CreateEntitySchemaCommand` is create-only and must
   not recreate — AC-02), **unconditional** `EnsureLookupRegistration`, pre-emptive
   package-match-plus-parent/kind collision failure, `outcome` on the result. U1 also **deletes
   `TryGetCollisionInfo`** (it removes the method's last caller, so leaving it would trip CLIO
   dead-code/analyzer warnings — U6 must NOT re-delete it). (FR-01/02/03/10, AC-01/02/04, OQ-05)
2. **U2 — ensure-update-entity per-column reconcile.** Wire column read into `ExecuteUpdateEntity`:
   add-if-absent / modify-if-different / remove→ensure-absent; emit only the delta; leave unlisted
   columns untouched. (FR-04/05, AC-05/06/07)
3. **U3 — seed-data contract decision (OQ-01).** No code change to the seed path; document the
   `Name`-keyed dedup contract in tool contract + docs + guidance; add a regression test that a
   re-run with `Name`-bearing rows (schema has a `Name` column) produces no duplicates (SkippedRows)
   and that rows without a `Name` are documented as non-convergent (PK-conflict on replay). (FR-08, AC-08)
4. **U4 — contract, guidance, docs (FR-07).** `BuildSchemaSync`, tool `[Description]`, the four
   guidance resources, `docs/commands/sync-schemas.md`, `docs/McpCapabilityMap.md`; ensure no
   guidance instructs a hand-composed catch-up batch (SM-04c). Also check `clio/tpl/**`
   (shipped workspace `AGENTS.md`/`CLAUDE.md`, guarded by `WorkspaceTemplateGuidanceDriftTests`)
   carries no catch-up-batch language for the convergent ops -- record "checked, no catch-up
   guidance in tpl/**" in the PR.
5. **U5 — test coverage.** Unit (ambiguous-failure re-run class, all outcomes, collision, no-op
   columns, remove-already-absent) + E2E (`SchemaSyncToolE2ETests`, real `mcp-server`) + the
   read-budget counter-metric assertion (no added MCP round-trip; no verify read-back; create-only path = 1 server-side read/op, reconcile path is 2 server-side reads/op -- per the OQ-04/OI-01 round-trip budget, NOT the literal one-state-read wording). (SM-01,
   SM-02, SM-03, AC-03, AC-09)
6. **U6 — #910 integration (FR-09).** Rebase onto the merged #910 branch; preserve the
   resume-plan / partial-result output shape; delete the now-redundant #910 resume special-cases for
   the convergent ops so the heuristic surface shrinks, not grows (SM-01c/SM-02c). `TryGetCollisionInfo`
   itself is already deleted in U1 (last caller removed) — U6 does NOT re-delete it. #910 independently
   rewrites `ExecuteCreateSchema`/`ExecuteUpdateEntity` (per-op retry + resume-plan), so U6 must
   reconcile two overlapping rewrites of those methods and re-verify the preserved result shape — a
   substantial rebase (sized L), not a half-day merge. Sequencing depends on A-06 (this branch has the
   collision-probe but not #910's resume-plan yet).

### Test strategy

| Layer | Framework | What to cover | File |
|-------|-----------|---------------|------|
| Unit | NUnit + NSubstitute + FluentAssertions | classifier outcomes; ensure-create create/reconcile/already-satisfied/collision; per-column add/modify/no-op/remove-absent; **ambiguous-failure re-run class stays green** (SM-01c); zero added MCP round-trips + no verify read-back on clean path — server-side read count 1 (create-only) / 2 (reconcile), NOT the literal ≤1 wording (SM-03, OQ-04/OI-01) | `clio.tests/Command/McpServer/SchemaSyncToolTests.cs`, `SchemaConvergenceServiceTests.cs` |
| Integration | Real FS / stub | n/a — sync-schemas has no local FS/DB path; state-reads mocked at unit tier | — |
| E2E | `clio.mcp.e2e` (real `clio mcp-server`, MCP protocol) | absent-create → success; existing-in-package reconcile → only-missing-added; replay idempotency (AC-03); cross-package collision → `success:false` + `outcome:collision` | `clio.mcp.e2e/SchemaSyncToolE2ETests.cs`, `ToolContractGetToolE2ETests.cs` |

MCP E2E is **not in CI yet** — flag in the test plan; run manually. Test-style rules (AGENTS.md):
AAA structure, every assertion has a `because`, every method has `[Description(...)]`, every method
`[Category("Unit")]` (never `UnitTests`), fixtures carry `[Property("Module","McpServer")]` (matches
the existing `SchemaSyncToolTests` fixture). Name format
`MethodName_ShouldExpectedBehavior_WhenCondition`.

## Consequences

- **Positive**: retry/resume of `sync-schemas` batches becomes safe by construction; the three
  residual holes (masked collision, replay-as-failure, no-`Name`-seed PK-conflict on replay) are eliminated or
  explicitly deferred; the heuristic branch count shrinks; no new backend endpoint; no new MCP
  round-trip; no operation-name churn (zero compat-catalog / drift-guard cost).
- **Trade-offs**: the reconcile branch adds one server-side column-detail read (bounded, off the
  clean-batch path AC-09 measures); the create path shared with `create-entity` is touched, so
  `create-entity` behavior must be regression-tested even though it is a "Could" (FR-10).
- **Breaking change**: **No.** Behavior is a superset; result additions are optional fields
  (`WhenWritingNull`). No `RELEASE.md` migration entry required beyond a feature note. **Caveat
  (A-03):** any caller relying on "create fails when the schema already exists" as a *signal* will
  now see `success:true / outcome:reconciled|already-satisfied`; call this out in release notes.

## ClioRing compatibility

**ClioRing compatibility reviewed, no Ring-consumed contract changed.** Inspected `clio-ring/`
(`ClioRing.Ipc`, `ClioRing`, `ClioRing.Desktop/actions.json`) — no reference to `sync-schemas` or
`SchemaSync`. Even if a future Ring action dispatched `sync-schemas` through `clio-run`, every
change here is additive (new optional `outcome` field, superset behavior, no renamed tool/args), so
Ring's unknown-field-tolerant DTO rule absorbs it and no versioned protocol transition is required.
If any story ends up altering a Ring-consumed nested command or the result envelope, run the Ring
compatibility commands from AGENTS.md before merge.

## Pre-implementation Checklist

- [ ] `sync-schemas` remains MCP-only — no CLI flag added; new field names are kebab-case (CLIO001).
- [ ] `ISchemaConvergenceService` registered in `BindingsModule.cs` and consumed via constructor
      injection (no `new` for behavior — CLIO001; no dead registration — CLIO005).
- [ ] `EnsureLookupRegistration` moved to run on the already-exists path (FR-02).
- [ ] Pre-emptive package-match collision replaces reactive `TryGetCollisionInfo` (FR-03/AC-04/OQ-05).
- [ ] Result additions are optional (`JsonIgnoreCondition.WhenWritingNull`) — wire shape preserved.
- [ ] Error messages are user-friendly `Error: {message}` strings; stop-on-first-failure preserved.
- [ ] MCP targets updated: `BuildSchemaSync`, tool `[Description]`, four guidance resources,
      `docs/commands/sync-schemas.md`, `docs/McpCapabilityMap.md`; `clio.mcp.e2e` coverage added.
- [ ] `WorkspaceTemplateGuidanceDriftTests` -- no change expected (no rename); confirm still green,
      and confirm `clio/tpl/**` templates carry no catch-up-batch guidance for the convergent ops.
- [x] OI-01 resolved: AC-09/SM-03 wording reconciled to the zero-round-trip budget in the PRD (see OQ-04).
- [ ] Rebased onto #910; resume-plan/result shape preserved; heuristic branches for convergent ops
      removed (FR-09).
- [ ] ClioRing: additive-only confirmed; no Ring-consumed contract changed.
