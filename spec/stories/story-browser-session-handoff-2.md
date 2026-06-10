# Story 2: ICreatioAuthClient — Forms-Auth (IsNetCore-aware), dedicated HttpClient

**Feature**: browser-session-handoff
**FR coverage**: FR-07, FR-08, FR-10, FR-14
**PRD**: [prd-browser-session-handoff.md](../prd/prd-browser-session-handoff.md)
**ADR**: [adr-browser-session-handoff.md](../adr/adr-browser-session-handoff.md) (Decisions 1, 2, 7)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: Story 11 (OQ-01) and Story 12 (OQ-06) — **both RESOLVED 2026-06-10**. Outcome: **no OAuth token→cookie branch** (forms-auth only; OAuth-only fails closed), and **cookie harvesting via a dedicated `IHttpClientFactory` client** (the NuGet cookie store is internal/unreachable)
**Revised**: 2026-06-10 — corrects BL-1 (`0/` inverted), BL-4 (cookie harvesting); spikes 11/12 closed → OAuth branch removed, dedicated `HttpClient` final

---

## As a

developer

## I want

an `ICreatioAuthClient` that authenticates against Creatio and returns harvested cookies as a `StorageStateResult`

## So that

the auth logic is encapsulated, testable, host-aware (NetFW/NetCore), and never leaks cookie values into logs or exceptions

---

## Background (corrections from review)

- **`0/` prefix was inverted.** The login URL must be `IsNetCore`-aware: NetFW (`IsNetCore == false`) **requires** the `0/` `WebAppAlias` prefix; NetCore does not. Verified via `ServiceUrlBuilder.cs:215-221`, `SimpleloginUri` (`ConfigurationOptions.cs:105`), and `EnvironmentManagerTests.cs:110-111` (asserts `…/0/ServiceModel/AuthService.svc/Login` for NetFW). The earlier "build without prefix / never use ServiceUrlBuilder" instruction was the bug.
- **Cookie harvesting (Story-12 spike RESOLVED — Decision B).** `IApplicationClient.ExecutePostRequest` returns only the body string (`IApplicationClient.cs:26`); the NuGet `CreatioClient` keeps its cookie store in an **`internal` `AuthCookie`** with no `InternalsVisibleTo` — unreachable from clio (reflection-confirmed). Therefore do **NOT** extend `IApplicationClient` and do **NOT** add `ICreatioCookieProvider`. `ICreatioAuthClient` uses its **own dedicated `HttpClient` + `CookieContainer` via `IHttpClientFactory`** (documented, scoped exception to the no-raw-HttpClient rule, modeled on the component-registry CDN client; mirrors the testkit reference).
- **No OAuth branch (Story-11 spike RESOLVED — NO-GO).** `OAuthTokenLogin` accepts only an external-access token, not clio's client-credentials token, on either host. So forms-auth (Login+Password) is the **sole** path; OAuth-only / incomplete-credential envs → **fail closed** with AC-ERR (FR-07). No `OAuthTokenLogin` call is implemented.

---

## Acceptance Criteria

- [ ] **AC-01** — Given a Creatio environment with `Login`/`Password`, when `LoginAsync()` is called, then the login request targets the **IsNetCore-aware** URL: `…/ServiceModel/AuthService.svc/Login` for NetCore and `…/0/ServiceModel/AuthService.svc/Login` for NetFW (built via `ServiceUrlBuilder`, not naive concat). A unit test asserts **both** URLs
- [ ] **AC-02** — Given a successful login, when `LoginAsync()` returns, then `StorageStateResult.Cookies` contains the cookies harvested from the dedicated `HttpClient`'s `CookieContainer` / `Set-Cookie` headers (Decision 7; NOT via `IApplicationClient`)
- [ ] **AC-03** — Given `Login`+`Password` present → forms-auth runs. Given **OAuth-only** (`Login`/`Password` absent, `ClientId`/`ClientSecret` present), `Login` without `Password`, or no credentials → **fail closed with AC-ERR** ("requires forms-auth credentials; environment is OAuth-only/incomplete"); **no request is attempted** and no `OAuthTokenLogin` call is made
- [ ] **AC-04** — Given a forms-auth HTTP 401 / non-success, when `LoginAsync()` runs, then it **fails with AC-ERR** (no fallback path exists to switch to)
- [ ] **AC-05** — Given any path, when `LoginAsync()` runs, then no cookie value (`.ASPXAUTH`, `BPMCSRF`, `UserType`) appears in any log output; cookie names may be logged with value `[REDACTED]`
- [ ] **AC-06** — Given an auth/HTTP failure, when `LoginAsync()` throws, then the thrown exception's `Message`/`ToString()` contain **no** URL query, headers, response body, password, or cookie material — safe to print under `--debug`
- [ ] **AC-07** — Given `StorageStateResult`, when serialised to Playwright storageState JSON, then it has the correct `cookies` array (`name`, `value`, `domain`, `path`, `httpOnly`, `secure`, `sameSite`, `expires`) and `origins` array
- [ ] **AC-ERR** — Given invalid credentials, the exception message is `authentication failed for environment '<env>' — check username and password in env config`. Given a network failure/timeout (not a 401), a distinct connectivity message is used

---

## Implementation Notes

**Files to create:**
- `clio/Common/BrowserSession/ICreatioAuthClient.cs`:
  ```csharp
  public interface ICreatioAuthClient
  {
      /// <summary>Authenticates and returns harvested cookies as a storageState structure.</summary>
      /// <remarks>Login URL is IsNetCore-aware (NetFW gets the "0/" WebAppAlias prefix; NetCore does not).
      /// Throws a SANITIZED exception on failure — no URL query, headers, body, or cookie material.</remarks>
      Task<StorageStateResult> LoginAsync(EnvironmentSettings env, CancellationToken ct = default);
  }
  ```
- `clio/Common/BrowserSession/CreatioAuthClient.cs` — forms-auth only, using a **named `HttpClient` from `IHttpClientFactory`** with a `CookieContainer`; harvests cookies from the container after the login POST; OAuth-only/incomplete creds → fail closed (no `OAuthTokenLogin` call); sanitized exceptions. Inject `IHttpClientFactory` (registered in `BindingsModule.cs`) — never `new HttpClient()`
- `clio/Common/BrowserSession/StorageStateResult.cs`:
  ```csharp
  public record StorageStateResult(IReadOnlyList<BrowserCookie> Cookies);
  public record BrowserCookie(string Name, string Value, string Domain, string Path,
      bool HttpOnly, bool Secure, string SameSite, double Expires);
  ```
  Internal to the service layer; never serialised into MCP response or CLI stdout.

**Files to modify (Decision 7 — cookie harvesting):**
- `clio/BindingsModule.cs` — register a **named `HttpClient` via `IHttpClientFactory`** for `ICreatioAuthClient` (modeled on the component-registry CDN client). **Do NOT modify `IApplicationClient`/`CreatioClientAdapter`** — the NuGet client exposes no cookie surface (see Background); the dedicated client harvests `Set-Cookie` itself. (If Story 12 proves a cookie surface exists, replace this with a segregated `ICreatioCookieProvider` instead.)

**Login URL (mandatory comment + IsNetCore-aware):**
```csharp
// AuthService.svc/Login is IsNetCore-aware: NetFW requires the "0/" WebAppAlias prefix, NetCore does not.
// Build via ServiceUrlBuilder (KnownRoute.AuthServiceLogin) so the prefix split stays centralized and test-covered.
var loginUrl = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.AuthServiceLogin, env);
```
(Add `AuthServiceLogin` to `ServiceUrlBuilder.KnownRoute` + `KnownRoutes` with the raw `ServiceModel/AuthService.svc/Login` path — `Build()` applies the `0/` for NetFW automatically.)

**Sanitized exception (AC-06):** catch `WebException`/`HttpRequestException`/parse errors inside `CreatioAuthClient`, rethrow a clio exception whose message names only the environment and failure class — never the URL/body/cookie. Unit-test that `ToString()` contains no secret.

**DI registration:** `ICreatioAuthClient` → `CreatioAuthClient` in `clio/BindingsModule.cs`.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Forms-auth URL = `…/ServiceModel/AuthService.svc/Login` for **NetCore** | `clio.tests/Common/BrowserSession/CreatioAuthClientTests.cs` |
| Unit `[Category("Unit")]` | Forms-auth URL = `…/0/ServiceModel/AuthService.svc/Login` for **NetFW** | `clio.tests/Common/BrowserSession/CreatioAuthClientTests.cs` |
| Unit `[Category("Unit")]` | Login+Password → forms-auth runs; forms-auth 401 → AC-ERR | `clio.tests/Common/BrowserSession/CreatioAuthClientTests.cs` |
| Unit `[Category("Unit")]` | OAuth-only (no Login/Password, ClientId/Secret present) → fail closed with AC-ERR, **no request attempted**, no `OAuthTokenLogin` call | `clio.tests/Common/BrowserSession/CreatioAuthClientTests.cs` |
| Unit `[Category("Unit")]` | `Login` without `Password`, and no-credentials → fail closed with AC-ERR naming the missing credential | `clio.tests/Common/BrowserSession/CreatioAuthClientTests.cs` |
| Unit `[Category("Unit")]` | Cookie values absent from captured logs **and** from thrown exception `ToString()` | `clio.tests/Common/BrowserSession/CreatioAuthClientTests.cs` |
| Unit `[Category("Unit")]` | `StorageStateResult` → valid Playwright JSON structure | `clio.tests/Common/BrowserSession/CreatioAuthClientTests.cs` |

Test naming: `LoginAsync_ShouldPrependZeroAlias_WhenNetFwEnvironment`, `LoginAsync_ShouldNotLeakCookieValues_WhenAuthFails`

## Definition of Done

- [x] Code compiles without `CLIO*` analyzer warnings
- [x] Login URL is IsNetCore-aware via `ServiceUrlBuilder` (new `KnownRoute.AuthServiceLogin`); tests assert both NetCore (no prefix) and NetFW (`0/`); no naive concat
- [x] Cookies harvested via a dedicated `IHttpClientFactory` client (`UseCookies=false`, `AllowAutoRedirect=false`); `IApplicationClient` **NOT** modified; the raw-HTTP exception is documented in `BindingsModule`; Story-12 outcome (Decision B) applied
- [x] Forms-auth is the sole path (FR-07); OAuth-only / incomplete-credential envs fail closed with AC-ERR before any request; **no `OAuthTokenLogin` call** (Story-11 NO-GO)
- [x] Cookie values never in logs (names only via `WriteDebug`); exceptions sanitized (safe under `--debug`) — both unit-tested
- [x] `ICreatioAuthClient`, the named `HttpClient`, and `IServiceUrlBuilderFactory` registered in `BindingsModule.cs`
- [x] Unit tests `[Category("Unit")]` — never `[Category("UnitTests")]`
- [x] **Smart regression**: full unit suite (BindingsModule + ServiceUrlBuilder touched) → 3504 passed, 0 new failures (3 pre-existing macOS path failures)
- [ ] PR description references this story file (no PR opened yet)

## Dev Agent Record

- Implementation started: 2026-06-10
- Implementation completed: 2026-06-10
- Tests passing: 10 `CreatioAuthClientTests` (both IsNetCore URLs, harvest, OAuth-only/incomplete fail-closed, invalid-creds, connectivity, no-cookie-value-in-logs, no-secret-in-exception, Playwright JSON); full unit suite 3504 passed / 0 new failures
- Files: `clio/Common/BrowserSession/{ICreatioAuthClient,CreatioAuthClient,StorageStateResult,StorageStateJson,CreatioAuthenticationException}.cs` (new); `clio/Common/ServiceUrlBuilder.cs` (`KnownRoute.AuthServiceLogin` = 42 + dict entry); `clio/BindingsModule.cs` (named auth `HttpClient`, `IServiceUrlBuilderFactory`, `ICreatioAuthClient`); `clio.tests/Common/BrowserSession/CreatioAuthClientTests.cs` (new).
- Notes:
  - **URL via `IServiceUrlBuilderFactory`**: `IServiceUrlBuilder` is not DI-registered (it is env-bound), so the auth client injects the stateless `IServiceUrlBuilderFactory` (newly registered) and calls `Create(env).Build(KnownRoute.AuthServiceLogin)` — keeps the `0/` split centralized in `ServiceUrlBuilder`, per-env, with no shared-instance mutation.
  - **Cookie harvest**: handler `UseCookies=false` keeps `Set-Cookie` headers readable; each is parsed manually (not `CookieContainer.SetCookies`, which mis-splits `Expires` dates on the comma) into a Playwright-shaped `BrowserCookie`. `AllowAutoRedirect=false` so a login-page 302 is treated as failure, not chased.
  - **Serialization**: `StorageStateJson.Serialize` (AC-07) produces `{cookies:[…camelCase…], origins:[]}`. Story 4 will call it when writing to the cache.
  - **MCP reviewed, no update required**: no MCP tool consumes the auth client yet (Stories 5/7 will); it is internal infrastructure.
  - **Docs reviewed, no update required**: no user-facing command/option added by this story.
