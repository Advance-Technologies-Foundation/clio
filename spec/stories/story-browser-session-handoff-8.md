# Story 8: ClearBrowserSessionTool — MCP Tool

**Feature**: browser-session-handoff
**FR coverage**: FR-05, FR-14
**PRD**: [prd-browser-session-handoff.md](../prd/prd-browser-session-handoff.md)
**ADR**: [adr-browser-session-handoff.md](../adr/adr-browser-session-handoff.md)
**Status**: ready-for-dev
**Size**: S (< 2h)

---

## As a

AI agent (MCP client)

## I want

to call `clear-browser-session` via the MCP tool interface

## So that

I can force a fresh login on the next `get-browser-session` call when a session becomes stale

---

## Acceptance Criteria

- [ ] **AC-01** — Given the MCP tool is invoked with a valid environment name, when it executes, then `IBrowserSessionService.ClearSessionAsync()` is called and the tool returns a success result
- [ ] **AC-02** — Given the tool definition, when inspected, then `Destructive = true` and `Idempotent = true` are set
- [ ] **AC-03** — Given no cached session exists for the environment, when the tool is invoked, then it returns success (idempotent — no error)
- [ ] **AC-ERR** — Given the environment is not registered, when the tool is invoked, then it returns a structured error result with `Error: environment '<env>' not found`

---

## Implementation Notes

**File to create:**
- `clio/Command/McpServer/Tools/ClearBrowserSessionTool.cs`:
  - Inherit from `BaseTool<ClearBrowserSessionOptions>` (the generic base — there is no non-generic `BaseTool`)
  - Tool name: `clear-browser-session`
  - Safety flags: `ReadOnly = false`, `Destructive = true`, `Idempotent = true`
  - Parameters: `environment` (string, required)
  - Execution: call `IBrowserSessionService.ClearSessionAsync(env, ct)`
  - Return success message: `"Browser session for '<env>' cleared."`

**MCP unit test file to create:**
- `clio.tests/Command/McpServer/ClearBrowserSessionToolTests.cs`

**MCP E2E test additions:**
- Add `clear-browser-session` test cases to `clio.mcp.e2e/GetBrowserSessionToolE2ETests.cs` (same file as Story 7 E2E, or a separate `ClearBrowserSessionToolE2ETests.cs`)

**Depends on:** Story 4 (`IBrowserSessionService.ClearSessionAsync` must exist)

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Tool calls `ClearSessionAsync` with correct env | `clio.tests/Command/McpServer/ClearBrowserSessionToolTests.cs` |
| Unit `[Category("Unit")]` | Tool returns success result | `clio.tests/Command/McpServer/ClearBrowserSessionToolTests.cs` |
| Unit `[Category("Unit")]` | Unknown environment → structured error result | `clio.tests/Command/McpServer/ClearBrowserSessionToolTests.cs` |
| E2E `[Category("E2E")]` | Tool invocation via real MCP protocol deletes cached file | `clio.mcp.e2e/GetBrowserSessionToolE2ETests.cs` |

Test naming: `Execute_ShouldCallClearSessionAsync_WhenEnvironmentIsValid`, `Execute_ShouldReturnError_WhenEnvironmentNotFound`

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004)
- [ ] Tool safety flags: `ReadOnly=false`, `Destructive=true`, `Idempotent=true`
- [ ] Tool is idempotent: no error when session file does not exist
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] E2E test cases present in `clio.mcp.e2e/` with `[Category("E2E")]` and not-in-CI note
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
