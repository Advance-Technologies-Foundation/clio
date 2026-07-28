# Story 7: GetBrowserSessionTool — MCP Tool

**Feature**: browser-session-handoff
**FR coverage**: FR-04, FR-10
**PRD**: [prd-browser-session-handoff.md](../prd/prd-browser-session-handoff.md)
**ADR**: [adr-browser-session-handoff.md](../adr/adr-browser-session-handoff.md)
**Status**: ready-for-dev
**Size**: M (half day)
**Revised**: 2026-06-10 — `BaseTool<T>` (not `BaseTool`), dedicated exception, `outputPath` removed from MCP surface (CLI-only)

---

## As a

AI agent (MCP client)

## I want

to call `get-browser-session` via the MCP tool interface and receive an absolute path to a Playwright-compatible storageState file

## So that

I can open Creatio in a browser context that is already authenticated without handling the auth flow myself

---

## Acceptance Criteria

- [ ] **AC-01** — Given the MCP tool is invoked with a valid environment name, when it executes, then it returns `{ "sessionFilePath": "<absolute path>" }` and the response contains no cookie values
- [ ] **AC-02** — Given a Safe-flagged environment in an MCP stdio session, when the tool is invoked, then it returns a structured error result instead of hanging (relies on `NonInteractiveConsole` from Story 1)
- [ ] **AC-03** — Given the tool definition, when inspected, then `ReadOnly = false`, `Destructive = false`, `Idempotent = false` are set
- [ ] **AC-04** — Given the tool is invoked with `forceRefresh = true`, when it executes, then `IBrowserSessionService.GetSessionPathAsync()` is called with `forceRefresh = true`
- [ ] **AC-05** — Given the MCP tool definition, when inspected, then it exposes **only** `environment` and `forceRefresh` — `outputPath` is **NOT** an MCP parameter (CLI-only; an agent must not redirect a bearer-cookie file to an arbitrary path)
- [ ] **AC-ERR** — Given auth fails, when the tool executes, then it returns `CommandExecutionResult.FromError("Error: authentication failed for environment '<env>' — check username and password in env config")` with a sanitized message (no cookie/URL/body)

---

## Implementation Notes

**File to create:**
- `clio/Command/McpServer/Tools/GetBrowserSessionTool.cs`:
  - Inherit from `BaseTool<GetBrowserSessionOptions>` (the generic base; environment-aware execution — do not inject the command at startup; resolve per-call)
  - Tool name: `get-browser-session`
  - Safety flags: `ReadOnly = false`, `Destructive = false`, `Idempotent = false`
  - Parameters: `environment` (string, required), `forceRefresh` (bool, optional). **No `outputPath`** — CLI-only (see AC-05)
  - Execution: call `IBrowserSessionService.GetSessionPathAsync(env, overrideOutputPath: null, forceRefresh, ct)`
  - Response JSON: `{ "sessionFilePath": "<path>" }` — no cookie values in any response field
  - Safe-env handling: the `SafeEnvironmentConfirmationRequiredException` from Story 1 propagates through `BaseTool<T>.InternalExecute()` (`BaseTool.cs:91-117`), which already converts it to a structured `CommandExecutionResult`. No bespoke catch needed unless the message wording must differ — verify against `CommandExecutionResult.FromException`

**Key file:** `clio/Command/McpServer/Tools/` — follow the existing `BaseTool<T>` subclass pattern

**Depends on:** Story 1 (NonInteractiveConsole DI wiring) and Story 5 (GetBrowserSessionCommand + options)

**MCP unit test file to create:**
- `clio.tests/Command/McpServer/GetBrowserSessionToolTests.cs`

**MCP E2E test file to create:**
- `clio.mcp.e2e/GetBrowserSessionToolE2ETests.cs` — not in CI; manual execution only; must be marked with a note in the file header

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Tool maps environment arg and calls `GetSessionPathAsync` | `clio.tests/Command/McpServer/GetBrowserSessionToolTests.cs` |
| Unit `[Category("Unit")]` | Tool returns `sessionFilePath` in response, no cookie fields | `clio.tests/Command/McpServer/GetBrowserSessionToolTests.cs` |
| Unit `[Category("Unit")]` | MCP param set is `{environment, forceRefresh}` only — no `outputPath` | `clio.tests/Command/McpServer/GetBrowserSessionToolTests.cs` |
| Unit `[Category("Unit")]` | `SafeEnvironmentConfirmationRequiredException` → structured error result (no hang) | `clio.tests/Command/McpServer/GetBrowserSessionToolTests.cs` |
| E2E `[Category("E2E")]` | Tool invocation via real MCP protocol returns file path | `clio.mcp.e2e/GetBrowserSessionToolE2ETests.cs` |

Test naming: `Execute_ShouldReturnSessionFilePath_WhenEnvironmentIsValid`, `Execute_ShouldReturnError_WhenSafeEnvironmentAndNonInteractive`

## Definition of Done

- [x] Code compiles without `CLIO*` analyzer warnings
- [x] Returns a structured typed response (`GetBrowserSessionResult`) — see Notes for why the `SchemaNamePrefixTool` pattern is used instead of `BaseTool<T>` (which returns `CommandExecutionResult`)
- [x] Tool safety flags: `ReadOnly=false`, `Destructive=false`, `Idempotent=false`
- [x] MCP param set is `{environment-name, force-refresh}` only — `output-path` not exposed (CLI-only)
- [x] Response carries `session-file-path` and never a cookie value; error messages sanitized
- [x] `SafeEnvironmentConfirmationRequiredException` yields a structured error result (no hang) — verified by test
- [x] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [x] E2E coverage added in `clio.mcp.e2e/GetBrowserSessionToolE2ETests.cs` (advertised-tool hermetic test + reachable-env happy path; not in CI — `Assert.Ignore` without a sandbox)
- [ ] PR description references this story file (single PR at the end)

## Dev Agent Record

- Implementation started: 2026-06-10
- Implementation completed: 2026-06-10
- Tests passing: 5 `GetBrowserSessionToolTests` (path returned; force-refresh forwarded; Safe-env → structured error; auth failure → error; args expose no output-path); full unit suite 3523 passed / 0 new failures; `clio.mcp.e2e` builds (2 E2E tests).
- Files: `clio/Command/McpServer/Tools/GetBrowserSessionTool.cs` (tool + args + result records); `clio.tests/Command/McpServer/GetBrowserSessionToolTests.cs`; `clio.mcp.e2e/GetBrowserSessionToolE2ETests.cs`.
- Notes:
  - **Pattern choice (deviation from the story's `BaseTool<T>` line, justified):** `BaseTool<T>.InternalExecute` returns a `CommandExecutionResult` (exit code + flushed log lines), which cannot express the story's required `{ session-file-path }` payload. The tool therefore follows the established **`SchemaNamePrefixTool` structured-response pattern** — inject `IToolCommandResolver`, `Resolve<IBrowserSessionService>(env)` + `Resolve<EnvironmentSettings>(env)` from the per-env container, call the service, and return a typed `GetBrowserSessionResult`. No explicit DI registration is needed (auto-discovered by `WithToolsFromAssembly`, constructed via `ActivatorUtilities`, exactly like `SchemaNamePrefixTool`).
  - **Safe-env fail-closed**: the `Resolve` call runs `Fill` → the Story-1 console fails closed on the MCP stdio path → `SafeEnvironmentConfirmationRequiredException` is caught and returned as a structured error (no hang). This is the in-tool counterpart of SM-03 (the automated stdio CI guard remains a follow-up — test-plan TC-I-05).
  - **MCP reviewed**: `output-path` deliberately omitted from the MCP surface (CLI-only) so an agent cannot redirect a bearer-token file. The capability-map entry (`docs/McpCapabilityMap.md`) is Story 10. No existing MCP prompt/resource enumerates per-tool, so none needed updating.
