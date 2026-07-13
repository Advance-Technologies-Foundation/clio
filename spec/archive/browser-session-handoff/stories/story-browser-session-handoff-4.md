# Story 4: IBrowserSessionService — Auth + Cache Orchestration

**Feature**: browser-session-handoff
**FR coverage**: FR-03, FR-07, FR-13
**PRD**: [prd-browser-session-handoff.md](../prd/prd-browser-session-handoff.md)
**ADR**: [adr-browser-session-handoff.md](../adr/adr-browser-session-handoff.md) (Session validation strategy)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: Stories 2 and 3
**Revised**: 2026-06-10 — corrects BL-3 (cache key via `BuildKey`), H-A (detect 200-login-page)

---

## As a

developer

## I want

an `IBrowserSessionService` that orchestrates `ICreatioAuthClient` and `IBrowserSessionCache` with a check-validate-login flow

## So that

callers get a valid storageState file path without knowing the details of caching or authentication

---

## Background (corrections from review)

- Cache access uses `IBrowserSessionCache.BuildKey(env)` (env.Uri + credential hash) — **not** `env.Name` (which does not exist).
- Validation must **not** key off HTTP status alone. Creatio returns **HTTP 200 with login-page HTML** on an expired session — reuse `ReauthExecutor.IsSessionExpiredResponse` semantics. A 401-only check would return a dead session as "valid."

---

## Acceptance Criteria

- [ ] **AC-01** — Given a valid cached session, when `GetSessionPathAsync()` is called without `forceRefresh`, then `ICreatioAuthClient.LoginAsync()` is **not** called and the cached path is returned (no POST to `AuthService.svc/Login`; a validation GET to env root is permitted)
- [ ] **AC-02** — Given no cached session, when `GetSessionPathAsync()` is called, then `LoginAsync()` is called once and the result is written to cache (via `BuildKey(env)`)
- [ ] **AC-03** — Given a cached session whose validation indicates expiry — **HTTP 401/403 OR HTTP 200 with login-page HTML** (`ReauthExecutor.IsSessionExpiredResponse`) — when `GetSessionPathAsync()` is called, then the stale file is deleted, `LoginAsync()` is called, and a fresh path is returned
- [ ] **AC-04** — Given a valid cached session, when called with `forceRefresh = true`, then `LoginAsync()` is called even though the cache file exists
- [ ] **AC-05** — Given a cached session, when `ClearSessionAsync()` is called, then `IBrowserSessionCache.Delete(BuildKey(env))` is invoked
- [ ] **AC-ERR** — Given `LoginAsync()` throws an auth exception, when `GetSessionPathAsync()` is called, then the (already sanitized) exception propagates with its user-friendly message

---

## Implementation Notes

**Files to create:**
- `clio/Common/BrowserSession/IBrowserSessionService.cs`:
  ```csharp
  public interface IBrowserSessionService
  {
      Task<string> GetSessionPathAsync(EnvironmentSettings env, string overrideOutputPath = null,
          bool forceRefresh = false, CancellationToken ct = default);
      Task ClearSessionAsync(EnvironmentSettings env, CancellationToken ct = default);
  }
  ```
- `clio/Common/BrowserSession/BrowserSessionService.cs` — flow:
  1. `key = cache.BuildKey(env)`.
  2. If `!forceRefresh` and `cache.TryRead(key, out path)` → **validate**: GET `{env.Uri}` with the cached cookies, then test the response with `ReauthExecutor.IsSessionExpiredResponse(body)` (NOT status-only). Not expired → return `path`. Expired → `cache.Delete(key)`, fall through.
  3. `result = authClient.LoginAsync(env, ct)` → `cache.Write(key, ToStorageStateJson(result), overrideOutputPath)` → return path.
  - Decide whether the validation probe must bypass the reauth wrapper so it measures the **cached** cookies, not clio's own session (document the choice).

**Serialization owner:** `ToStorageStateJson(StorageStateResult)` lives in the service (or a shared serializer); `IBrowserSessionCache.Write` takes already-serialized JSON. Resolve the Story 2/3 ambiguity here: the service owns serialization.

**DI registration:** `IBrowserSessionService` → `BrowserSessionService` in `clio/BindingsModule.cs`.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Cache hit + valid → `LoginAsync` not called | `clio.tests/Common/BrowserSession/BrowserSessionServiceTests.cs` |
| Unit `[Category("Unit")]` | Cache miss → `LoginAsync` called once, written via `BuildKey` | `clio.tests/Common/BrowserSession/BrowserSessionServiceTests.cs` |
| Unit `[Category("Unit")]` | Expired via **200-login-page** body → cache deleted, `LoginAsync` called | `clio.tests/Common/BrowserSession/BrowserSessionServiceTests.cs` |
| Unit `[Category("Unit")]` | Expired via **401** → cache deleted, `LoginAsync` called | `clio.tests/Common/BrowserSession/BrowserSessionServiceTests.cs` |
| Unit `[Category("Unit")]` | `forceRefresh = true` → `LoginAsync` called even on valid cache | `clio.tests/Common/BrowserSession/BrowserSessionServiceTests.cs` |
| Unit `[Category("Unit")]` | `ClearSessionAsync` → `Delete(BuildKey(env))` | `clio.tests/Common/BrowserSession/BrowserSessionServiceTests.cs` |

Test naming: `GetSessionPathAsync_ShouldReauthenticate_WhenCachedSessionReturnsLoginPage`

## Definition of Done

- [x] Code compiles without `CLIO*` analyzer warnings
- [x] Cache access uses `BuildKey(env)` — no `env.Name`
- [x] Validation detects the 200-login-page (`ReauthExecutor.IsSessionExpiredResponse`) AND 401/403, not status-only
- [x] Serialization owned by the service (`StorageStateJson.Serialize`); `StorageStateJson.ToCookieHeader` rebuilds the validation cookie header
- [x] `IBrowserSessionService` registered in `BindingsModule.cs`; all collaborators constructor-injected (no `new`)
- [x] Unit tests `[Category("Unit")]` — never `[Category("UnitTests")]`
- [x] **Smart regression**: full unit suite (BindingsModule touched) → 3511 passed, 0 new failures (3 pre-existing macOS)
- [ ] PR description references this story file (single PR at the end)

## Dev Agent Record

- Implementation started: 2026-06-10
- Implementation completed: 2026-06-10
- Tests passing: 7 `BrowserSessionServiceTests` (cache hit/valid, miss→login, expired-via-login-page, expired-via-401, force-refresh skips cache read, clear, login-throws-propagates); full unit suite 3511 passed / 0 new failures
- Files: `clio/Common/BrowserSession/{IBrowserSessionService,BrowserSessionService}.cs` (new); `clio/Common/BrowserSession/StorageStateJson.cs` (+`ToCookieHeader`); `clio/BindingsModule.cs` (1 registration); `clio.tests/Common/BrowserSession/BrowserSessionServiceTests.cs` (new).
- Notes:
  - **Validation**: reads the cached storageState, rebuilds a `Cookie` header (`StorageStateJson.ToCookieHeader`), GETs `env.Uri` via the dedicated `creatio-auth` client, and treats 401/403 OR `ReauthExecutor.IsSessionExpiredResponse(body)` (the 200-login-page case) as expired → delete + re-login.
  - **Override path**: with `--output-path`, the fresh session is written to the validated override and that absolute path is returned; the default cache path is not populated (one-off export).
  - MCP reviewed / docs reviewed: internal orchestration service, no MCP-tool or command-doc surface yet (Stories 5/7 add it).
