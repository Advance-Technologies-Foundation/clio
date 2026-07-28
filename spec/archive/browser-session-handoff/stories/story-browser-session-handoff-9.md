# Story 9: open-web-app --authenticated — CDP Cookie Injection (Mode A)

**Feature**: browser-session-handoff
**FR coverage**: FR-06 (Could)
**PRD**: [prd-browser-session-handoff.md](../prd/prd-browser-session-handoff.md)
**ADR**: [adr-browser-session-handoff.md](../adr/adr-browser-session-handoff.md) (Decision 6 — implemented)
**Status**: review
**Size**: L (full day) + spike
**Revised**: 2026-06-10 — **implemented**; CDP spike resolved + live-verified (supersedes the H-E deferral)

---

> **IMPLEMENTED 2026-06-10.** The prerequisite CDP spike was resolved by a live end-to-end demo
> against `ts1-core-dev04` and then ported to C#. All four spike risks are closed in source:
> `Network.setCookie` is driven as a **WebSocket** command on a page target (not an HTTP `Fetch`);
> `--remote-debugging-port=0` is resolved by reading line 1 of `<user-data-dir>/DevToolsActivePort`
> then the page target's `webSocketDebuggerUrl` from `http://127.0.0.1:{port}/json`; DevTools binds
> **loopback-only** (port 0, never widened); on macOS a concrete Chromium binary is located and
> launched directly (no `open` indirection). Transport is `System.Net.WebSockets.ClientWebSocket`
> with **no** Playwright/Puppeteer dependency.
>
> Implemented behind `IAuthenticatedBrowserLauncher` (process launch + CDP) and `IChromiumLocator`
> (discovery); `OpenAppCommand` gains the `--authenticated` flag. When the flag is absent,
> `open-web-app` is unchanged. The live DevTools-socket navigation is a **manual E2E** gate (needs a
> real browser + live Creatio); unit tests cover the command orchestration and discovery logic.

---

## As a

developer (CLI user)

## I want

to run `clio open-web-app --authenticated -e <env>` and have Chromium launch with Creatio session cookies already injected

## So that

I land on the Creatio home page without seeing the login form

---

## Acceptance Criteria

- [x] **AC-01** — Given a registered environment with valid credentials, when `clio open-web-app --authenticated -e <env>` is called, then Chromium launches, session cookies are injected via CDP `Network.setCookie`, and the browser navigates to the Creatio home page without showing the login form *(live-verified 2026-06-10 against `ts1-core-dev04`)*
- [x] **AC-02** — Given `--authenticated` is NOT set, when `open-web-app` is called, then existing behavior is unchanged (no CDP injection, no call to `IBrowserSessionService`) *(unit: `Execute_ShouldNotCallBrowserSession_WhenAuthenticatedFlagAbsent`)*
- [x] **AC-03** — Given the new `--authenticated` option, when inspected via Roslyn analyzer, then the option name is kebab-case and CLIO001 emits no warnings *(build clean, 0 CLIO warnings)*
- [x] **AC-04** — Given no Chromium binary is found on the host, when `open-web-app --authenticated` is called, then clio prints `Error: Chromium binary not found — ensure a Chromium-based browser is installed` and exits non-zero (does not silently fall back to unauthenticated mode) *(unit: `Execute_ShouldReturnError_WhenChromiumNotFound` + `ChromiumLocatorTests`)*
- [x] **AC-05** — Given the `--authenticated` flow, when `IBrowserSessionService.GetSessionPathAsync()` throws an auth exception, then the error is printed and the command exits non-zero without launching Chromium *(unit: `Execute_ShouldNotLaunchChromium_WhenGetSessionThrows`)*
- [x] **AC-ERR** — Given `--authenticated` is set but the env has no credentials, when the command runs, then clio prints `Error: authentication failed for environment '<env>' — check username and password in env config` and exits non-zero *(the canonical `CreatioAuthenticationException.InvalidCredentials` message, surfaced with an `Error: ` prefix)*

---

## Implementation Notes

**File to modify:**
- `clio/Command/OpenAppCommand.cs`:
  - Add `[Option("authenticated", Required = false, HelpText = "Inject session cookies via CDP before opening Chromium")]` to `OpenAppOptions`
  - Extend `Execute()`: when `options.Authenticated == true`:
    1. Call `IBrowserSessionService.GetSessionPathAsync(env)` — on failure: print error and return non-zero
    2. Launch Chromium with `--remote-debugging-port=0` via `IProcessExecutor`
    3. Poll DevTools JSON endpoint (`GET http://localhost:{port}/json/version`) until ready
    4. Send `Network.setCookies` CDP command with cookies read from the storageState file
    5. Send `Page.navigate` CDP command to `env.Uri`
  - If Chromium binary not found: print error and return non-zero (never fall back silently)
  - `IProcessExecutor` is already injected in `OpenAppCommand`; no new DI registration needed for process launch

**CDP injection uses `IApplicationClient` or `HttpClient` only for DevTools localhost calls** — the DevTools endpoint is `http://localhost:{port}/json` (not a Creatio endpoint), so it does not go through `IApplicationClient`. Document this exception with a comment.

**New DI wiring:** inject `IBrowserSessionService` into `OpenAppCommand` constructor. Update `BindingsModule.cs` if `OpenAppCommand` is already registered there; otherwise add it.

**Chromium discovery:** check `CHROME_PATH` env var, then standard OS paths (`/usr/bin/google-chrome`, `/Applications/Google Chrome.app/...`, `C:\Program Files\Google\Chrome\Application\chrome.exe`). Expose as a private helper method.

**Depends on:** Story 4 (`IBrowserSessionService` must exist)

**Implementation deviations from the draft notes (intentional, cleaner than inlining in the command):**
- The CDP + process-launch logic lives in a dedicated `IAuthenticatedBrowserLauncher`
  (`clio/Common/BrowserSession/AuthenticatedBrowserLauncher.cs`), and Chromium discovery in
  `IChromiumLocator` (`ChromiumLocator.cs`), rather than inline in `OpenAppCommand`. The command
  only orchestrates: `GetSessionPathAsync` → `LaunchAsync`, with `--authenticated` catch/return.
- Port discovery reads `<user-data-dir>/DevToolsActivePort` (line 1), then resolves the page
  target's `webSocketDebuggerUrl` from `http://127.0.0.1:{port}/json` (not `/json/version`, which is
  the browser target — page-level Network/Page domains need a page target).
- `Network.setCookie` (singular, once per cookie) over `ClientWebSocket` — matches the live demo.
- Navigation target is the **IsNetCore-aware Shell entry** (`{Uri}/0/Shell/` on NetFramework,
  `{Uri}/Shell/` on NetCore), **not** the bare `env.Uri`. Live testing showed the bare root renders
  the login form even with a valid injected `.ASPXAUTH`, whereas the Shell URL honours it and lands
  on the workspace. Mirrors `EnvironmentSettings.SimpleloginUri`'s split, minus `?simplelogin=true`.
- Both new services auto-register via the `Clio.*` interface convention in `BindingsModule`
  (`RegisterAssemblyInterfaceTypes`); no explicit DI line needed. `OpenAppCommand`'s constructor
  gains `IBrowserSessionService` + `IAuthenticatedBrowserLauncher` (both injected, no `new`).
- The local DevTools HTTP call uses `IHttpClientFactory` (not `IApplicationClient`, which is
  Creatio-only) with an inline comment explaining the exception.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | `--authenticated` absent → `IBrowserSessionService` not called | `clio.tests/Command/OpenAppCommandTests.cs` |
| Unit `[Category("Unit")]` | `--authenticated` present → `GetSessionPathAsync` called before process launch | `clio.tests/Command/OpenAppCommandTests.cs` |
| Unit `[Category("Unit")]` | `GetSessionPathAsync` throws → error printed, process not launched | `clio.tests/Command/OpenAppCommandTests.cs` |
| Unit `[Category("Unit")]` | Chromium not found → error printed, exit non-zero | `clio.tests/Command/OpenAppCommandTests.cs` |

Implemented test names — command (`OpenAppCommandTests`): `Execute_ShouldNotCallBrowserSession_WhenAuthenticatedFlagAbsent`,
`Execute_ShouldCallGetSessionPathAsync_WhenAuthenticatedFlagIsSet` (asserts ordering via `Received.InOrder`),
`Execute_ShouldNotLaunchChromium_WhenGetSessionThrows`, `Execute_ShouldReturnError_WhenChromiumNotFound`.
Discovery (`ChromiumLocatorTests`): `Locate_ShouldReturnChromePathEnvValue_WhenSetAndFileExists`,
`Locate_ShouldReturnStandardInstallPath_WhenChromePathUnsetButBrowserExists`,
`Locate_ShouldThrowChromiumNotFound_WhenNoBrowserExists`, `Locate_ShouldIgnoreChromePath_WhenItPointsAtMissingFile`.

## Definition of Done

- [x] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004)
- [x] `--authenticated` option is kebab-case; CLIO001 passes
- [x] When `--authenticated` absent: zero behavior change from existing `open-web-app` (existing 15 `OpenAppCommandTests` still green)
- [x] Chromium not-found case fails with clear error, not silent fallback
- [x] `IBrowserSessionService` injected via constructor (no `new`) — plus `IAuthenticatedBrowserLauncher`
- [x] DevTools HTTP call is commented to explain why it is not via `IApplicationClient`
- [x] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [x] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-06-10
- Implementation completed: 2026-06-10
- Tests passing: yes — `dotnet test --filter "Category=Unit&(Module=Command|Module=Common)"` → 1882 passed, 0 failed (incl. 4 new command + 4 new locator tests)
- Notes: CDP spike resolved by a live demo against `ts1-core-dev04` (Chrome launched → `Network.setCookie` harvested cookies incl. HttpOnly `.ASPXAUTH` → `Page.navigate` to `/0/Shell/`, no login form), then ported to C# behind `IAuthenticatedBrowserLauncher` + `IChromiumLocator`. Live DevTools-socket navigation (AC-01 end-to-end) remains a manual E2E gate — no real-browser test runs in CI. No MCP tool added for `open-web-app` (local GUI launch; not meaningful as a headless/remote MCP tool — the agent-facing surface is `get-browser-session`).
