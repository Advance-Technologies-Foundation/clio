# Inventory 2 — state that survives between MCP calls

**Feature:** mcp-worker-execution-boundary · **Jira:** ENG-95262 · **Stage:** 0 (design artifact)
**Measured against:** `origin/master` @ `3fc50bf99`, 2026-08-17

Today one `clio mcp-server` process serves every call, so anything held in process memory is implicitly
shared and implicitly durable. Moving execution into short-lived children breaks both properties at once.
This inventory enumerates every piece of state that outlives a single `tools/call`, and states for each one
what the move does to it: **disappears** (the sharing was the bug), **must move to the parent** (the sharing
was load-bearing), or **becomes an interprocess hazard** (it was never process-local to begin with).

The third category is the dangerous one. Separate address spaces isolate memory; they do **not** isolate the
filesystem (rule 8). A file that was safe because one process touched it at a time stops being safe the
moment there are eight children.

## 1. Summary

| Verdict | Count | Meaning |
|---|---|---|
| Disappears | 5 | the state exists only to make in-process sharing survivable; with per-call children there is nothing to share |
| Moves to the parent | 4 | the sharing is the feature; the parent becomes its owner |
| Interprocess hazard | 4 | already on disk or in the OS; more processes make an existing race reachable |
| Already safe | 2 | designed cross-process from the start; no work |

## 2. Disappears — the sharing was the defect

| # | State | Where | Why it goes |
|---|---|---|---|
| D-1 | **Per-tenant execution monitor** — `ITenantExecutionLockProvider`, a `lock` object per tenant key | `McpToolExecutionLock.GetLock` (`Tools/McpToolExecutionLock.cs:157-159`), registered `BindingsModule.cs:734` | This *is* the wedge (mechanism 2). A per-call child runs exactly one call, so mutual exclusion within a process is vacuous. Deleted at Stage 10. |
| D-2 | **Session-container cache** — authenticated containers + `IApplicationClient` per cache key, with LRU eviction and in-flight pinning | `SessionContainerCache` (`BindingsModule.cs:729-730`) | A child logs in once and exits. The pinning (`MarkInUse` / `MarkAvailable`) exists only to stop eviction disposing a container mid-call; with one call per process there is nothing to evict. |
| D-3 | **Read-response deadline machinery** — abandoned `Task.Run` work and its gate | `McpReadResponseDeadline`, `McpReadDeadlineGate` | Bounds the *answer*, not the *work*, and the abandoned work keeps the monitor (mechanism 3). The parent's kill bounds the work itself. Deleted at Stage 10. |
| D-4 | **`CwdLock`** — a process-wide monitor serialising `Environment.CurrentDirectory` mutations | `McpToolExecutionLock.CwdLock`, taken by `OutputPathConfinement.cs:47`, `GetClassicPageSourcesCommand.cs:1390`, `PageBaselineGuard.cs:71`, `PageFileWriter.cs:76` | Current directory is per-process. A child has its own; nothing to serialise. **Caveat:** what it protects is a *path*, and paths are shared — see H-1. Removing the lock without the file gate trades a correct guard for a race. |
| D-5 | **`AsyncLocal` flow state** — log capture buffers, db-operation session, last-resolved tenant key | `ConsoleLogger.cs:34,51,52`, `DbOperationLogging.cs:65-66`, `ToolCommandResolver.cs:82` | Exists to keep concurrent in-process calls from reading each other's flow state. One call per process makes the flow the process. Harmless if left; simply stops doing work. |

**Why D-2 is not a regression.** Deleting the shared session costs one login per call — measured warm login
p50 **0.468 s**, inside the ~0.7 s total per-call overhead already accepted. In exchange, the refuted
platform hypothesis becomes irrelevant: Creatio does not serialize concurrent requests on one session
(8 concurrent heavy `SelectQuery` = 2.27 s shared vs 2.67 s on eight logins), so nothing was being bought by
sharing it.

## 3. Moves to the parent — the sharing is load-bearing

| # | State | Where | What the parent must own |
|---|---|---|---|
| P-1 | **Compile operation registry** — `BoundedOperationStore<CompileOperationRecord>` | `ICompileOperationRegistry`, singleton at `BindingsModule.cs:738` | `compile-creatio` writes it, `compile-status` reads it later. With a child per call the poll reaches a *different process* and finds nothing. Either the sticky worker serves both calls, or the parent owns the registry. |
| P-2 | **Restart operation registry** — `BoundedOperationStore<RestartOperationRecord>` | `IRestartOperationRegistry`, singleton at `BindingsModule.cs:742` | Same shape as P-1, for `restart-by-environment-name` / `restart-status`. |
| P-3 | **`configuration-build` reservation** — `_configurationBuildInFlight`, a `ConcurrentDictionary` with a 30-minute reclaim ceiling and monotonic ownership tokens | `McpToolExecutionLock.TryReserveConfigurationBuild` (`:215`), held by `CompileCreatioTool.cs:66` and `InstallProcessBuilderTool.cs:167` | This is the one piece of mutual exclusion that is genuinely *needed*: two concurrent configuration builds against one environment corrupt each other regardless of which process issued them. Stage 7 moves it to the parent, keyed by **normalised tenant + resource**. Its 30-minute ceiling and token-based ownership carry over unchanged — they were designed for exactly this "holder may never release" case. |
| P-4 | **Enabled-tool generation** — the feature-toggle-filtered set of `[McpServerToolType]` classes | `McpFeatureToggleFilter.RegisterEnabledPrimitives`, `appsettings.json` `features` | Resolved once at parent startup and passed to every child **frozen** (rule 11). A child that re-read `appsettings.json` could disagree with the parent mid-session — a tool present in `tools/list` but absent in the worker, or the reverse. Four toggles today: `deploy-identity`, `process-designer`, `mobile-page-converter`, `watch-compilation`. |

**The gap P-1/P-2 do not cover.** Only these two registries exist. `install-process-builder` and
`create-app-section` have no registry at all, and `restart-by-credentials` is deliberately unreportable —
so three of the four long-running modes have no terminal status a supervisor could reap on. This is why
rule 5 requires a **private completion signal** between worker and parent rather than "reap when the
operation registry says done". OQ-4 asks whether `create-app-section` should also gain a real registry.

## 4. Interprocess hazards — never process-local, now reachable

These were safe by accident: one process, and within it a monitor. Remove the monitor and add processes, and
the race is real. Stage 9 installs the gates.

| # | Resource | Path / owner | Hazard | Gate needed |
|---|---|---|---|---|
| H-1 | **`.clio-pages/{schema}/meta.json`** | `PageBaselineGuard.cs`, `PageFileWriter.cs`; tools `get-page`, `update-page`, `sync-pages` | Read-modify-write **with swallowed I/O failures**. Two children writing the same schema interleave and the loser's write is lost silently — no error surfaces. Today `CwdLock` (D-4) accidentally serialises this; removing D-4 first would open the race. | File lock on the schema directory, and the swallowed I/O failures surfaced |
| H-2 | **Browser-session cache** | shared under the clio home directory; tool `get-browser-session` | Concurrent children read and rewrite one cached session | File lock, or accept last-write-wins explicitly and document it |
| H-3 | **`appsettings.json`** — the environment catalog | `ISettingsRepository` | Children read it; `reg-web-app` writes it. A write during a concurrent read yields a torn or stale catalog. ENG-94529 already made the read path reload from disk rather than serve a startup snapshot, which makes children *correct* but also makes concurrent reads frequent. | Read-share on read; atomic replace on write |
| H-4 | **Telemetry queue / flush** | `ITelemetryService`, `ITelemetryFlushService` singletons (`BindingsModule.cs:566-574`) | Each child would start its own flush service and drain on exit — N processes posting where one did. Rule 11 forbids worker startup from running host bootstrap, which covers this: **workers do not flush telemetry; the parent does.** | Suppressed in worker mode, not gated |

**Already safe, no work:**

| # | Resource | Why |
|---|---|---|
| S-1 | **DbHub** | Designed cross-process: `.clio.lock` with `FileShare.None` |
| S-2 | **Curated-knowledge / component-registry cache** under `~/.clio/cache` | Read-mostly and content-addressed. Measured: child `initialize` cost is **identical with no `~/.clio/cache` present at all** (0.63–0.66 s), so children neither depend on it being warm nor race to fill it. OQ-3 keeps open whether a machine that actually runs the budgeted knowledge-bootstrap path behaves the same. |

## 5. Ordering constraint for the deletions

The deletions in §2 are not independent of the gates in §4, and doing them in the wrong order reintroduces a
defect while removing another:

1. **H-1's file gate must land before D-4's `CwdLock` is removed.** `CwdLock` is today the only thing
   serialising `.clio-pages` writes. Removing it first converts a correct guard into a silent data race —
   and H-1's failures are already swallowed, so the race would not even be visible.
2. **P-3 must move to the parent before per-call children reach `compile-creatio`.** Otherwise two children
   each hold their own private "reservation" and neither excludes the other.
3. **D-1 and D-3 are removed last (Stage 10)** — they are the fallback for every tool not yet on the worker
   path. Deleting them while cohorts remain in-process removes the only bound those cohorts have.

## 6. What this inventory does not cover

Per-*client* isolation for `mcp-http` — which sticky worker a given authenticated caller may reach — is
credential state, not execution state, and is covered by the credential threat model
(`mcp-worker-execution-boundary-credential-threat-model.md`). The two meet at one place: a sticky worker's
scope key is `authenticated session/principal + normalised target + credential fingerprint`, and dropping
any of the three is a cross-client boundary violation rather than a performance choice.
