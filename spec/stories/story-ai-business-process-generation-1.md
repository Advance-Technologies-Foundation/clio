# Story 1: Extract ICdpSession / CdpSession and Refactor AuthenticatedBrowserLauncher

**Feature**: ai-business-process-generation
**FR coverage**: FR-09 (CDP-plumbing-extraction portion), NFR-06, NFR-07
**PRD**: [prd-ai-business-process-generation.md](../prd/prd-ai-business-process-generation.md)
**ADR**: [adr-ai-business-process-generation.md](../adr/adr-ai-business-process-generation.md) (Decision A / OQ-03, "Boundary rules")
**Status**: ready-for-dev
**Size**: M (half day)

---

## As a

clio developer (foundation work for the Process Designer driver)

## I want

the CDP WebSocket plumbing currently private to `AuthenticatedBrowserLauncher` (frame-pump send/receive, page-target discovery, DevTools-port read) extracted into a shared `ICdpSession`/`CdpSession` helper, with the launcher refactored to consume it

## So that

the new `IProcessDesignerDriver` (Story 6) can run `Runtime.evaluate` over the same CDP page session without duplicating WebSocket plumbing, while the shipped `open-web-app --authenticated` flow keeps working unchanged

---

## Acceptance Criteria

- [ ] **AC-01** — Given a running browser on a loopback DevTools port, when `CdpSession.ConnectAsync(port)` then `SendAsync(method, params)` is called, then it returns the matching result frame and throws on a CDP error frame exactly as `AuthenticatedBrowserLauncher` did before extraction.
- [ ] **AC-02** — Given `CdpSession` is connected, when `EvaluateAsync(expression)` is called, then it issues `Runtime.evaluate` (with `awaitPromise` honored) and returns the awaited JSON result value.
- [ ] **AC-03** — Given the refactored launcher, when `open-web-app --authenticated` runs, then launch + cookie-inject + navigate behave identically (no contract or behavior change); the launcher's existing unit/E2E coverage passes unchanged.
- [ ] **AC-04** — Given the driver needs the chosen port, when the launcher launches the browser, then the launcher exposes the selected `DevToolsPort` (e.g. via a `LaunchResult { int DevToolsPort }` return or a `LaunchAndKeepOpenAsync`), without breaking the existing `--authenticated` callers.
- [ ] **AC-05** — Given `CdpSession` connects, when it opens the WebSocket, then it binds to `127.0.0.1` only (loopback; NFR-06) and never logs cookie values (NFR-07 — names only).
- [ ] **AC-ERR** — Given the DevTools port cannot be read or no page target is found, when `ConnectAsync` runs, then it throws the same exception type the launcher raised before extraction (no new failure mode introduced), and the launcher surfaces its existing user-friendly `Error:` message.

## Implementation Notes

Move the private members `CdpSendAsync` / `ReceiveTextAsync` / `FindPageTargetAsync` / `ReadDevToolsPortAsync` out of `AuthenticatedBrowserLauncher` into the new `CdpSession`. Keep the existing CDP error handling **verbatim** — this is a pure refactor with NO behavior change.

`ICdpSession` contract (from ADR "Key interfaces"):
```csharp
public interface ICdpSession : IAsyncDisposable {
    Task ConnectAsync(int devToolsPort, CancellationToken ct = default);
    Task<JsonElement> SendAsync(string method, object @params, CancellationToken ct = default);
    Task<JsonElement> EvaluateAsync(string expression, bool awaitPromise = true, CancellationToken ct = default);
}
```

Files to create:
- `clio/Common/BrowserSession/ICdpSession.cs`
- `clio/Common/BrowserSession/CdpSession.cs`

Files to modify:
- `clio/Common/BrowserSession/AuthenticatedBrowserLauncher.cs` — consume `ICdpSession`; optionally expose chosen `DevToolsPort`.
- `clio/Common/BrowserSession/IAuthenticatedBrowserLauncher.cs` — if port handoff is needed, change `LaunchAsync` to return `LaunchResult { int DevToolsPort }` (or add `LaunchAndKeepOpenAsync`). This is an **internal** interface — no RELEASE.md migration entry, but note it in the change summary.
- `clio/BindingsModule.cs` — register `services.AddTransient<ICdpSession, CdpSession>();` (DI; no `new`).

Pattern to follow: existing `clio/Common/BrowserSession/AuthenticatedBrowserLauncher.cs` (the source of the methods being moved); reference stories `story-browser-session-handoff-9` (CDP cookie injection, Mode A).

This story touches `clio/Common/**` and `clio/BindingsModule.cs` → **full unit suite trigger** (smart-testing rule 4). The launcher refactor is risk R-05 in the ADR — keep the contract identical; the launcher's live DevTools navigation is a manual E2E gate (carried from `story-browser-session-handoff-9`).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | `CdpSession.SendAsync` returns matching frame / throws on CDP error frame; `EvaluateAsync` issues `Runtime.evaluate`, honors `awaitPromise`, returns result; loopback-only target; port-read failure throws | `clio.tests/Common/BrowserSession/CdpSessionTests.cs` |
| Unit `[Category("Unit")]` | Launcher still launches + injects + navigates after refactor (mock `ICdpSession`); exposes `DevToolsPort` | `clio.tests/Common/BrowserSession/AuthenticatedBrowserLauncherTests.cs` (existing — keep green) |
| E2E `[Category("E2E")]` | Launcher live DevTools navigation regression guard (manual; not in CI) | reuse `story-browser-session-handoff-9` manual E2E gate |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004)
- [ ] `ICdpSession` registered in `BindingsModule.cs` (no `new` of behavior classes)
- [ ] `IAuthenticatedBrowserLauncher` contract behavior unchanged for `open-web-app --authenticated`
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] Full unit suite run (Common/ + BindingsModule.cs changed): `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit"` — 0 new failures
- [ ] Cookie VALUES never logged (names only — NFR-07); CDP loopback-only (NFR-06)
- [ ] Change summary notes the launcher refactor (internal interface return-type change)
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-06-12
- Implementation completed: 2026-06-12
- Tests passing: yes — new `CdpSessionTests` (7: send/skip-events/error-frame/evaluate+awaitPromise/exception/loopback-connect/cancel) + reworked `AuthenticatedBrowserLauncherTests` (mock `ICdpSession`, happy-path returns port + connect-fail). Full unit suite `dotnet test -c Release --filter "Category=Unit"` = 3881 passed, 0 failed, 20 skipped.
- Notes: Created `ICdpSession`/`CdpSession` + an `ICdpConnection` transport seam (`ClientWebSocketCdpConnection`) so SendAsync/EvaluateAsync are unit-testable. `ICdpConnection` made PUBLIC (impl internal) to satisfy accessibility on `CdpSession`'s public DI ctor (avoids `new` / CLIO001). `AuthenticatedBrowserLauncher` refactored to consume `ICdpSession` (dropped `IHttpClientFactory`; kept `ReadDevToolsPortAsync`); behavior for `open-web-app --authenticated` unchanged. Added `LaunchAndKeepOpenAsync` returning `LaunchResult{DevToolsPort}` (internal-interface change, no RELEASE.md entry); existing `LaunchAsync` delegates to it. DI: NO explicit BindingsModule line — `RegisterAssemblyInterfaceTypes` auto-registers `ICdpSession`+`ICdpConnection` (explicit add would duplicate). Built/tested in Release (clio MCP server locks bin/Debug). `ClientWebSocket.SendAsync(byte[])` resolves to the Task overload — used `ArraySegment<byte>`, no `.AsTask()`.
