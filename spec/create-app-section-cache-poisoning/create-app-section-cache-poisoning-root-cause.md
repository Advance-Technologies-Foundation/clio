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
  the hierarchy-read failure error **only** when it carries a poisoned-cache signature (the empty-IN()
  `Incorrect syntax near ')'` text, or an empty hierarchy). It mirrors the existing save-path
  `AppendActionableHint` pattern and is wired into the `get-page` and `update-page` surfacing seams
  above. The hint is worded as **escalating recovery options** with **Restart Creatio** as the
  *guaranteed* fallback; the lighter recoveries (`clear-redis-db-by-environment`, `sync-schemas`) are
  offered as "may help" only, because which lighter step actually clears the server schema-manager
  phantom is **not yet confirmed on a live stand** (open question Q1 / RISK1 — to be verified during
  the verification stage).
- **Regression coverage (R2/R3).** A unit test pins that two concurrent same-tenant
  `create-app-section` executions never overlap (serialize via the per-tenant lock) and fails if that
  lock is removed from the create path.

## Out of scope (escalated or tracked elsewhere)

- The server-side fix (server building `IN ()` from an empty cached collection; schema-manager cache
  coherency; environment-wide save-blocking blast radius) — **escalated as a linked Creatio platform
  ticket (R5)**; clio cannot fix it.
- Cross-process CLI serialization (Scope B) — **declined**.
- The 90 s create false-timeout that enables the abandoned create — **ENG-94419**.

## References

- Jira: **ENG-94418** (this bug), **ENG-94419** (90 s false-timeout), **ENG-93089** (parallel-contention
  guard, prior art), parent epic **ENG-85256**.
- Timeout-diagnostics prior work: `spec/create-app-section-timeout-diagnostics/`.
