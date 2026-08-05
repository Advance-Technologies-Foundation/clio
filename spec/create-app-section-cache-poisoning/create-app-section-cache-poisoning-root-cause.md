# create-app-section cache poisoning — root cause (ENG-94418)

## Summary

A concurrent `create-app-section` whose creation is **abandoned mid-flight** can leave the Creatio
server-side **schema-manager cache** holding a *phantom* for the half-created section. That phantom
then breaks two later, unrelated operations:

1. **Poisoned hierarchy read** — a page-schema-hierarchy read fails with the SQL Server error
   `Incorrect syntax near ')'`.
2. **Poisoned save block** — a later page save is rejected with `Item with name '…' not found`.

Both are **symptoms of the same server-side defect**. clio only *surfaces* them (and *drives* the
concurrency that triggers them); it does **not** build the failing SQL and cannot repair the server
cache. The true fix is server-side and is escalated as a linked Creatio platform ticket (R5).

## Why the two symptoms share one cause

When a section create is abandoned after it has partially registered its schema(s), the server
schema-manager cache retains an in-memory entry (the "phantom") for a section whose backing rows are
missing or inconsistent:

- **Empty-IN() hierarchy read.** Resolving the parent/child hierarchy for a page builds a
  `... WHERE UId IN (<parents>)` query from a **cached collection**. For the phantom that collection
  is empty, so the server emits `... IN ()` and SQL Server rejects it with
  `Incorrect syntax near ')'`. This is a *server-built* query — clio passes the schema UId and the
  package UId and receives the error verbatim.
- **"Item with name not found" save block.** A later save resolves the replacing/base schema through
  the same poisoned cache; the cache points at a name/UId that no longer reconciles with the stored
  rows, so the save is rejected. Because the cache is **environment-wide**, this can block *unrelated*
  page saves until the cache is cleared — the blast radius the ticket calls out.

The enabling condition (an abandoned create) is made more likely by the 90 s false-timeout that
detaches an in-flight create; that timeout is tracked **separately** as **ENG-94419** and is out of
scope here.

## clio's role and surfacing points

clio is the concurrency **driver** and the symptom **surface**, not the cause:

- **Concurrency driver (already hardened).** Same-tenant `create-app-section` is serialized
  in-process so clio never issues two racing section creates through one MCP server:
  - Per-tenant MCP execution lock — `BaseTool<T>.ExecuteWithCleanLog` /
    `ExecuteUnderTenantLock` over `McpToolExecutionLock` / `TenantExecutionLockProvider`
    (`clio/Command/McpServer/Tools/`).
  - Per-environment+application section-create guard —
    `ISectionCreateSerializationGuard` / `SectionCreateSerializationGuard`
    (`clio/Command/ApplicationSectionCreateSerializationGuard.cs`, prior art **ENG-93089**).
  - **Known limitation (Scope A):** neither serializes across *separate* `clio` OS processes.
    Cross-process CLI-vs-CLI serialization (Scope B) was considered and **declined** — the production
    path is a single agent → single `clio mcp-server` process, already serialized per tenant.
- **Symptom surface (this change).** The poisoned reads reach the user through
  `IPageDesignerHierarchyClient.GetParentSchemas`
  (`clio/Command/PageDesignerHierarchyClient.cs`), consumed by both:
  - `get-page` — `PageGetCommand.TryGetPage` (`clio/Command/PageGetOptions.cs`), and
  - `update-page` — `PageUpdateCommand.TryGetHierarchy` (`clio/Command/PageUpdateOptions.cs`).
  The save-block symptom is surfaced by `PageUpdateCommand.AppendActionableHint`
  (the existing `Item with name … not found` phantom-cache save hint).

## clio change (Scope A: harden + escalate)

- **Diagnostic recovery hint (R4).** A new additive helper `PageHierarchyRecoveryHint.Append`
  (`clio/Command/PageHierarchyRecoveryHint.cs`) appends an actionable phantom-cache recovery hint to
  the hierarchy-read failure error **only** when it carries the empty-IN() `Incorrect syntax near ')'`
  signature. It mirrors the existing save-path `AppendActionableHint` pattern and is wired into the
  `get-page` and `update-page` surfacing seams above. Two scoping decisions came out of review:
  - **An empty hierarchy is NOT hinted (F1).** It has legitimate non-phantom causes (a stale
    post-save bundle, a wrong or renamed schema name), so keying the hint on it would recommend an
    unnecessary production restart. Only the SqlException signature fires the hint.
  - **The hint is scoped to the hierarchy read itself (F2).** `get-page` wraps only the
    `GetParentSchemas` call in a narrow `try/catch` (mirroring `update-page`'s `TryGetHierarchy`); an
    exception from any other step of `TryGetPage` is surfaced without the hint.

  **Q1 answered on a live stand:** flushing Redis (`clio clear-redis-db`) does **not** clear the
  phantom — it lives in server in-process memory. The hint therefore directs to **Restart Creatio**
  as the confirmed recovery and no longer offers lighter "may help" steps. A web-farm /
  Redis-distributed deployment may behave differently; that is untested.

  **Known limitation — the signature is Microsoft SQL Server specific.** `Incorrect syntax near ')'`
  is the SQL Server wording, which is what the repro stand emitted. Whether the phantom produces a
  SQL syntax error at all on PostgreSQL is unobserved, and PostgreSQL words it differently
  (`syntax error at or near ")"`). On PostgreSQL the hint therefore does **not** fire — a missed
  diagnostic, not a wrong one. The signature is deliberately not broadened on inference: firing
  wrongly recommends restarting a production application, and the Restart-Creatio recovery is itself
  confirmed only on the MSSQL / .NET Framework stand. Broadening requires observing the symptom on a
  PostgreSQL stand first.
- **Regression coverage (R2/R3).** A unit test pins that two concurrent same-tenant
  `create-app-section` executions never overlap (serialize via the per-tenant lock) and fails if that
  lock is removed from the create path.

## Out of scope (escalated or tracked elsewhere)

- The server-side fix (server building `IN ()` from an empty cached collection; schema-manager cache
  coherency; environment-wide save-blocking blast radius) — **escalated as ENG-94564** (R5); clio
  cannot fix it.
- Cross-process CLI serialization (Scope B) — **declined**.
- The 90 s create false-timeout that enables the abandoned create — **ENG-94419**.

## Acceptance-criteria coverage (ENG-94418)

The ticket's four criteria are **not** all closable inside clio. Honest mapping:

| Criterion | Status in clio | Where the rest lives |
|---|---|---|
| Root cause identified for the empty-IN-list hierarchy query | **Done** — identified as a server schema-manager cache phantom; clio only receives the server-built error | Server fix: **ENG-94564** |
| Concurrent `create-app-section` either serializes or leaves no partially-wired section | **Partial** — the existing in-process per-tenant serialization is *pinned* by a regression test; no new serialization added, and separate `clio` OS processes are still not serialized (Scope B declined) | Partially-wired section is a server-side outcome: **ENG-94564**; the abandoning timeout: **ENG-94419** |
| A half-created section cannot block saves of unrelated schemas | **Not addressed in clio** — the environment-wide blast radius is server cache behavior; clio can only surface it (save-path `AppendActionableHint`, already shipped) | **ENG-94564** |
| Regression coverage for the concurrent shape | **Done** — unit-level: same-tenant creates serialize, different-tenant creates overlap (proving the probe detects real overlap) | — |

## References

- Jira: **ENG-94418** (this bug), **ENG-94564** (server-side root cause), **ENG-94419** (90 s
  false-timeout), **ENG-93089** (parallel-contention guard, prior art), parent epic **ENG-85256**.
- Timeout-diagnostics prior work: `spec/create-app-section-timeout-diagnostics/`.
