# PRD: sync-schemas create-lookup resume — verify against intent

**Status**: Draft
**Author**: PM Agent
**Created**: 2026-07-20
**Jira**: ENG-93374

---

## Problem Statement

The sync-schemas MCP tool's `ExecuteCreateSchema` (`clio/Command/McpServer/Tools/SchemaSyncTool.cs`, ~L326-342) forces a create-lookup to `success:true, status:"completed"` whenever the create returns exit≠0 with no thrown exception AND a same-named schema exists in the TARGET package — assuming it is our own schema from an interrupted prior attempt. That single observable signal covers two different realities: a legitimate resume (the schema was really created with the requested columns, only the response was lost) and a durable collision (the schema pre-existed for an unrelated reason, the create genuinely failed, and the requested columns were never applied). In the durable-collision case the branch silently drops the requested columns and flips the pre-PR behavior, so an agent creating `UsrColors` with a new `UsrHexCode` column against an already-existing `UsrColors` is told the operation completed while `UsrHexCode` was never applied.

## Goals

- [ ] Goal 1 — Distinguish a real resume from a durable collision before forcing success. Success metric SM-01: 100% of create-lookup ops whose requested columns are absent from the existing target-package schema return `success:false` with the "use update-entity" hint (was: forced `success:true`). Counter: legitimate resume of an interrupted create (requested columns already present, or `op.Columns` empty) MUST still complete registration and return success — no regression to the ENG-93374 resume fix.
- [ ] Goal 2 — Never fabricate verified success when verification could not run. SM-02: a transient/network failure of the column-verification probe yields a distinct `resumed-existing` status carrying an explicit "columns NOT verified" warning in 100% of such cases. Counter: the transient degrade path MUST NOT be reported as plain `completed`/verified success.
- [ ] Goal 3 — Result messages stay consistent with the result status. SM-03: no `completed`/success result carries Error-level `Messages` from the failed create attempts. Counter: genuine diagnostics for failing ops MUST still be surfaced (no silent message loss on `success:false`).

## Non-goals

- Will NOT convert schema operations to convergent "ensure" semantics (read → apply delta → verify). That systemic redesign is tracked separately in **ENG-93807** and is explicitly out of scope here.
- Will NOT change resume behavior for the empty-columns case — when `op.Columns` is empty there is nothing to lose and the current resume path is retained as-is.
- Will NOT add automatic column application/`update-entity` on a durable collision — the tool returns the hint and lets the caller decide.
- Will NOT introduce any new CLI flag or verb — this is an internal behavior fix to an existing MCP tool.

## User Stories (high level)

| As a | I want | So that |
|------|--------|---------|
| developer (AI coding agent) | a create-lookup that collides with a pre-existing unrelated schema to fail honestly with a "use update-entity" hint | I am not told the operation completed while my requested columns were silently dropped |
| developer | an interrupted create whose response was lost to resume and finish registration | re-submitting the same create-lookup completes instead of failing on the collision |
| CI pipeline author | a distinct, machine-detectable status when column verification could not run due to a transient fault | my automation does not treat unverified state as confirmed success |
| QA engineer | a regression test pinning the different-package guard | a future refactor cannot silently start skipping create for foreign-package collisions |

## Feature Requirements

| ID | Requirement | Priority |
|----|------------|---------|
| FR-01 | When `op.Columns` is EMPTY, the create-lookup resume branch MUST behave exactly as today: on a same-target-package collision, skip re-create and complete lookup registration, returning success. | Must |
| FR-02 | When `op.Columns` is NON-EMPTY, before resuming the tool MUST read the existing schema's actual column set (via `GetEntitySchemaPropertiesCommand`, resolved through the same `commandResolver`) and compare it against the requested columns. | Must |
| FR-03 | If any requested column is missing from the existing schema, the tool MUST NOT force success: it returns `success:false` plus the existing "schema already exists — use update-entity to add columns" collision hint (restoring pre-PR durable-collision behavior). | Must |
| FR-04 | If all requested columns are already present on the existing schema, the tool MUST treat it as a legitimate resume: skip re-create and complete registration, returning success. | Must |
| FR-05 | If the column-verification probe itself FAILS due to a network/transient fault, the tool MUST NOT return a blind `success:true`; it degrades to a distinct status `resumed-existing` carrying a warning that the requested columns were NOT verified. | Must |
| FR-06 | A forced-success / resumed `execution` result MUST keep its `Messages` consistent with its status — Error-level lines from failed create attempts MUST NOT surface inside a `completed`/success result. | Must |
| FR-07 | The different-package collision guard (`string.Equals(collision.ExistingPackageName, args.PackageName, …)`) MUST be preserved: a schema existing in a DIFFERENT package never skips create; the op fails and registration is not invoked. | Must |
| FR-08 | Column comparison MUST be case-insensitive on column names and MUST compare against the requested `op.Columns` set (missing = requested-but-absent); it does not require the existing schema to be column-identical (extra existing columns are allowed). | Should |
| FR-09 | A negative regression unit test MUST assert that a different-package collision does NOT skip create: `Success == false`, registration service `DidNotReceive().EnsureLookupRegistration(...)`, and create invoked exactly once. | Must |

## CLI Impact

| Change | Details | Breaking? |
|--------|---------|-----------|
| New flag | none | — |
| Modified flag | none | — |
| MCP tool behavior | `sync-schemas` create-lookup resume path now verifies requested columns against the existing schema before forcing success; adds `resumed-existing` status | No new surface; changes result semantics for the durable-collision case (restores pre-#910 `success:false`) |

All flags: **kebab-case only** (CLIO001 enforced). No new CLI flags are introduced — this is an internal behavior fix to the existing `sync-schemas` MCP tool. MCP impact is scoped to `sync-schemas`; see `docs/McpCapabilityMap.md`.

## Acceptance Criteria

- [ ] AC-01: Given a create-lookup op with empty `op.Columns` and a same-target-package collision, when `ExecuteCreateSchema` runs, then it skips re-create, completes registration, and returns `success:true`.
- [ ] AC-02: Given a create-lookup op with non-empty `op.Columns` where every requested column already exists on the target-package schema, when the op runs, then it resumes (skips re-create), completes registration, and returns `success:true`.
- [ ] AC-03: Given a create-lookup op with non-empty `op.Columns` where at least one requested column is MISSING from the existing target-package schema, when the op runs, then it returns `success:false` with the "schema already exists — use update-entity to add columns" hint and does NOT complete registration.
- [ ] AC-04: Given a create-lookup op with non-empty `op.Columns` and a collision, when the column-verification probe fails with a transient/network fault, then the result status is `resumed-existing` and includes a warning that the requested columns were NOT verified (not a plain verified success).
- [ ] AC-05: Given a create-lookup op whose collision resolves to a DIFFERENT package than `args.PackageName`, when the op runs, then create is invoked exactly once, `Success == false`, and the registration service `DidNotReceive().EnsureLookupRegistration(...)`.
- [ ] AC-06: Given a forced-success / resumed result, when the result is finalized, then its `Messages` contain no Error-level lines from the failed create attempts (status and messages are consistent).
- [ ] AC-ERR: Given a create-lookup that fails for a non-collision reason (no same-name schema found), when the op runs, then it returns `success:false` with the honest error and non-zero op outcome, unchanged from current behavior.

## Assumptions Index

| # | Assumption | Risk if wrong |
|---|-----------|--------------|
| A-01 | `GetEntitySchemaPropertiesCommand` can be resolved via `commandResolver` and exposes the existing schema's column set in a form comparable to `op.Columns`. | If the command cannot return columns reliably, FR-02 verification is unusable and the fix cannot distinguish the two realities. |
| A-02 | A transient probe fault is distinguishable from a definitive "schema has no such column" answer (so FR-05 does not swallow a real missing-column signal as `resumed-existing`). | Misclassification could report a durable collision as `resumed-existing` instead of `success:false`, weakening the intended honesty. |
| A-03 | The existing collision hint text ("use update-entity to add columns") is the pre-PR behavior QA/consumers expect for a durable collision. | If consumers depend on a different message shape, downstream automation may not parse the restored hint. |
| A-04 | `resumed-existing` is a new status value that consuming clients/tests tolerate (additive) without breaking on an unknown status. | A strict status enum on a consumer could reject the new value. |

## Open Questions

| # | Question | Owner | Due |
|---|---------|-------|-----|
| OQ-01 | Should `resumed-existing` be surfaced as `success:true` (with warning) or `success:false` in the result envelope? Leaning success-with-warning to preserve resume, but confirm with architect. | Architect | Phase 2 |
| OQ-02 | Is a per-column type/length comparison required, or is presence-by-name sufficient for FR-08? Current scope is presence-by-name. | Architect | Phase 2 |
| OQ-03 | Does the column-verification probe reuse the single-collision-probe budget, or is it an additional round-trip under the per-tenant lock? | Architect | Phase 2 |

## Dependencies

- Depends on: existing ENG-93374 resume fix already present in PR #910 (this refines that branch).
- Related: [PRD — Durable MCP invocation](prd-mcp-durable-invocation.md), [PRD — entity schema authoring gaps](prd-entity-schema-authoring-gaps.md).
- Ships inside: PR #910 (ENG-93374).
- Blocks: nothing directly; the convergent "ensure" redesign is tracked separately in ENG-93807 (non-goal here).
