# Story 9: open-web-app --authenticated — CDP Cookie Injection (DEFERRED)

**Feature**: browser-session-handoff
**FR coverage**: FR-06 (Could)
**PRD**: [prd-browser-session-handoff.md](../prd/prd-browser-session-handoff.md)
**ADR**: [adr-browser-session-handoff.md](../adr/adr-browser-session-handoff.md) (Decision 6 — deferred)
**Status**: deferred
**Size**: L (full day) + spike
**Revised**: 2026-06-10 — deferred per review H-E

---

> **DEFERRED — do not implement in this iteration.** Mode A introduces a browser-automation
> subsystem clio does not currently have (Chromium discovery across 3 OSes + CDP transport).
> It is **not required** for the agent use case — Mode B (`get-browser-session` → storageState
> file path, Stories 1–8) fully satisfies it. Moved to a follow-up feature behind a **CDP spike**.
>
> **Prerequisite spike (before this story is implementable):** resolve the CDP mechanics that the
> first draft got wrong — `Network.setCookies` is a **WebSocket** command on a target session, not
> an HTTP `Fetch`; `--remote-debugging-port=0` requires scraping the chosen port from Chromium's
> `DevToolsActivePort` file/stderr; bind DevTools to **loopback only** (the endpoint is
> unauthenticated — a local-process token-exfil window); on macOS the `open` indirection yields no
> CDP handle, so a Chromium binary must be located and launched directly. The AC below are the
> intended target spec once the spike resolves; they are **not** ready-for-dev as written.

The acceptance criteria and notes below are retained as the target spec for the follow-up feature.

---

## As a

developer (CLI user)

## I want

to run `clio open-web-app --authenticated -e <env>` and have Chromium launch with Creatio session cookies already injected

## So that

I land on the Creatio home page without seeing the login form

---

## Acceptance Criteria

- [ ] **AC-01** — Given a registered environment with valid credentials, when `clio open-web-app --authenticated -e <env>` is called, then Chromium launches, session cookies are injected via CDP `Network.setCookies`, and the browser navigates to the Creatio home page without showing the login form
- [ ] **AC-02** — Given `--authenticated` is NOT set, when `open-web-app` is called, then existing behavior is unchanged (no CDP injection, no call to `IBrowserSessionService`)
- [ ] **AC-03** — Given the new `--authenticated` option, when inspected via Roslyn analyzer, then the option name is kebab-case and CLIO001 emits no warnings
- [ ] **AC-04** — Given no Chromium binary is found on the host, when `open-web-app --authenticated` is called, then clio prints `Error: Chromium binary not found — ensure a Chromium-based browser is installed` and exits non-zero (does not silently fall back to unauthenticated mode)
- [ ] **AC-05** — Given the `--authenticated` flow, when `IBrowserSessionService.GetSessionPathAsync()` throws an auth exception, then the error is printed and the command exits non-zero without launching Chromium
- [ ] **AC-ERR** — Given `--authenticated` is set but the env has no credentials, when the command runs, then clio prints `Error: authentication failed for environment '<env>' — check username and password in env config` and exits non-zero

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

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | `--authenticated` absent → `IBrowserSessionService` not called | `clio.tests/Command/OpenAppCommandTests.cs` |
| Unit `[Category("Unit")]` | `--authenticated` present → `GetSessionPathAsync` called before process launch | `clio.tests/Command/OpenAppCommandTests.cs` |
| Unit `[Category("Unit")]` | `GetSessionPathAsync` throws → error printed, process not launched | `clio.tests/Command/OpenAppCommandTests.cs` |
| Unit `[Category("Unit")]` | Chromium not found → error printed, exit non-zero | `clio.tests/Command/OpenAppCommandTests.cs` |

Test naming: `Execute_ShouldCallGetSessionPathAsync_WhenAuthenticatedFlagIsSet`, `Execute_ShouldNotLaunchChromium_WhenGetSessionThrows`

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004)
- [ ] `--authenticated` option is kebab-case; CLIO001 passes
- [ ] When `--authenticated` absent: zero behavior change from existing `open-web-app`
- [ ] Chromium not-found case fails with clear error, not silent fallback
- [ ] `IBrowserSessionService` injected via constructor (no `new`)
- [ ] DevTools HTTP call is commented to explain why it is not via `IApplicationClient`
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
