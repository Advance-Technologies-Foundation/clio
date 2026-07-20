# PRD: sync-schemas — convergent "ensure" semantics for schema operations

**Status**: Draft
**Author**: PM Agent
**Created**: 2026-07-20
**Jira**: ENG-93807 (sub-task of ENG-93367 "Analyze long app creating")

---

## Problem Statement

The `sync-schemas` MCP tool (`clio/Command/McpServer/Tools/SchemaSyncTool.cs`) drives a batch of
imperative, non-idempotent schema operations (`create-lookup`, `create-entity`, `update-entity`,
`seed-data`). After an ambiguous post-send failure — the request reached the server but the response
was lost — the client cannot know the server state. PR #910 (ENG-93374) added per-operation transient
retry, a resume-plan, and a collision-probe so a long batch can be resumed, but all three are
**heuristics that guess server state from indirect signals** (exit code, error text, schema existence),
and every guess leaves a residual correctness hole. This affects the AI agents and CI flows that
bootstrap Creatio applications through the tool, where a single transient fault forces a hand-composed
catch-up batch or silently masks a durable collision.

## Goals

- [ ] **G1 — Convergent create/update.** Move `create-lookup` and `update-entity` to read-current-state →
      apply-only-the-delta semantics (verification collapses into the next run's read — re-running IS the verification), so retry and resume are safe by construction rather than by
      heuristic.
      Success metric **SM-01**: 100% of the three documented residual-hole scenarios (masked pre-existing
      collision, replay-turns-applied-change-into-failure, no-`Name`-seed PK-conflict on replay) are provably eliminated
      or explicitly deferred (seed-data) by test cases in the test plan.
      Counter **SM-01c**: no masked-failure or duplicate-mutation regression introduced by convergence
      (measured by the ambiguous-failure re-run test class staying green).

- [ ] **G2 — Repeatable-by-default resume.** Any convergent operation is provably safe to repeat: a full
      re-submit of the original batch converges to the same end state with no error on already-applied ops.
      **SM-02**: re-running an identical batch that has already fully succeeded returns `success: true`
      with zero new mutations. Counter **SM-02c**: resume/retry logic added by #910 does not need to grow
      new special-case branches for the convergent ops (heuristic surface shrinks, not grows).

- [ ] **G3 — Bounded latency cost.** Read-before-write must not erode the round-trip and lock savings that
      justify `sync-schemas` over sequential calls.
      **SM-03** (counter-metric-led): **zero added MCP round-trips** and **no post-write verify
      read-back**. Server-side DataService reads within the single existing batch call are NOT counted against
      the round-trip budget — the reconcile-existing path legitimately does up to 2 server-side reads per op
      (`FindEntitySchemaCommand` for existence/package/collision + `GetEntitySchemaPropertiesCommand` for the
      column delta), all inside the single batch call under one per-tenant lock hold. Counter **SM-03c**: p50
      batch wall-time for a clean (no-collision) batch does not regress beyond an agreed budget versus the
      pre-change tool. (Resolved per ADR OQ-04/OI-01: the earlier "added state-reads per operation ≤ 1"
      framing was self-contradictory for the 2-read reconcile path and is superseded.)

- [ ] **G4 — Contract truth.** The tool contract, MCP guidance, docs, and E2E coverage reflect the
      convergent semantics (including the explicit seed-data decision).
      **SM-04**: `ToolContractGetTool.BuildSchemaSync`, `docs/commands/sync-schemas.md`, the four guidance
      resources, and `clio.mcp.e2e` are updated and pass the drift/guard tests. Counter **SM-04c**: no
      guidance still instructs agents to hand-compose a catch-up batch for the convergent ops.

## Non-goals

- Will NOT add an arbitrary `uniqueness-key` upsert path for `seed-data` in this scope. Per the resolved
  OQ-01 decision, `seed-data` keeps its existing **Name-keyed** dedup (`CreateBinding`→`ProcessRows`
  skips rows already present by `Name`); the contract is documented as *"a row is replay-safe only when
  the target schema has a `Name` column AND the row carries a `Name`; rows without a `Name` (or schemas
  without a `Name` column) are non-convergent — a stable-`Id`, no-`Name` row causes a primary-key
  conflict on replay."* A value-updating upsert-by-`Id` (via `IDataBindingDbService.UpsertRow`, which
  keys on `Id`) or an optional `uniqueness-key` arg is an explicit future additive story, not this
  effort.
- Will NOT implement **full reconcile** for `update-entity` (making the column set *exactly equal* the
  request, which would delete unlisted columns). Only additive per-column ensure is in scope (see FR-04,
  OQ-02).
- Will NOT change the imperative fallback for `create-entity` in this effort beyond what convergence of the
  shared create path naturally provides; a dedicated `ensure-entity` is out of scope unless the create path
  is trivially covered.
- Will NOT re-open or duplicate the narrow verify-against-intent fix already landing inside PR #910
  (ENG-93374). This PRD is the systemic redesign that supersedes the need for those heuristics, not a rework
  of them.
- Will NOT add a new Creatio backend endpoint. Convergence must be built on the existing read surfaces
  (see A-01).

## User Stories (high level)

| As a | I want | So that |
|------|--------|---------|
| AI agent (app bootstrap) | to re-run a failed `sync-schemas` batch verbatim | a transient fault does not force me to hand-compose a catch-up batch |
| developer | `create-lookup` to add my requested columns to a lookup that already exists | a pre-existing schema is not silently accepted while dropping my columns |
| developer | `update-entity` to treat an already-applied column change as success on replay | an ambiguous network failure does not turn a completed change into a reported failure |
| CI pipeline author | deterministic, repeatable schema provisioning | pipeline retries converge to the same end state without duplicate rows or spurious failures |
| QA engineer | an explicit, documented seed-data contract | I can test either upsert-by-key or fail-fast against a stated expectation |

## Feature Requirements

| ID | Requirement | Priority |
|----|------------|---------|
| FR-01 | `create-lookup` becomes convergent: read the target schema once; create it if absent; if present, confirm it is the caller's schema in the target package (from that same read) and add only the missing requested columns; treat an already-correct schema as success without recreating or issuing a separate verify read-back. | Must |
| FR-02 | Convergent `create-lookup` must converge the **Lookups registration** too (`EnsureLookupRegistration`), not only the schema object — an existing schema with a missing registration is reconciled, not reported as done. | Must |
| FR-03 | A durable pre-existing collision (a schema of the same name that is NOT the caller's intended schema, e.g. wrong package, incompatible parent/kind — a `BaseEntity`-derived entity vs. a `BaseLookup` — or incompatible columns) must be surfaced as an explicit, machine-readable failure — never masked as success. | Must |
| FR-04 | `update-entity` reconciles the requested column set against current state per-column: for each requested column, add-if-absent / modify-if-different; a requested `remove` means "ensure absent" (already-absent is success). Columns not named in the request are left untouched. | Must |
| FR-05 | Every convergent operation is idempotent: repeating it (including after an ambiguous failure where the mutation already applied) yields `success: true` with no duplicate or rejected mutation. | Must |
| FR-06 | Convergence must reuse the existing state-read surfaces (schema existence + column-level detail) without introducing a new MCP round-trip or a new backend endpoint. | Must |
| FR-07 | The tool contract (`ToolContractGetTool.BuildSchemaSync`), the tool `[Description]`, the four guidance resources, and `docs/commands/sync-schemas.md` describe the convergent semantics and the resolved seed-data decision. | Must |
| FR-08 | `seed-data` keeps its existing **Name-keyed** dedup (`CreateBinding`→`ProcessRows` skips rows already present by `Name`): a row is convergent (replay-safe) only when the target schema has a `Name` column AND the row carries a `Name`; rows without a `Name` (or schemas without a `Name` column) are non-convergent and are documented as such (a stable-`Id`, no-`Name` row PK-conflicts on replay). No upsert-by-key path is added in this scope. The choice is resolved by OQ-01. | Must |
| FR-09 | The convergent redesign preserves the resume-plan / partial-result output shape introduced by PR #910 so existing consumers are not broken; convergence should reduce, not grow, the heuristic branches those paths carry. | Should |
| FR-10 | `create-entity` reuses the convergent create path where it shares logic with `create-lookup` (create-if-absent + add-missing-columns), to the extent this is a natural consequence of FR-01 and does not require full `ensure-entity` scope. | Could |

## MCP Tool-Contract Impact

`sync-schemas` is an **MCP-only tool** — it has no standalone CLI verb, therefore **no CLI flags**.
The template's "CLI Impact" section is reframed here as MCP tool-contract impact.

| Change | Details | Breaking? |
|--------|---------|-----------|
| Operation semantics | `create-lookup` / `update-entity` become convergent (superset of current behavior: also read-and-reconcile). | No (superset) — pending OQ-03 |
| Operation type naming | Keep existing type names (`create-lookup`, `update-entity`) with convergent behavior, OR rename to `ensure-lookup` / etc. with a compatibility alias + drift guard. | Depends — see OQ-03 |
| New optional arg (seed-data) | Possible `uniqueness-key` (upsert-by-key). Pending OQ-01. | No (additive) if adopted |
| Result fields | May add explicit "no-op / already-satisfied" and "durable-collision" discriminators to per-op results. | No (additive) |

All new operation-type names and argument field names must be **kebab-case** (CLIO001 governs MCP
argument naming the same as CLI flags): e.g. `ensure-lookup`, `uniqueness-key`, `reference-schema-name`.
If any operation type is renamed, add a `McpToolCompatibilityCatalog` entry for the old name and keep the
`WorkspaceTemplateGuidanceDriftTests` oracle green.

## Acceptance Criteria

- [ ] AC-01: Given a `create-lookup` op for a schema that does not exist, when the batch runs, then the
      lookup schema is created, its Lookups registration is ensured, and the result is `success: true`.
- [ ] AC-02: Given a `create-lookup` op for a schema that already exists in the target package with a
      subset of the requested columns, when the batch runs, then only the missing columns are added, no
      recreation is attempted, and the result is `success: true`.
- [ ] AC-03 (the thesis): Given any convergent op whose mutation already applied on the server but whose
      response was lost, when the identical batch is re-run, then the result is `success: true` with no
      masked failure and no duplicate or rejected mutation.
- [ ] AC-04: Given a `create-lookup` op whose schema name collides with a schema in a DIFFERENT package
      (or an incompatible existing schema), when the batch runs, then the op fails with an explicit
      machine-readable collision error and is NOT reported as success.
- [ ] AC-05: Given an `update-entity` op requesting columns that are already present and identical, when
      the batch runs, then no mutation is issued for those columns and the op is `success: true`.
- [ ] AC-06: Given an `update-entity` op with a `remove` for a column that is already absent, when the
      batch runs, then the op treats it as "ensure absent" and returns `success: true`.
- [ ] AC-07: Given an `update-entity` op, when it runs, then columns not named in the request remain
      unchanged (no full-reconcile deletion).
- [ ] AC-08: Given the resolved seed-data decision (OQ-01: **Name-keyed dedup**, committed), when a
      `seed-data` op carrying `Name`-bearing rows (target schema has a `Name` column) is re-run, then
      already-present-by-`Name` rows are skipped (`SkippedRows`, no duplicates) and the op is
      `success: true`; and the contract/docs explicitly state *"a row is replay-safe only when the target
      schema has a `Name` column AND the row carries a `Name`; rows without a `Name` (or schemas without a
      `Name` column) are non-convergent — a stable-`Id`, no-`Name` row causes a primary-key conflict on
      replay."* Arbitrary `uniqueness-key` (or upsert-by-`Id`) is explicitly deferred to a future additive
      story (not in this scope).
- [ ] AC-09: Given a clean batch with no collisions, when it runs, then it introduces **no additional MCP
      round-trip** and performs **no separate verify read-back** (correctness comes from idempotent re-run,
      not a post-write read). Server-side reads for delta computation are not counted against the round-trip
      budget: the create-only path does 1 read/op, the reconcile-existing path up to 2 reads/op, `update-entity`
      1 read/op — all server-side within the single batch call (FR-06 budget; resolved per ADR OQ-04/OI-01).
- [ ] AC-ERR: Given invalid input (e.g. a durable collision or a malformed uniqueness key), the operation
      result carries `success: false` with a user-friendly `Error: {message}` and the batch stops on first
      failure (existing stop-on-first-failure contract preserved).

## Assumptions Index

| # | Assumption | Risk if wrong |
|---|-----------|--------------|
| A-01 | Existing read tools (`FindEntitySchemaCommand` for existence/package, `get-app-info` / `get-entity-schema-properties` for column-level detail) expose enough state to compute the delta with no new backend endpoint. | Convergence needs a new endpoint → scope and ClioGate work expand; ADR must re-plan. |
| A-02 | `create-lookup` "done" definition includes the Lookups registration, so ensure must converge both schema and registration. | Ensure-lookup converges the schema but leaves a stale/missing registration → lookup not usable in the Lookups section. |
| A-03 | Making the existing operation types convergent is a behavioral superset (still creates-if-absent; additionally adds-missing) and therefore backward-compatible for current callers. | If any caller relies on "fails when schema exists" as a signal, the superset silently changes their control flow. |
| A-04 | Per-column ensure (additive) is the intended reading of "reconcile the target column set"; full reconcile (delete-unlisted) is out of scope. | Building full reconcile would delete user columns = data loss. |
| A-05 | The reads occur server-side within the single existing batch call and per-tenant lock, so latency cost is bounded and does not add MCP round-trips. | Reads add client round-trips → the round-trip-saving rationale for `sync-schemas` regresses (SM-03). |
| A-06 | PR #910 (ENG-93374) lands first; this effort builds on its resume-plan/result shape as the baseline. #910 independently rewrites `ExecuteCreateSchema`/`ExecuteUpdateEntity` (per-op retry + resume-plan), so Story 6 must reconcile two overlapping rewrites of those methods and re-verify the preserved result shape — a substantial rebase (Story 6 sized L), not a trivial merge. | If #910 is abandoned/reworked, the baseline this PRD assumes shifts and FR-09 must be revisited; the double-rewrite reconciliation cost also grows if #910's rewrite diverges further. |

## Open Questions

| # | Question | Owner | Status / Resolution |
|---|---------|-------|-----|
| OQ-01 | **seed-data decision (required):** add upsert-by-key (require a row-identity/uniqueness key in the seed-row contract) OR keep the documented non-convergent fail-fast? | Alex Kravchuk | **RESOLVED (2026-07-20):** Name-keyed dedup, committed. `seed-data` stays on `DataBindingDbCommand` (`CreateBinding`→`ProcessRows`), which skips rows already present by `Name`; a row is replay-safe only when the target schema has a `Name` column AND the row carries a `Name`. Rows without a `Name` (or schemas without a `Name` column) are non-convergent — a stable-`Id`, no-`Name` row causes a primary-key conflict on replay — and are documented as such. Arbitrary `uniqueness-key` upsert (or upsert-by-`Id` via `IDataBindingDbService.UpsertRow`) deferred to a future additive story. Honors the PRD non-goal. (ADR §Open-question resolutions.) |
| OQ-02 | Should full reconcile (delete columns not in the request) ever be offered as an explicit opt-in mode later, or permanently excluded as too destructive for an "ensure" contract? | Alex Kravchuk | **DEFERRED:** out of scope here; per-column additive ensure only (A-04). Not resolved — revisit if an explicit opt-in destructive mode is ever requested. |
| OQ-03 | **Naming/compat:** keep existing type names (`create-lookup`, `update-entity`) with superset convergent behavior, OR introduce new `ensure-*` type names? | Alex Kravchuk | **RESOLVED (2026-07-20):** no rename. Keep `create-lookup` / `update-entity`; new behavior is a pure superset, surfaced via the additive `outcome` discriminator. Zero `McpToolCompatibilityCatalog` / `WorkspaceTemplateGuidanceDriftTests` churn. (ADR §Open-question resolutions.) |
| OQ-04 | What is the agreed latency/round-trip budget for the read-before-write step (SM-03 / AC-09 threshold)? | Alex Kravchuk | **RESOLVED (2026-07-20):** budget = zero added MCP round-trips + no post-write verify read-back; server-side DataService reads inside the single batch call are not counted (up to 2 server-side reads on the reconcile path). SM-03/AC-09 reworded accordingly (OI-01 closed). SM-03c p50 wall-time is the real regression guard. (ADR OQ-04.) |
| OQ-05 | How is "the caller's intended schema" (FR-03) identified for the collision check — package match only, or package + column-shape compatibility? | Alex Kravchuk | **RESOLVED (2026-07-20):** package-match gate **plus a parent/kind-compatibility check on the same-package branch**. A name collision in a DIFFERENT package → `success:false` + collision error. A same-package schema whose parent/kind is incompatible with the requested lookup (e.g. a `BaseEntity`-derived entity vs. a `BaseLookup`) is ALSO surfaced as a collision (never silently reconciled with lookup columns + a forced `EnsureLookupRegistration`). Same-package column-type differences remain per-column modify-conflicts (AC-ERR), not schema collisions. Name/package comparison uses `OrdinalIgnoreCase`. Known limitation: `FindEntitySchemaCommand.FindSchemas` filters `ManagerName == "EntitySchemaManager"`, so a same-named schema under a different manager (e.g. a source-code schema) is invisible to the gate. (ADR OQ-05.) |
| OI-01 | AC-09 / SM-03 literal "one extra state-read per operation" wording contradicts the 2-read reconcile path flagged by the ADR. | Alex Kravchuk | **RESOLVED (2026-07-20):** AC-09 and SM-03 reworded to the round-trip formulation (server-side reads not counted). Contradiction removed. |

## Dependencies

- **Depends on**: PR #910 / ENG-93374 (`sync-schemas` per-op transient retry + resume-plan + collision-probe)
  — the predecessor whose residual heuristic holes motivate this systemic fix; its resume-plan result shape
  is the baseline.
- **Depends on**: existing read surfaces `FindEntitySchemaCommand`, `get-app-info`,
  `get-entity-schema-properties`; existing write commands `CreateEntitySchemaCommand`,
  `UpdateEntitySchemaCommand`, `DataBindingDbCommand`, and `ILookupRegistrationService`.
- **Blocks**: reliable unattended app-bootstrap flows (parent ENG-93367 "Analyze long app creating") that
  need retries/resume to be safe by construction rather than by heuristic.
- **Related**: `docs/McpCapabilityMap.md` (sync-schemas long-running entry), the four guidance resources
  updated by #910, and `WorkspaceTemplateGuidanceDriftTests` if any operation type is renamed.
