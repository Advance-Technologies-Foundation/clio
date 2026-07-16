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

- Implementation started: 2026-07-09 (investigation only — no code; blocked by session/API limit, resets 18:30 Europe/Warsaw)
- Implementation completed: —
- Tests passing: —
- Notes:
  - **CRITICAL design finding (verified by reading `ConsoleLogger.cs`) — the naive "AsyncLocal-back `PreserveMessages`/`LogMessages`" would BREAK capture entirely.** Log capture does NOT happen where `PreserveMessages` is set/read; it happens at **drain time** in `ConsoleLogger.FlushQueueCore` (line 74: `if (PreserveMessages) LogMessages.Add(item)`), which runs on the shared background `_printThread` (started once at process init), NOT on the producing tool flow. A drain-thread AsyncLocal slot ≠ the tool flow's slot → nothing would be captured.
  - **Approved FR-06 mechanism:** move capture from drain-time to **enqueue-time** — inside the `Write*` methods (which run on the producing flow) — gated by a per-flow `AsyncLocal<bool>` PreserveMessages flag writing into a per-flow `AsyncLocal<List<LogMessage>>` buffer. Console **rendering** stays on the shared `_printThread` unchanged. The `PreserveMessages = true` setter must establish a FRESH per-flow buffer (neutralizes the `Program.cs:1200` process-wide MCP-mode set + any inherited startup-flow context, so each tool scope is isolated). `DbOperationLogContextAccessor.CurrentSession`/`LastCompletedPath` → static `AsyncLocal` analogously.
  - **CLI safety (checked):** production capture READERS are MCP-only (`BaseTool` + EntitySchema/CompileCreatio/SchemaSync/AddItemModel tools); no CLI production path reads `LogMessages`/`FlushAndSnapshotMessages`, and tests set/read on the same flow → AsyncLocal is safe.
  - **Still to do (FR-05, unchanged from the story):** `ITenantExecutionLockProvider` (DI singleton, keyed by the Story-8 cache identity) + `GetTenantKey` on `IToolCommandResolver` (shared key-builder); migrate ALL 6 lock sites (9a shared-root: `BaseTool` ×2, `PageEditToolHelpers`, `PageSyncTool`, `SchemaSyncTool`; 9b tool-local static: `CompileCreatioTool`, `AddItemModelTool`) — per-site decide re-key vs remove-if-redundant; wire Story-8 `MarkInUse`/`MarkAvailable` around the execution boundary. DI-breadth: adding a `ITenantExecutionLockProvider` ctor param to `BaseTool` touches many subclass ctors — assess vs a static facade that delegates to the DI provider.
  - Order remains STRICT: FR-06 (enqueue-time capture isolation) BEFORE FR-05 (lock de-globalization).

### Implementation (2026-07-09) — DONE

**FR-06 (shipped first) — capture isolation confirmed.**
- `clio/Common/ConsoleLogger.cs`: capture moved from drain-time (`FlushQueueCore`) to ENQUEUE-time. Added instance `AsyncLocal<bool> _preserveMessages` + `AsyncLocal<List<LogMessage>> _captureBuffer`. `PreserveMessages` setter establishes a FRESH per-flow buffer when set `true`. `LogMessages` returns the current flow's lazily-created buffer. New private `CaptureMessage(msg)` (gated by the flow-local flag, under `_messageBufferLock`) is called at the head of every enqueue (`Write`, `WriteError`, `WriteInfo`, `WriteLine`×2, `WriteWarning`, `WriteDebug`, `PrintTable`). `FlushQueueCore` no longer captures (console rendering only). `FlushAndSnapshotMessages`/`ClearMessages` still drain the queue (console) then snapshot/clear the per-flow buffer.
- `clio/Common/DbOperationLogging.cs`: `DbOperationLogContextAccessor.CurrentSession`/`LastCompletedPath` now static `AsyncLocal`; instance `_syncRoot` removed (per-flow slots need no lock).
- **Capture-mechanism change confirmed:** the naive AsyncLocal-on-drain approach would have captured nothing (drain runs on the shared `_printThread`); enqueue-time capture runs on the producing flow, so it works. All existing capture-reader tests (BaseTool response log-messages, DeleteSchemaTool trace, CompileCreatio/AddItemModel/SchemaSync/EntitySchema, RestoreDb/Installer/LastCompilation log-artifact) pass unchanged.

**FR-05 (shipped second) — full lock inventory de-globalized.**
- New `clio/Command/McpServer/Tools/TenantExecutionLockProvider.cs`: `ITenantExecutionLockProvider { object GetLock(string) }` + impl backed by `ConcurrentDictionary<string,object>` (OrdinalIgnoreCase). Process-wide `Shared` instance (private ctor) so the same tenant serializes on the same lock across root + per-session ephemeral containers.
- `clio/Command/McpServer/Tools/McpToolExecutionLock.cs`: rewritten from the global `SyncRoot` object into a static facade — `GetLock(key)` (null/blank → `SharedFallbackKey`), `MarkInUse`/`MarkAvailable` (no-op for fallback), `Configure(provider, cache)` wired once per host.
- `IToolCommandResolver.GetTenantKey(EnvironmentOptions)` added; `Resolve` + `GetTenantKey` share a new `ResolveSettingsAndKey` builder (passthrough branch → `BuildPassthroughCacheKey`; else registry/URI → `BuildCacheKey`). `GetTenantKey` never throws — a resolution failure yields a stable `unresolved:<id>` fallback.
- `BaseTool.cs`: removed the static shared-root lock; added `ResolveTenantLockKey`, `GetTenantExecutionLock`, `ExecuteUnderTenantLock` (locks per-tenant + `MarkInUse`/`MarkAvailable`), and an options-aware `ExecuteWithCleanLog(EnvironmentOptions, executor)` overload (the parameterless overload delegates with the shared fallback key). `InternalExecute(command, options)` now locks the per-tenant key and wires the in-flight guard in the try/finally.
- **DI-breadth choice: option (c) static facade.** BaseTool is the base of 62 tool types and `ExecuteWithCleanLog` has no options in scope; a ctor param (option a) would touch 62 subclass ctors. The facade delegates to the DI-registered `ITenantExecutionLockProvider` (registered as `TenantExecutionLockProvider.Shared` in `BindingsModule`, added to the `RegisterAssemblyInterfaceTypes` skip-list because its ctor is private) and is `Configure`-wired at both hosts: `McpServerCommand` (stdio, via new ctor deps) and `McpHttpServerCommand.Run` (after `app.Build()`).
- **Per-site decisions (all 6 named + the transitive shared-root users):**
  - `BaseTool.InternalExecute` (~194) — re-keyed to per-tenant lock + `MarkInUse`/`MarkAvailable`.
  - `BaseTool.ExecuteWithCleanLog` (~29) — re-keyed via the new options overload + `MarkInUse`/`MarkAvailable`; all ~19 caller sites updated to pass their `options` (env-less/pre-computed callers keep the shared-fallback overload).
  - `PageEditToolHelpers`, `PageSyncTool`, `SchemaSyncTool` — re-keyed to `commandResolver.GetTenantKey(options)` + `MarkInUse`/`MarkAvailable`.
  - `CompileCreatioTool` (9b), `AddItemModelTool` (9b) — tool-local static locks removed; re-keyed to the per-tenant provider + `MarkInUse`/`MarkAvailable`.
  - Transitive shared-root users that used `CommandExecutionSyncRoot`: `EntitySchemaTool` (CreateLookupTool) and `ApplicationDeleteTool` → `ExecuteUnderTenantLock(options, …)`; `CreateUiProjectTool` and `WorkspaceSyncTool` → `ExecuteUnderTenantLock` with the SHARED fallback key deliberately, because they mutate process-wide `CurrentDirectory` and MUST hold a single global lock (lock ordering is always global→tenant, never the reverse, so no deadlock).
- **CLI-safety grep result:** capture readers (`LogMessages`/`FlushAndSnapshotMessages`, `CurrentSession`/`LastCompletedPath`) are MCP-only — `BaseTool`, `EntitySchemaTool`, `CompileCreatioTool`, `AddItemModelTool`, `SchemaSyncTool`, and the `CreatioInstallerService`/`RestoreDb` native-line writers, each of which sets and reads within the SAME synchronous `Execute` flow. No CLI path reads the capture buffer, and no site sets on one thread and reads on another. `Program.cs:~1200` still sets `PreserveMessages = true` on the MCP startup flow — now a harmless no-op for tool handlers (each establishes its own fresh per-flow buffer).

**Tests / gates.**
- New: `clio.tests/Common/ConsoleLoggerCaptureIsolationTests.cs` (2 tests — logger + db-context per-flow, `Barrier`-forced overlap), `clio.tests/Command/McpServer/TenantExecutionLockProviderTests.cs` (4 unit), `clio.tests/Command/McpServer/TenantLockConcurrencyTests.cs` (2 `[Category("Integration")]` — same-tenant serialize, different-tenant concurrent).
- Build clean; no new `CLIO*` warnings in modified files (only 2 pre-existing CLIO005 on `CreateEntity/CreatePageBusinessRuleCommand` registrations, untouched); no nested ternaries.
- Full `Category=Unit` (net10.0): **5126 passed, 35 skipped, 0 failed**. `Category=Integration&(Module=McpServer|Module=Common)`: **5 passed, 0 failed**. MCP-host DI-graph gate tests (`BindingsModuleMcpHostGateTests`, `Program.Tests`) pass — both host graphs pass ValidateOnBuild with the new ctor deps/registration.
- **MCP surface + docs: reviewed, no update required** — no tool/verb/flag/argument/description change; the change is internal locking + capture-buffer scoping only.

**Follow-ups (out of scope):** `GetTenantKey` re-runs `settings.Fill` (deterministic, no prompt in MCP) so the key is computed twice per `InternalExecute` invocation; a future optimization could thread the key from `Resolve`. The `Thread.Sleep(500)` in `CompileCreatioTool`/`AddItemModelTool` is now unnecessary for capture (enqueue-time is synchronous) but was left untouched to keep this change scoped.

### Pre-commit review fixes (2026-07-09) — DONE

Applied the verified adversarial-review findings on top of the Story 9 core (per-flow capture isolation + per-tenant lock already in the working tree). The per-tenant lock ENABLED concurrent different-tenant execution, which exposed process-global-state races the old single global lock hid.

**FIX 1 (HIGH, H1+H2) — process-global `Environment.CurrentDirectory` cross-tenant race.**
- **Full cwd-toucher inventory** (grepped `clio/Command/McpServer/**` + every helper they call for `CurrentDirectory` / `Get`/`SetCurrentDirectory` / `FindWorkspaceRoot` / `WorkingDirectoriesProvider` / `ResolveAnchor`). MCP-reachable, concurrent set:
  - **Writers** (`SetCurrentDirectory`): `WorkspaceSyncTool`/`WorkspaceCommandToolBase.ExecuteInWorkspace` (push/restore-workspace), `CreateUiProjectTool.CreateUiProject`, `DownloadConfigurationTool.ExecuteInWorkspace` (was on its OWN private `WorkspaceExecutionLock` — the H2 miss).
  - **Readers** (`GetCurrentDirectory` → `PageOutputDirectoryResolver.ResolveAnchor`/`FindWorkspaceRoot`): `PageSyncTool.WriteVerifiedBodyFile` (verify read-back anchor), `PageBaselineGuard.TryArm` (sync-pages/update-page baseline anchor, via `PageBaselineStore.ResolveMetaFilePath`), `PageFileWriter.WritePageFiles` (get-page anchor). Confirmed these are the full concurrent set (`ModelBuilder`/`Workspace*`/`Package*`/`ProcessExecutor` cwd reads run INSIDE a command already under the per-tenant lock and are not additional MCP concurrency surfaces beyond the anchor readers).
- **One dedicated global lock** `McpToolExecutionLock.CwdLock` (single `static readonly object`, ordering documented on it) — NOT the per-tenant lock. Every cwd reader AND writer acquires it around the cwd-sensitive region. `DownloadConfigurationTool`'s private `WorkspaceExecutionLock` was removed and routed onto `CwdLock` (fixes H2).
- **Deadlock ordering — single global order: per-tenant → CwdLock, NEVER cwd → tenant.** Writers acquire their per-tenant lock FIRST via `ExecuteUnderTenantLock(options, …)` using the SAME key the inner `InternalExecute<TCommand>` resolves under (so the inner acquire is a reentrant no-op — no new cwd→tenant edge), THEN take `CwdLock` around pin/execute/restore. `WorkspaceCommandToolBase.ExecuteInWorkspace` and `DownloadConfigurationTool.ExecuteInWorkspace` now take the command `options` so the outer key matches the inner (previously `ExecuteInWorkspace` passed `null` → shared-fallback outer + env inner = mismatch, which under `CwdLock` would have been a cwd→tenant edge). Readers take `CwdLock` while already holding their per-tenant lock (PageSync batch / `InternalExecute`). CLI callers of `PageBaselineGuard`/`PageFileWriter` hold no per-tenant lock and are single-threaded, so `CwdLock` is uncontended there and creates no cwd→tenant edge (per-tenant locks are MCP-only).
- **Fail-first test** `clio.tests/Command/McpServer/CwdConcurrencyIsolationTests.cs` (`[Category("Unit")]`, `[NonParallelizable]`, `MockFileSystem` — no process-cwd mutation, no I/O). Deterministic observation of cross-placement is structurally impossible once the fix exists (the fix IS mutual exclusion — forcing the overlap would deadlock), so per the permitted fallback the tests assert the real reader paths acquire the SAME `CwdLock`: a reader BLOCKS while the test holds `CwdLock` and completes once released. **Verified fail-first**: with the `CwdLock` temporarily removed from `PageBaselineGuard.TryArm` and `PageFileWriter.WritePageFiles`, both tests FAIL — `Expected completedWhileHeld to be False … but found True` (the reader proceeded without serializing, reproducing the race). With the fix restored, both pass.

**FIX 2 (HIGH, was M1) — `TenantExecutionLockProvider._locks` unbounded growth.** Replaced the never-evicted `ConcurrentDictionary<string,object>` with a bounded map mirroring `SessionContainerCache` (idle-TTL `SessionContainerCacheDefaults.IdleTtl` + LRU-over-capacity `MaxSessions`, `Func<DateTime>` clock seam). A key whose lock is currently held is NEVER evicted — in-use count driven by new `ITenantExecutionLockProvider.MarkInUse`/`MarkAvailable`, which the `McpToolExecutionLock` facade now calls alongside the session-cache marks (for ALL keys incl. fallback). Evicting a held mapping and later minting a new object for the same key would break mutual exclusion; the sub-µs `GetLock`→`MarkInUse` window is covered by the fresh last-access set in `GetLock` (idle-TTL can't fire on a just-touched entry; LRU never picks the newest). The provider still serves keys with NO session-container entry (fallback / `unresolved:` / env-less) — it tracks its own holder count independently of the cache. Tests: `GetLock_ShouldEvictLruUnheldMapping_WhenOverCapacity`, `GetLock_ShouldNotEvictInUseMapping_WhenOverCapacity`, `GetLock_ShouldEvictIdleUnheldButKeepIdleHeld_WhenIdlePastTtl` (deterministic clock/capacity seam).

**FIX 3 (Medium, security — FR-06 completion) — scoped-file-sink broadcast not flow-isolated.** `ConsoleLogger.WriteToAdditionalSinks` previously wrote every drained message to ALL registered `_scopedFileSinks`, so a concurrent tenant's lines bled into another flow's `restore-db`/`deploy-creatio` artifact. Fixed at ENQUEUE time (not drain — the drain runs on the shared `_printThread` whose AsyncLocal slot differs): scoped sinks are now per async-flow (`AsyncLocal<List<SharedAppendFileSinkLease>>`), each enqueued `LogMessage` captures its producing flow's active sinks onto `LogMessage.ScopedSinks` (internal, `[JsonIgnore]`-equivalent — never serialized to MCP), and the drain writes each message ONLY to its own captured sinks (`_drainingScopedSinks`, set under `_messageBufferLock`). `BeginScopedFileSink` registers on the current flow; the process-wide `_scopedFileSinks` dict + `_scopedSinksLock` were removed. Test: `BeginScopedFileSink_ShouldReceiveOnlyOwnFlowLines_WhenTwoFlowsEmitConcurrently` (two flows, distinct sink files, barrier-forced interleave → each artifact contains only its own flow's lines).

**FIX 4 (elevated to MUST — FR-05 correctness invariant, L3) — key-equivalence tests.** `clio.tests/Command/McpServer/TenantKeyEquivalenceTests.cs`: a key-capturing `ISessionContainerCache` records the key `Acquire` is called with; the test asserts `IToolCommandResolver.GetTenantKey(options)` equals that key for BOTH the passthrough path (`BuildPassthroughCacheKey`, key starts `passthrough:`) AND the legacy/registry path (`BuildCacheKey`). If the two derivations drift, the per-tenant lock keys off a different identity than the shared session it guards. The BaseTool-path in-use survival (marked entry survives an over-capacity `Acquire`) is already covered for the session cache by `SessionContainerCacheTests` (`…NotEvictInUseEntry…`) and, newly, for the lock provider by FIX 2's `GetLock_ShouldNotEvictInUseMapping_WhenOverCapacity`.

**FIX 5 (Medium M2) — compute the tenant key ONCE.** The execution path no longer recomputes the key via `GetTenantKey` (a second `settings.Fill`). `ToolCommandResolver.Resolve` now records the key it cached under in a flow-local `LastResolvedTenantKey` (new interface member); `BaseTool.ResolveFromCallContainer` reads THAT immediately after resolving (before any checker resolve overwrites it) and threads it into the lock + `MarkInUse`/`MarkAvailable` via the new private `ExecuteLocked(command, options, tenantKey)` core. Chosen over an `out`-param overload because ~159 unit tests mock the existing `Resolve<T>(options)`; the ambient value keeps that signature (mocks yield null → normalized to the shared fallback, harmless in single-threaded unit tests). `GetTenantKey` is retained for the direct-lock sites (`PageSyncTool`, `PageEditToolHelpers`, `SchemaSyncTool`, `CompileCreatioTool`, `AddItemModelTool`) and the injected/direct `InternalExecute(command, options)` entry.

**FIX 6 (Medium M3) — `ExecuteWithCleanLog` establishes a fresh buffer.** Now sets `logger.PreserveMessages = true` (save/restore in try/finally) to establish a FRESH per-flow capture buffer, matching `InternalExecute` and the docstring's isolation promise, instead of only `ClearMessages()` on exit.

**Advisory — NOT fixed this batch (noted per instruction):**
- **L1** — the in-flight guard (`MarkInUse`) is not atomic with `Acquire`; safe by recency (the just-Acquired/GetLock'd entry has a fresh last-access, so neither idle-TTL nor LRU picks it in the sub-µs window before `MarkInUse`).
- **L2** — a few readers use `logger.LogMessages` directly (`AddItemModelTool`, `CompileCreatioTool`) rather than `FlushAndSnapshotMessages`; prefer the latter for a drained+snapshotted read. Functionally fine on the same synchronous flow.
- **M4** — `Program.cs:~1200` process-wide `PreserveMessages = true` is now vestigial (each tool scope establishes its own fresh per-flow buffer); candidate for removal in a follow-up.

**Gates (this batch).** Build clean; no new `CLIO*`/S3358 in modified files (only the 2 pre-existing CLIO005 on `CreateEntity`/`CreatePageBusinessRuleCommand`, untouched). Full `Category=Unit` (net10.0): **5134 passed, 35 skipped, 0 failed**. `Category=Integration&(Module=McpServer|Module=Common)`: **5 passed, 0 failed**. Host-graph gates (`BindingsModuleMcpHostGateTests`, `ProgramTestCase`): 12 passed — both host graphs pass ValidateOnBuild. **MCP surface + docs: reviewed, no update required** — internal locking / capture-scoping only, no tool/verb/flag/argument/description change.
