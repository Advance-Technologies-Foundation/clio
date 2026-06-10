# Story 6: ClearBrowserSessionCommand — CLI Verb

**Feature**: browser-session-handoff
**FR coverage**: FR-02, FR-12
**PRD**: [prd-browser-session-handoff.md](../prd/prd-browser-session-handoff.md)
**ADR**: [adr-browser-session-handoff.md](../adr/adr-browser-session-handoff.md)
**Status**: ready-for-dev
**Size**: S (< 2h)

---

## As a

developer (CLI user)

## I want

to run `clio clear-browser-session -e <env>` to delete the cached storageState for a target environment

## So that

the next `get-browser-session` call performs a fresh login instead of reusing a stale session

---

## Acceptance Criteria

- [ ] **AC-01** — Given an existing cached session for `<env>`, when `clio clear-browser-session -e <env>` is called, then the cached storageState file is deleted and exit code is 0
- [ ] **AC-02** — Given no cached session exists for `<env>`, when `clio clear-browser-session -e <env>` is called, then the command completes with exit code 0 (idempotent — no error if nothing to delete)
- [ ] **AC-03** — Given the command options class, when inspected via Roslyn analyzer, then all `[Option]` names are kebab-case and CLIO001 emits no warnings
- [ ] **AC-04** — Given a session was cleared, when `clio get-browser-session -e <env>` is subsequently called, then a fresh login is performed
- [ ] **AC-ERR** — Given the environment is not registered, when `clio clear-browser-session -e <unknown>` is called, then clio prints `Error: environment '<unknown>' not found` and exits non-zero

---

## Implementation Notes

**Files to create:**
- `clio/Command/BrowserSession/ClearBrowserSessionOptions.cs`:
  ```csharp
  [Verb("clear-browser-session", HelpText = "Delete the cached browser session for an environment")]
  public class ClearBrowserSessionOptions : EnvironmentOptions { }
  ```
- `clio/Command/BrowserSession/ClearBrowserSessionCommand.cs` — `Command<ClearBrowserSessionOptions>`:
  - Constructor injects `IBrowserSessionService`, `ISettingsRepository`
  - `Execute()` resolves env from `ISettingsRepository`, calls `IBrowserSessionService.ClearSessionAsync(env)`
  - Prints a confirmation message on success (e.g. `Browser session for '<env>' cleared.`)
  - On unknown environment: prints `Error: environment '<env>' not found` and exits non-zero

**Files to modify:**
- `clio/BindingsModule.cs` — register `ClearBrowserSessionCommand`
- `clio/Program.cs` — wire `ClearBrowserSessionOptions` verb

**Depends on:** Story 4 (`IBrowserSessionService.ClearSessionAsync` must exist)

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | `Execute()` with valid env → `ClearSessionAsync` called, exit 0 | `clio.tests/Command/ClearBrowserSessionCommandTests.cs` |
| Unit `[Category("Unit")]` | `Execute()` with unknown env → error message, non-zero exit | `clio.tests/Command/ClearBrowserSessionCommandTests.cs` |

Use `BaseCommandTests<ClearBrowserSessionOptions>` as fixture base class.
Test naming: `Execute_ShouldCallClearSessionAsync_WhenEnvironmentIsValid`, `Execute_ShouldExitNonZero_WhenEnvironmentNotFound`

## Definition of Done

- [ ] Code compiles without `CLIO*` analyzer warnings
- [ ] All CLI option names are kebab-case; CLIO001 passes
- [ ] `ClearBrowserSessionCommand` registered in `BindingsModule.cs` and wired in `Program.cs`
- [ ] Command is idempotent: no error if session file does not exist
- [ ] **MCP review (AGENTS.md mandatory)**: `ClearBrowserSessionTool` (Story 8) reflects this command's final contract; state "MCP reviewed" in the PR
- [ ] **Docs review (AGENTS.md mandatory)**: help/`docs/commands` slice tracked in Story 10; state "docs reviewed" in the PR
- [ ] Unit tests use `BaseCommandTests<ClearBrowserSessionOptions>` and `[Category("Unit")]`
- [ ] **Smart regression**: `dotnet test --filter "Category=Unit&Module=Command"`
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
