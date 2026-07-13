# Story 8: Cache-key discrimination + TTL/bounded eviction (with disposal/GC-safety decision)

**Feature**: mcp-http-credential-passthrough
**FR coverage**: FR-07, FR-08
**PRD**: [prd-mcp-http-credential-passthrough.md](../prd/prd-mcp-http-credential-passthrough.md)
**ADR**: [adr-mcp-http-credential-passthrough.md](../adr/adr-mcp-http-credential-passthrough.md) (step 8; FR-07/08)
**Jira**: [ENG-93208](https://creatio.atlassian.net/browse/ENG-93208)
**Status**: ready-for-dev
**Size**: L (full day)
**Depends on**: Story 1 (spike), Story 2 (spike), Story 3 (client seam), Story 7 (ephemeral resolution builds the cached providers)

---

## As a

developer preventing cross-tenant collisions and unbounded memory growth

## I want

`BuildCacheKey` to include the passed token/cookie, and the process-wide static container dictionary replaced by a bounded, evictable `ISessionContainerCache` with idle-TTL + LRU cap

## So that

two tenants on the same URL with different tokens never share an authenticated container, and a many-tenant edge stays memory-bounded

---

## Acceptance Criteria

- [ ] **AC-01 (FR-07)** — Given two requests to the **same** URL with empty login/password but **distinct** bearer tokens, when cache keys are built, then the keys **differ** (the hashed credential string now includes `AccessToken`/`AccessTokenType`/`Cookie`), so each resolves to a **distinct** container — no collision (maps FR-07; AC-04).
- [ ] **AC-02** — Given the cache key, when built, then the credential string is SHA-256 hashed before use so the discriminator remains secret-free (maps FR-07/FR-11).
- [ ] **AC-03 (FR-08 idle-TTL)** — Given `--session-idle-ttl` (default 5 min), when a container is idle past the TTL, then it is evicted (maps FR-08; AC-07).
- [ ] **AC-04 (FR-08 capacity)** — Given `--max-sessions` (default 50) is exceeded, when a new session is added, then the LRU container (oldest `lastAccessUtc`) is evicted (maps FR-08; AC-07).
- [ ] **AC-05 (disposal decision)** — Given a container is evicted, when disposal runs, then the child `IServiceProvider` is `Dispose()`d **and** the transport-lifecycle decision is applied: **either** (a) a documented proof that GC-only cleanup is safe under bounded churn (no leaked `HttpClient`/socket per evicted tenant — `CreatioClient` is not `IDisposable`, neither is `CreatioClientAdapter`/`IApplicationClient`), **or** (b) a real transport lifecycle (`CreatioClientAdapter` made `IDisposable` + a disposable handle on the underlying client/`HttpClient`). "Dispose the provider" alone is **not** sufficient and must not be claimed as such (maps FR-08).
- [ ] **AC-06 (in-flight guard)** — Given a container with an in-flight call, when eviction is considered, then it is **never** evicted mid-call (ref-count / in-use marker; eviction runs outside the per-tenant lock) (maps FR-08).
- [ ] **AC-ERR** — Given a cache/eviction failure, when it occurs, then it surfaces a clean error and no secret from the key material is exposed (maps FR-11).

## Implementation Notes

From ADR step 8 (FR-07/08) + the "Verified caveat" in the FR-08 design section:

- `ToolCommandResolver.BuildCacheKey` — extend the hashed credential string to include `settings.AccessToken`, `settings.AccessTokenType`, `settings.Cookie` (was `Login|Password|ClientId|IsNetCore`). Same SHA-256-before-use. This key also feeds `ITenantExecutionLockProvider` (Story 9).
- Replace the `static ConcurrentDictionary<string, IServiceProvider>` with singleton `ISessionContainerCache { IServiceProvider GetOrAdd(string cacheKey, Func<IServiceProvider> factory); }` holding `{ provider, lastAccessUtc }`.
- Eviction: idle-TTL (`--session-idle-ttl`, default 5 min) + capacity cap (`--max-sessions`, default 50, evict LRU).
- **Disposal cascade — resolve the decision explicitly (AC-05):** ADR verified via reflection that `Creatio.Client.CreatioClient` does NOT implement `IDisposable` (only `ICreatioClient`), and neither does `IApplicationClient`/`CreatioClientAdapter`. Provider disposal alone releases nothing at transport level. Do (a) prove+document GC-safety under bounded churn, or (b) add a real lifecycle. Never evict in-flight — guard with ref-count/in-use marker (eviction runs outside the per-tenant lock).

Key files: `clio/Command/McpServer/ToolCommandResolver.cs` (`BuildCacheKey`, `ContainerCache`), new `clio/Command/McpServer/SessionContainerCache.cs` (+ interface).
Pattern to follow: existing `BuildCacheKey` hashing; DI singleton registration.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | distinct keys for same-url/different-token; secret-free hashed key; idle-TTL eviction; LRU capacity eviction; in-flight not evicted; provider disposed on eviction | `clio.tests/Command/McpServer/SessionContainerCacheTests.cs`, `clio.tests/Command/McpServer/ToolCommandResolverCacheKeyTests.cs` |

Test naming `MethodName_ShouldBehavior_WhenCondition`; AAA + `because` + `[Description]`; NSubstitute; deterministic clock/TTL injection (no `Thread.Sleep`).
Targeted run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer"`

## Definition of Done

- [ ] Code compiles with no new `CLIO*` warnings in modified files (CLIO001–CLIO005 clean)
- [ ] `--session-idle-ttl`, `--max-sessions` kebab-case (CLIO001)
- [ ] `ISessionContainerCache` registered singleton in `BindingsModule` — no MediatR; no raw `HttpClient`
- [ ] Disposal/GC-safety decision (AC-05) is proven+documented (a) or a real lifecycle added (b); NOT claimed sufficient without one
- [ ] In-flight eviction guard present
- [ ] Cache key hashed / secret-free (FR-11)
- [ ] MCP surface + docs reviewed (FR-15) — flag docs in Story 14; state outcome
- [ ] Unit tests `[Category("Unit")]`; AAA + `because` + `[Description]`
- [ ] Targeted `dotnet test --filter "Category=Unit&Module=McpServer"` green before commit
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-07-09
- Implementation completed: 2026-07-09
- Tests passing: yes — full unit suite `dotnet test --filter "Category=Unit" -f net10.0` → 5120 passed, 0 failed, 35 skipped. Targeted `Category=Unit&Module=McpServer` → 1851 passed, 0 failed, 1 skipped.

### FR-07 — cache-key token discrimination
- `ToolCommandResolver.BuildCacheKey` now hashes `Login|Password|ClientId|AccessToken|AccessTokenType|Cookie|IsNetCore` (was `Login|Password|ClientId|IsNetCore`), so same-URL/different-token requests resolve to distinct containers (AC-01).
- Factored a single `HashSecretMaterial` helper shared by `BuildCacheKey` and `BuildPassthroughCacheKey`; both SHA-256 hash the credential material before it enters the key (AC-02, FR-11).
- **Deviation:** the legacy key no longer truncates the hash to 16 hex chars — it uses the FULL hash, matching `BuildPassthroughCacheKey`. Rationale: on this feature "same url, different token" is the norm, so a 64-bit prefix collision would be a cross-tenant credential crossover. No persisted format depends on the key.
- `BuildCacheKey` was made `internal static` (was `private`) for unit testing via existing `InternalsVisibleTo("clio.tests")`.

### FR-08 — ISessionContainerCache (replaced the static dictionary)
- New `clio/Command/McpServer/SessionContainerCache.cs`: `ISessionContainerCache` + `SessionContainerCache` + `SessionContainerCacheDefaults`. The static `ConcurrentDictionary` on `ToolCommandResolver` was removed; the resolver now injects `ISessionContainerCache` (6th ctor param) and calls `Acquire` in BOTH the legacy and passthrough paths.
- API: `Acquire(key, factory)` (get-or-create + bump lastAccessUtc + opportunistic eviction sweep), `MarkInUse(key)` / `MarkAvailable(key)` (in-flight guard). Deterministic `Func<DateTime>` clock seam injected for tests — no `Thread.Sleep`/wall-clock.
- Idle-TTL eviction (default 5 min) + LRU capacity eviction (default 50, oldest `lastAccessUtc`). Eviction skips any entry with `inUseCount > 0` AND the just-added key; if all others are in-use, a temporary overshoot is allowed rather than evicting an in-use/just-requested container (AC-06).

### AC-05 — disposal decision: OPTION (a), GC-safety proven
- On eviction the child `IServiceProvider` is disposed; that is sufficient. **Evidence (decompiled `Creatio.Client` 1.0.38 netstandard2.0 via `ilspycmd`):** `class CreatioClient : ICreatioClient` — it does NOT implement `IDisposable`. Its only fields are `string`/`bool`, a `CookieContainer`, an `ICredentials` and a `RetryPolicy` — no long-lived per-instance `HttpClient`/`HttpClientHandler`/`ClientWebSocket`/socket. Every HTTP call creates and disposes its own `HttpClient(handler)` inside a `using` block (per-request; no shared static, no `IHttpClientFactory` pool). The only socket-bearing type, `WsListenerNetFramework : IWsListener, IDisposable` (owns a `ClientWebSocket` + 8 MB buffer), is a separate object created only inside `StartListening`; it is never a field of `CreatioClient` and is not touched by the request/response command path the cached containers use. `CreatioClientAdapter`/`IApplicationClient` are likewise not `IDisposable` and wrap nothing long-lived. Therefore an evicted container leaks no transport resource: provider dispose releases incidental IDisposables, and the adapter + client are then plain GC-collectable managed state. Option (b) (custom transport lifecycle) is NOT required. This is documented in the `SessionContainerCache` XML `<remarks>`.

### AC-06 — in-flight guard scope: cache-level now, execution-boundary Release DEFERRED to Story 9
- Per the coordinator ruling: `MarkInUse`/`MarkAvailable` are implemented and unit-tested at the cache level; `Acquire` does NOT hold a lingering in-use ref (get-or-create + bump lastAccess only), so production eviction is fully functional now (entries are not permanently pinned in-use). Wiring `MarkInUse`/`MarkAvailable` into the BaseTool execution boundary is DEFERRED to Story 9, which refactors the execution lifecycle + removes the global lock. TODAY the global `McpToolExecutionLock` serializes all tool execution, so a container of another tenant cannot be evicted mid-call; the cache-level guard is proven by unit tests (`Acquire_ShouldNotEvictInUseEntry_*`).

### Options + cross-host DI
- Added `--session-idle-ttl` (string, default `5m`) and `--max-sessions` (int, default 50) to `McpHttpServerCommandOptions` (kebab-case, CLIO001 clean).
- `--session-idle-ttl` parsing (`SessionContainerCacheDefaults.ResolveIdleTtl`): accepts suffixed duration (`90s`/`5m`/`1h`/`1d`), bare seconds (`300`), or a `TimeSpan` string (`00:05:00`); null/blank/unparseable/non-positive → 5-minute default (never disables eviction).
- Default singleton registered in shared `BindingsModule.RegisterInto`; run-time-configured instance registered in `McpHttpServerCommand.Run` AFTER the shared build (last-registration-wins). `ISessionContainerCache` added to the `RegisterAssemblyInterfaceTypes` skip-list (impl ctor takes primitive TimeSpan/int → would break stdio ValidateOnBuild). Both host graphs validate on build (confirmed by the green suite incl. `CredentialPassthroughDiRegistrationTests`).

### MCP / docs
- MCP: `mcp-http` is a host launcher, not an MCP tool — no tool/prompt/resource wraps it. MCP reviewed, no update required.
- Docs: deferred to Story 14 per work order. No `ReadmeChecker`/doc test regressed (full suite green), so no `help/en/mcp-http.txt` stub was needed.

### Tests added
- `clio.tests/Command/McpServer/SessionContainerCacheTests.cs` — reuse/distinct providers, idle-TTL eviction + dispose, LRU eviction + dispose, in-use survival (capacity + idle), release-then-evict, ctor guards, `ResolveIdleTtl` parsing.
- `clio.tests/Command/McpServer/ToolCommandResolverCacheKeyTests.cs` — same-URL/distinct-token → distinct legacy AND passthrough keys; secret-free (hashed) keys.
- Updated `ToolCommandResolverTests.cs` / `ToolCommandResolverNoWriteTests.cs` for the new 6th ctor param.

- Notes: DID NOT commit or push (per work order).
