# Story 9: De-globalize the execution lock + isolate the capture it guards (FR-06 → FR-05, COUPLED)

**Feature**: mcp-http-credential-passthrough
**FR coverage**: FR-06 (isolate capture — do FIRST), FR-05 (per-tenant lock — do SECOND). COUPLED — one design unit.
**PRD**: [prd-mcp-http-credential-passthrough.md](../prd/prd-mcp-http-credential-passthrough.md)
**ADR**: [adr-mcp-http-credential-passthrough.md](../adr/adr-mcp-http-credential-passthrough.md) (step 9 / 9a+9b; FR-05+FR-06 coupled)
**Jira**: [ENG-93208](https://creatio.atlassian.net/browse/ENG-93208)
**Status**: ready-for-dev
**Size**: L (full day)
**Depends on**: Story 1 (spike — FR-06 async-flow verdict), Story 2 (spike), Story 7 (resolution), Story 8 (cache-key identity feeds the per-tenant lock)

---

## As a

QA engineer / developer certifying multi-tenant safety

## I want

the process-wide capture state isolated per-execution-scope **first**, then the single global execution lock replaced by a per-credential-identity lock across the **full** lock inventory

## So that

different tenants are no longer serialized against each other **and** concurrent tenants can never corrupt or leak each other's captured log lines / db-operation context into each other's MCP responses

> **STRICT INTERNAL ORDER: FR-06 (isolate capture) BEFORE FR-05 (narrow the lock).** Shipping the lock change without capture isolation turns a throughput fix into a cross-tenant data-disclosure bug.

---

## Acceptance Criteria

### FR-06 — isolate capture (do first)
- [ ] **AC-01** — Given concurrent tool invocations, when each toggles capture at the tool-execution boundary (where `BaseTool.InternalExecute` sets `logger.PreserveMessages = true`), then `ConsoleLogger`'s capture buffer (`PreserveMessages` + `LogMessages`, `FlushAndSnapshotMessages`/`ClearMessages`) is per-async-flow storage — console **rendering** stays on the singleton, only the **capture buffer** is per-flow (maps FR-06).
- [ ] **AC-02** — Given concurrent invocations, when each touches db-operation context, then `DbOperationLogContextAccessor.CurrentSession`/`LastCompletedPath` is per-async-flow (`AsyncLocal`) storage (maps FR-06).
- [ ] **AC-03 (scope placement)** — Given the capture-isolation scope, when established, then it is inside the **tool-execution boundary** (the `try/finally` around `command.Execute`), **not** in HTTP middleware (else it inherits RISK #1) (maps FR-06).
- [ ] **AC-04 (concurrency prerequisite)** — Given two different-credential requests emitting log lines / db-context, when both run, then each MCP response contains **only its own** log lines and db-context — **no** cross-tenant bleed (maps FR-06; AC-05). If Story 1 found flows are **shared**, capture falls back to a per-invocation object resolved from the per-call child container instead of AsyncLocal on the singleton.

### FR-05 — per-tenant lock (do second, only after FR-06)
- [ ] **AC-05 (9a shared-root sites)** — Given the shared-root lock (`McpToolExecutionLock.SyncRoot` / `BaseTool.CommandExecutionLock`), when replaced by `ITenantExecutionLockProvider.GetLock(cacheKey)` keyed by the **same** identity as the container cache (FR-07/Story 8), then **all** shared-root users migrate: `BaseTool.cs:15-17,194-218`, `PageEditToolHelpers.cs:8-30`, `PageSyncTool.cs:234-252`, `SchemaSyncTool.cs:79-107` (maps FR-05).
- [ ] **AC-06 (9b tool-local static sites)** — Given the tool-local `static` locks (their own objects, NOT `McpToolExecutionLock`), when re-keyed to the per-tenant provider, then `CompileCreatioTool.cs:17-21,73-101` and `AddItemModelTool.cs:20-24,68-87` no longer serialize `compile-creatio` / `add-item model` across all tenants (maps FR-05 — 9b is easy to forget and independently regresses concurrency).
- [ ] **AC-07** — Given two concurrent **different-tenant** requests, when they run, then they are **not** serialized by a single global lock (measured concurrency > 1 / no global-mutex contention) (maps FR-05; AC-06).
- [ ] **AC-08** — Given two concurrent **same-tenant** calls, when they run, then the per-tenant lock still serializes them (protecting the shared authenticated client's session/reauth) — correct granularity (maps FR-05).

## Implementation Notes

From ADR step 9 (COUPLED, ordered FR-06 → FR-05):

- **FR-06 first:** back `ConsoleLogger` capture buffer + `DbOperationLogContextAccessor` state with `AsyncLocal`-scoped storage established at the tool-execution boundary (BaseTool `try/finally`), NOT middleware. Justified: `BindingsModule` registers `ILogger` as singleton `ConsoleLogger.Instance` (line 167) and `DbOperationLogContextAccessor` singleton (line 168) — isolation must live inside the instance. **Concurrency prerequisite** (from Story 1): AsyncLocal is correct only if concurrent invocations carry independent `ExecutionContext` branches; else fall back to a per-invocation capture object from the child container.
- **FR-05 second:** new singleton `ITenantExecutionLockProvider { object GetLock(string cacheKey); }` keyed by the Story 8 identity. Replace **every** MCP execution lock — split into **9a** (shared-root users) and **9b** (tool-local static locks). Do NOT stop at `McpToolExecutionLock`.
- **Latent shared state (Medium risk):** the global lock today wraps the entire `command.Execute` body, so it may serialize other static/shared mutation beyond logger/db capture — the concurrency-isolation e2e (Story 15c) must probe beyond logger/db-context.

Key files: `clio/Common/Logger/ConsoleLogger.cs`, `DbOperationLogContextAccessor`, `clio/Command/McpServer/BaseTool.cs`, `PageEditToolHelpers.cs`, `PageSyncTool.cs`, `SchemaSyncTool.cs`, `CompileCreatioTool.cs`, `AddItemModelTool.cs`, new `ITenantExecutionLockProvider`.
Pattern to follow: existing `McpToolExecutionLock` usage sites; Story 8 cache-key.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | AsyncLocal capture isolation (two flows → distinct buffers); db-context per-flow; `ITenantExecutionLockProvider` returns same lock per key, distinct per key | `clio.tests/Command/McpServer/TenantExecutionLockProviderTests.cs`, `clio.tests/Common/ConsoleLoggerCaptureIsolationTests.cs` |
| Integration `[Category("Integration")]` | concurrent same-tenant serialized; different-tenant not serialized (in-process, no live Creatio) | `clio.tests/Command/McpServer/TenantLockConcurrencyTests.cs` |

Note: the full different-credential cross-tenant no-bleed proof is the Story 15c concurrency-isolation e2e (AC-05/AC-06).
Test naming `MethodName_ShouldBehavior_WhenCondition`; AAA + `because` + `[Description]`; deterministic (no arbitrary sleeps).
Targeted run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=McpServer|Module=Common)"`

## Definition of Done

- [ ] Code compiles with no new `CLIO*` warnings in modified files (CLIO001–CLIO005 clean)
- [ ] No new CLI flags (any touched kebab-case, CLIO001)
- [ ] `ITenantExecutionLockProvider` registered singleton in `BindingsModule` — no MediatR; no raw `HttpClient`
- [ ] **FR-06 shipped BEFORE FR-05** (capture isolated at exec boundary before lock narrowed)
- [ ] **Full lock inventory migrated**: 9a shared-root (`BaseTool`, `PageEditToolHelpers`, `PageSyncTool`, `SchemaSyncTool`) AND 9b tool-local static (`CompileCreatioTool`, `AddItemModelTool`) — not just `McpToolExecutionLock`
- [ ] MCP surface + docs reviewed (FR-15) — state outcome
- [ ] Unit + Integration tests with correct `[Category]`; AAA + `because` + `[Description]`
- [ ] Targeted `dotnet test --filter "Category=Unit&(Module=McpServer|Module=Common)"` green before commit
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
