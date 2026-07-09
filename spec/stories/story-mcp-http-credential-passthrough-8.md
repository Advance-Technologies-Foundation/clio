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

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
