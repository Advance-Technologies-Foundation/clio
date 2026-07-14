# PRD: Browser Session Handoff

**Status**: Revised (post adversarial review 2026-06-10)
**Author**: PM Agent
**Created**: 2026-06-10
**Revised**: 2026-06-10 — incorporates [review-adr-browser-session-handoff-2026-06-10.md](../reviews/review-adr-browser-session-handoff-2026-06-10.md)
**Jira**: ENG-91234 (continuation of ENG-90846)

---

## Problem Statement

AI agents using the clio MCP server have no way to obtain an already-authenticated Creatio browser session. Every time an agent attempts to verify a UI change autonomously, Playwright or Chromium lands on the login page and the workflow stalls. Additionally, `EnvironmentSettings.Fill()` deadlocks the stdio MCP server when targeting Safe-flagged environments (it blocks on `Console.ReadKey()`), making even an existing workaround unusable in CI or agent pipelines.

## Goals

- [ ] Goal 1 — Enable AI agents to obtain a valid Creatio browser session programmatically
  SM-01: `get-browser-session` returns a usable storageState file path in under 5 s on a warmed environment / Counter: existing `open-web-app` command regression rate stays 0
- [ ] Goal 2 — Cache and reuse sessions to avoid unnecessary re-login
  SM-02: repeated `get-browser-session` calls within a valid session lifetime perform **0 POST requests to `AuthService.svc/Login`** (a lightweight validation GET to the environment root is permitted) / Counter: a cached session must not be returned for an invalidated or expired session (verified by integration test)
- [ ] Goal 3 — Fix the Safe-env MCP deadlock that blocks all MCP operations on protected environments
  SM-03: MCP stdio server completes a `get-browser-session` call against a Safe-flagged environment without hanging, returning a structured error / Counter: non-Safe environments must not regress in behavior

## Non-goals

- Will NOT provision or manage Creatio user accounts (no throwaway-user provisioning). Environments where neither forms-auth nor `OAuthTokenLogin` can authenticate are **explicitly unsupported** in this scope (see FR-07 fail-closed behavior).
- Will NOT implement a general-purpose Playwright wrapper inside clio.
- Will NOT expose raw cookie strings through any public surface (MCP response, log output, or CLI stdout).
- Will NOT add a GUI or interactive consent flow; all auth is non-interactive (service-account credentials or OAuth token based).
- Mode A (`open-web-app --authenticated`, in-process Chromium/CDP cookie injection) is **out of scope for this iteration** — it requires a browser-automation subsystem clio does not currently have and is deferred to a follow-up feature behind a CDP spike (see FR-06 and ADR Decision 6). Mode B (storageState file path) fully satisfies the agent use case.

## User Stories (high level)

| As a | I want | So that |
|------|--------|---------|
| AI agent (MCP client) | to call `get-browser-session` and receive a path to a Playwright-compatible storageState file | I can open Creatio in a browser context that is already authenticated |
| developer (CLI user) | to run `clio get-browser-session -e <env>` and get a storageState file path | I can pass the file to a Playwright script without implementing auth myself |
| AI agent (MCP client) | to call `clear-browser-session` when a session becomes stale | I can force a fresh login on the next `get-browser-session` call |
| CI pipeline author | to run any clio MCP command against a Safe-flagged environment | The command completes without deadlocking the stdio server |
| developer (CLI user) | *(deferred)* to run `clio open-web-app --authenticated -e <env>` | I can open a Chromium window that is already logged in (Mode A — follow-up feature) |

## Feature Requirements

| ID | Requirement | Priority |
|----|------------|---------|
| FR-01 | Implement `get-browser-session` CLI command (`Command<GetBrowserSessionOptions>`) that authenticates against Creatio, writes a Playwright-compatible storageState JSON to disk, and returns the absolute file path | Must |
| FR-02 | Implement `clear-browser-session` CLI command (`Command<ClearBrowserSessionOptions>`) that deletes the cached storageState for the target environment (idempotent — no error if none exists) | Must |
| FR-03 | Implement on-disk session cache keyed by a stable environment identifier (environment **URI** + a credential discriminator hash) — **not** an `EnvironmentSettings.Name` property, which does not exist; reuse cache on subsequent calls after validating the session | Must |
| FR-04 | Expose `get-browser-session` as an MCP tool in `clio/Command/McpServer/Tools/` with `ReadOnly = false`, `Destructive = false`, `Idempotent = false`; cookie values must never appear in the MCP JSON payload; `--output-path` is **not** exposed on the MCP surface (CLI-only — see FR-11a) | Must |
| FR-05 | Expose `clear-browser-session` as an MCP tool in `clio/Command/McpServer/Tools/` with `ReadOnly = false`, `Destructive = true`, `Idempotent = true` | Must |
| FR-06 | *(Deferred — Could)* Add `--authenticated` flag to the existing `open-web-app` command (Mode A: launch Chromium via CDP and inject session cookies). Deferred to a follow-up feature behind a CDP spike; not implemented in this iteration. Listed here for traceability only | Could |
| FR-07 | **Authentication strategy (forms-auth only).** The Story-11 spike resolved OQ-01: `OAuthTokenLogin` accepts only an external-access/portal token (requires `prop:ResourceId` + an `ExternalAccess` DB row), **not** clio's client-credentials token, on either host — so there is no OAuth token→cookie path. (a) both `Login`+`Password` present → **forms-auth POST** (see FR-08), the sole cookie-issuance path, NetFW and NetCore; (b) **OAuth-only, `Login` without `Password`, or no credentials → fail closed with AC-ERR** naming why (e.g. "browser session handoff requires forms-auth credentials; environment '<env>' is OAuth-only/incomplete"); never attempt a request or open a login page; (c) a forms-auth HTTP 401/non-success → fail with AC-ERR | Must |
| FR-08 | The login URL must be **`IsNetCore`-aware**: NetCore → `{env.Uri}/ServiceModel/AuthService.svc/Login`; NetFW (`IsNetCore == false`) → `{env.Uri}/0/ServiceModel/AuthService.svc/Login` (the `0/` `WebAppAlias` prefix is **required** on NetFW). Prefer resolving via `ServiceUrlBuilder` (which already applies this split) or the existing `EnvironmentSettings.AuthAppUri` rather than naive string concatenation. A unit test must assert the exact URL for both `IsNetCore` values | Must |
| FR-09 | Fix `EnvironmentSettings.Fill()` so it does not call `Console.ReadKey()` or `Environment.Exit()` in a non-interactive context (MCP stdio server or CI). The non-interactive path must **fail closed** (refuse the Safe-env operation) and surface a structured error via a dedicated exception type. The fix must cover **all four** current `Fill()` call sites (`ConfigurationOptions.cs:587` + `ToolCommandResolver.cs:59,62,88`), not just the CLI one | Must |
| FR-10 | Cookie values must be redacted in all clio **log output** (any sink: console, file, MCP execution-log messages) — including exception messages under `--debug` (the auth client must throw sanitized exceptions carrying no URL query, headers, body, or cookie material) | Must |
| FR-11 | Mode B delivery: `get-browser-session` writes storageState JSON to the cache file (or a user-specified `--output-path`) and returns the absolute file path; the JSON itself is NOT returned inline in MCP or CLI stdout | Must |
| FR-11a | `--output-path` (CLI-only) must be validated: reject `..` traversal, refuse to follow an existing symlink, resolve via `Path.GetFullPath`, and write with owner-only permissions regardless of destination | Must |
| FR-12 | The session storageState file (and its cache directory) must be created with **owner-only permissions** via a dedicated `IFileSecurityHardening` abstraction: Unix → `0600` file / `0700` dir (`File.SetUnixFileMode`); Windows → current-user-only ACL (`FileSecurity`, inheritance disabled). Windows owner-only ACL *writing* is net-new (the existing `FsPermissionAssertion` helper is read-only) — if it proves too costly this iteration, the Windows clause is downgraded to a **documented limitation** rather than asserting parity. Verified per-OS | Must |
| FR-13 | Session validation must distinguish a live session from an **expired session that returns HTTP 200 with login-page HTML** (Creatio's actual behavior — not a 401). Reuse `ReauthExecutor.IsSessionExpiredResponse` semantics rather than a status-code-only check | Must |
| FR-14 | Authentication harvests cookies via a **dedicated `HttpClient` + `CookieContainer` obtained through `IHttpClientFactory`** inside `ICreatioAuthClient` (mirrors the testkit reference; a documented, scoped exception to the no-raw-HttpClient rule). It must **NOT** extend `IApplicationClient`. The Story-12 spike (OQ-06) confirmed the NuGet `CreatioClient` keeps its cookie store in an `internal` `AuthCookie` (no `InternalsVisibleTo`) — unreachable from clio — so the dedicated client is final (no `ICreatioCookieProvider`) | Must |
| FR-15 | Unit tests `[Category("Unit")]` covering: session fetch (forms-auth, both `IsNetCore` URLs), OAuth-branch request shape (mock), cache hit, cache miss, cache invalidation on expired-session detection, cookie redaction in logs and exceptions, file permissions, Safe-env non-interactive fail-closed | Must |
| FR-16 | Integration test `[Category("Integration")]` against a local Creatio instance verifying end-to-end authentication and storageState file validity (flagged: requires a live Creatio on the runner — may be a manual gate if CI cannot provide one) | Should |
| FR-17 | MCP E2E test in `clio.mcp.e2e/` covering `get-browser-session`, `clear-browser-session`, and the Safe-env no-hang case (flagged: not in CI). Additionally, an **automated** CI guard (with a timeout assertion) for the Safe-env no-hang on the real stdio path | Must (CI guard) / Should (E2E) |
| FR-18 | Documentation: `help/en/get-browser-session.txt`, `help/en/clear-browser-session.txt`, `docs/commands/get-browser-session.md`, `docs/commands/clear-browser-session.md`, updated `Commands.md`, updated `docs/McpCapabilityMap.md`, updated `Wiki/WikiAnchors.txt` | Must |

## CLI Impact

| Change | Details | Breaking? |
|--------|---------|-----------|
| New verb | `get-browser-session` | No |
| New verb | `clear-browser-session` | No |
| New flag | `--output-path` on `get-browser-session` (CLI-only; full file path) | No |
| New flag | `--force-refresh` on `get-browser-session` | No |
| New flag *(deferred)* | `--authenticated` on `open-web-app` (Mode A — follow-up feature) | No |
| Bug fix | `EnvironmentSettings.Fill()` no longer blocks on `Console.ReadKey()` in non-interactive context; fails closed | No — interactive CLI behavior unchanged; fix only affects stdio/non-interactive paths |

All flags: **kebab-case only** (CLIO001 enforced).

## Acceptance Criteria

- [ ] AC-01: Given a registered environment with valid username and password, when `clio get-browser-session -e <env>` is called, then a storageState JSON file is written to disk and its absolute path is printed to stdout; exit code is 0
- [ ] AC-02: Given a cached storageState that is still valid, when `clio get-browser-session -e <env>` is called again, then **no POST to `AuthService.svc/Login` is made** (a lightweight validation GET to the environment root is permitted) and the cached file path is returned; exit code is 0
- [ ] AC-03: Given a cached storageState whose validation indicates an expired session (HTTP 401 **or** HTTP 200 login-page HTML), when `clio get-browser-session -e <env>` is called, then a new login is performed and a fresh storageState file is written
- [ ] AC-04: Given an existing cached session, when `clio clear-browser-session -e <env>` is called, then the cached file is deleted (idempotent) and subsequent `get-browser-session` calls perform a fresh login
- [ ] AC-06: Given any authentication path, when cookies are harvested, then cookie values (`.ASPXAUTH`, `BPMCSRF`, `UserType`) never appear in MCP JSON response fields, CLI stdout, log output, or exception messages (including under `--debug`)
- [ ] AC-07: Given a forms-auth environment, when `get-browser-session` is called, then authentication succeeds via POST to the `IsNetCore`-aware URL — `…/ServiceModel/AuthService.svc/Login` on NetCore and `…/0/ServiceModel/AuthService.svc/Login` on NetFW
- [ ] AC-08: Given an **OAuth-only** environment (no `Login`/`Password`), when `get-browser-session` is called, then it **fails closed** with a clear AC-ERR message stating that browser-session handoff requires forms-auth credentials — no request is attempted and no login page is opened. (OQ-01 spike resolved: no OAuth token→cookie path exists for clio's token on either host)
- [ ] AC-09: Given a Safe-flagged environment in an MCP stdio session, when any MCP tool is invoked, then `EnvironmentSettings.Fill()` does not block on `Console.ReadKey()` and the tool returns a structured error result instead of hanging or killing the process
- [ ] AC-10: Given the unit test suite, when `dotnet test --filter "Category=Unit"` is run, then all tests for session fetch (both `IsNetCore` URLs), cache hit/miss, cookie redaction, file permissions, and Safe-env fail-closed pass
- [ ] AC-11: Given the MCP capability map, when the PR is merged, then `docs/McpCapabilityMap.md` lists both `get-browser-session` and `clear-browser-session` tools with correct metadata
- [ ] AC-12: Given a written session file on Unix, when its mode is inspected, then it is `0600` (file) within a `0700` directory; on Windows the ACL grants access to the current user only
- [ ] AC-13: Given `--output-path` pointing at a path containing `..` or an existing symlink, when `get-browser-session` is called, then clio refuses to write and exits non-zero with a clear error
- [ ] AC-ERR: Given invalid/missing credentials, when `clio get-browser-session -e <env>` is called, then clio prints `Error: authentication failed for environment '<env>' — check username and password in env config` and exits non-zero. Given a **network failure/timeout** (not an auth rejection), clio prints a distinct message naming the connectivity problem and exits non-zero

## Assumptions Index

| # | Assumption | Risk if wrong |
|---|-----------|--------------|
| A-01 | `AuthService.svc/Login` accepts `{UserName, UserPassword}` POST body and returns `Set-Cookie` headers on all supported Creatio versions (NetFW and NetCore) | If some versions respond differently, forms-auth path fails; surface AC-ERR clearly |
| A-02 | **RESOLVED (Story-11 spike):** `OAuthTokenLogin` exists on both hosts but accepts only an external-access/portal token (requires `prop:ResourceId` + `ExternalAccess` row), not clio's client-credentials token — so it cannot exchange clio's token for a cookie. There is no OAuth token→cookie path | OAuth-only environments are unsupported for browser-session handoff (FR-07(b) fail-closed); no throwaway-user fallback in scope |
| A-03 | Playwright storageState format (cookies array + origins array) is stable across Playwright versions used by agent consumers | Format drift would silently break session injection in consuming test kits |
| A-04 | The clio environment config stores `Login` and `Password` for forms-auth (already modeled on `EnvironmentSettings`); no interactive credential prompting is required | If credentials are not stored, FR-07(d) fail-closed applies |
| A-05 | The NuGet `CreatioClient` does **not** expose its cookie jar (strong evidence: `CreatioClientAdapter` holds no `CookieContainer`; `ReauthExecutor.cs:143-145` says the client exposes no HTTP status/ResponseUri). Therefore cookies are harvested by a dedicated `IHttpClientFactory` auth client (FR-14), not by reusing the existing session | If the Story-12 spike finds a cookie surface after all, switch to a segregated `ICreatioCookieProvider`; otherwise the dedicated client stands |
| A-06 | **Security gate — ACCEPTED 2026-06-10 (Alex):** forms-auth depending on plaintext `Password` in `appsettings.json` (already the status quo) and a harvested bearer cookie written owner-only to disk (no encryption at rest) are acceptable. No security concerns. Gate cleared | Resolved — stories may enter `in-progress` |

## Open Questions

| # | Question | Owner | Status |
|---|---------|-------|--------|
| OQ-01 | Is `OAuthTokenLogin` available on .NET Framework, and can clio's token use it? | Architect | **RESOLVED (Story 11, 2026-06-10):** endpoint exists on NetFW (`0/ServiceModel/AuthService.svc/OAuthTokenLogin`) but accepts only an external-access token, not clio's client-credentials token — NO-GO on both hosts. Forms-auth is the sole path; OAuth-only fails closed |
| OQ-02 | Session cache directory location and at-rest security | Architect + Security | **RESOLVED** (ADR Decision 3): `{AppSettingsFolderPath}/sessions/`, owner-only perms (FR-12), no encryption at rest, pending A-06 sign-off |
| OQ-03 | Should `--output-path` accept a directory or a full file path? | PM + Dev | **RESOLVED**: full file path (CLI-only, validated per FR-11a) |
| OQ-04 | Expected `.ASPXAUTH` session lifetime / configurable cache TTL | Architect | **RESOLVED** (ADR Decision 3): no fixed TTL; per-call validation via FR-13; a bounded max-age purges abandoned files |
| OQ-05 | Does the `Fill()` fix need a new interface or a startup flag? | Architect | **RESOLVED** (ADR Decision 4): `IInteractiveConsole` as a required `Fill()` param; check stays in `Fill()` (Safe is not propagated); fail-closed in MCP |
| OQ-06 | Does the NuGet `creatio.client` expose a cookie surface `ICreatioAuthClient` could reuse? | Architect | **RESOLVED (Story 12, 2026-06-10):** No — the cookie store is `internal CreatioClient.AuthCookie`, no `InternalsVisibleTo`. clio uses a dedicated `IHttpClientFactory` client (FR-14); no `ICreatioCookieProvider` |

## Dependencies

- Depends on: ENG-90846 (prior browser-session investigation); `IApplicationClient` / `CreatioClient` / `ReauthExecutor` HTTP infrastructure already present in clio
- Blocks: any agent workflow that requires autonomous UI verification in Creatio (e.g. Freedom UI regression checks via MCP)
- Security gate A-06 must be resolved before stories leave `ready-for-dev`
