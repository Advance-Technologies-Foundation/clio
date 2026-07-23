# Story 1: Safe-env Deadlock Fix — IInteractiveConsole Abstraction (fail-closed, all callers)

**Feature**: browser-session-handoff
**FR coverage**: FR-09
**PRD**: [prd-browser-session-handoff.md](../prd/prd-browser-session-handoff.md)
**ADR**: [adr-browser-session-handoff.md](../adr/adr-browser-session-handoff.md) (Decision 4)
**Status**: ready-for-dev
**Size**: M (half day)
**Revised**: 2026-06-10 — corrects BL-2 (4 callers, fail-closed default, dedicated exception)

---

## As a

CI pipeline author

## I want

the Safe-env confirmation to stop calling `Console.ReadKey()` / `Environment.Exit(1)` in non-interactive contexts, at **every** call site

## So that

MCP stdio server commands against Safe-flagged environments complete with a structured error instead of deadlocking or killing the process

---

## Background (why the first design was wrong)

`.Fill()` has **four** call sites (verified), three of which are the MCP path this fix targets:
- `clio/Environment/ConfigurationOptions.cs:587` (`SettingsRepository.GetEnvironment`)
- `clio/Command/McpServer/Tools/ToolCommandResolver.cs:59`
- `clio/Command/McpServer/Tools/ToolCommandResolver.cs:62` (`new EnvironmentSettings().Fill(options)`)
- `clio/Command/McpServer/Tools/ToolCommandResolver.cs:88`

An optional `IInteractiveConsole = null` defaulting to `RealInteractiveConsole` is **fail-open**: any caller not passing the non-interactive impl keeps the deadlock. Moving the check **out** of `Fill()` to a downstream boundary is also wrong: `Fill()` never copies `Safe` to its result (`ConfigurationOptions.cs:174-235`), so a boundary reading `env.Safe` would read `null` and **silently never prompt** — dropping production protection for every CLI command. This story therefore **keeps the check inside `Fill()`** (where `this.Safe` is valid) but makes `IInteractiveConsole` a **required parameter** of `Fill()` — compile-enforced at all four call sites, so no one can "forget to pass it", and the MCP sites pass the fail-closed `NonInteractiveConsole`.

---

## Acceptance Criteria

- [ ] **AC-01** — Given `Fill(options, console)` called with a `NonInteractiveConsole` and a Safe-flagged environment, when invoked, then a `SafeEnvironmentConfirmationRequiredException` is thrown and `Console.ReadKey()` is never called
- [ ] **AC-02** — Given `Fill(options, console)` with a `RealInteractiveConsole` and a Safe-flagged environment, when the user presses a key other than `y`, then `SafeEnvironmentConfirmationRequiredException` is thrown (same observable refusal as before)
- [ ] **AC-03** — Given `Fill(options, console)` with a `RealInteractiveConsole` and a Safe-flagged environment, when the user confirms with `y`, then execution continues normally without throwing
- [ ] **AC-04** — Given the MCP server startup path, when `IInteractiveConsole` is resolved from DI, then `NonInteractiveConsole` is returned and `ToolCommandResolver` passes it to `Fill(options, _console)` at all 3 sites (59/62/88)
- [ ] **AC-05** — Given the CLI (non-MCP) startup path, when `IInteractiveConsole` is resolved (or the `new SettingsRepository()` sites in `Program.cs`), then `RealInteractiveConsole` is used
- [ ] **AC-06** — Given `EnvironmentSettings.Fill()` after this change, when inspected, then it contains **no** `Console.ReadKey()` / `Console.WriteLine` confirmation / `Environment.Exit()` — the confirmation goes through `console.Prompt()`, and `Fill` now requires an `IInteractiveConsole` parameter (no default)
- [ ] **AC-07** — Given a Safe env and a declined/non-interactive confirmation at **any** of the four call sites, when the command runs, then the command **returns non-zero and does not proceed** (regression guard: no production mutation slips through)
- [ ] **AC-08 (regression)** — Given an ordinary CLI command (e.g. a non-browser-session verb) against a Safe env with a `RealInteractiveConsole`, when run, then the production confirmation prompt **still fires** (the prompt must not be silently lost by this refactor)
- [ ] **AC-ERR** — Given a Safe-flagged environment in an MCP stdio session, when an MCP tool resolves it, then the tool returns a structured error (e.g. `Error: Safe environment confirmation required but the context is non-interactive.`) and exits non-zero, without hanging

---

## Implementation Notes

**Files to create:**
- `clio/Common/IInteractiveConsole.cs` — `bool Prompt(string message)`
- `clio/Common/RealInteractiveConsole.cs` — production (CLI); abstract the keypress source (inject a `TextReader`/`Func<char>`) so unit tests stay `[Category("Unit")]` without a real console
- `clio/Common/NonInteractiveConsole.cs` — returns `false` (**fail closed**), logs a warning; no `ReadKey`
- `clio/Common/SafeEnvironmentConfirmationRequiredException.cs` — dedicated exception (do **not** reuse `OperationCanceledException`, which collides with cancellation plumbing and may be swallowed)

**Files to modify:**
- `clio/Environment/ConfigurationOptions.cs` — change `Fill(EnvironmentOptions options)` → `Fill(EnvironmentOptions options, IInteractiveConsole console)` (required param). Inside `Fill()`, replace the `Console.ReadKey()`/`Environment.Exit(1)` block (still reads `this.Safe`) with:
  ```csharp
  if (this.Safe == true && !console.Prompt($"Modify production environment {this.Uri}? [Y/N]")) {
      throw new SafeEnvironmentConfirmationRequiredException(this.Uri);
  }
  ```
  `SettingsRepository` gains an `IInteractiveConsole` constructor dependency and calls `Fill(options, _console)` at `ConfigurationOptions.cs:587`.
- `clio/Command/McpServer/Tools/ToolCommandResolver.cs` — inject `IInteractiveConsole` (bound to `NonInteractiveConsole` in the MCP host) and pass it to `Fill(options, _console)` at lines 59, 62, 88.
- `clio/Program.cs` — at the `new SettingsRepository()` sites (`Program.cs:556,634,658`), pass `new RealInteractiveConsole()` (CLI is interactive).
- `clio/BindingsModule.cs` — register `IInteractiveConsole` → `RealInteractiveConsole` (CLI)
- MCP server startup — register `IInteractiveConsole` → `NonInteractiveConsole`

**MCP catch point:** `BaseTool<T>.InternalExecute()` already wraps execution in `try/catch (Exception) → CommandExecutionResult.FromException` (`BaseTool.cs:91-117`). Verify the resulting message wording against `CommandExecutionResult.FromException`; the dedicated exception flows through there — no bespoke catch needed unless wording must differ.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | `NonInteractiveConsole.Prompt()` returns `false` without blocking | `clio.tests/Common/SafeEnvironmentFillTests.cs` |
| Unit `[Category("Unit")]` | `Fill(options, NonInteractiveConsole)` + Safe=true → `SafeEnvironmentConfirmationRequiredException`, no `ReadKey` | `clio.tests/Common/SafeEnvironmentFillTests.cs` |
| Unit `[Category("Unit")]` | `Fill(options, console)` + Safe=false → completes normally, console not prompted | `clio.tests/Common/SafeEnvironmentFillTests.cs` |
| Unit `[Category("Unit")]` | `RealInteractiveConsole` with injected keypress source `'n'` → refuse; `'y'` → confirm | `clio.tests/Common/SafeEnvironmentFillTests.cs` |
| Unit `[Category("Unit")]` | DI: MCP context resolves `NonInteractiveConsole`; CLI resolves `RealInteractiveConsole` | `clio.tests/Common/SafeEnvironmentFillTests.cs` |
| Unit `[Category("Unit")]` | Command returns non-zero / does not proceed when confirmation declined (AC-07) | `clio.tests/Common/SafeEnvironmentFillTests.cs` |
| Unit `[Category("Unit")]` | **Regression**: an ordinary command against a Safe env with `RealInteractiveConsole` still prompts (AC-08) | `clio.tests/Common/SafeEnvironmentFillTests.cs` |

Test naming: `Fill_ShouldThrowSafeEnvironmentConfirmationRequiredException_WhenNonInteractiveAndSafeEnvironment`

## Definition of Done

- [x] Code compiles without `CLIO*` analyzer warnings (CLIO002 on the genuine interactive prompt suppressed with justification in `RealInteractiveConsole`)
- [x] `EnvironmentSettings.Fill()` takes a **required** `IInteractiveConsole`; no `Console.ReadKey()` / `Environment.Exit()`; check reads `this.Safe` and uses `console.Prompt()`
- [x] All 4 `Fill()` call sites pass a console: `SettingsRepository.GetEnvironment` (ctor dep `_interactiveConsole ?? RealInteractiveConsole.Shared`) + `ToolCommandResolver` (3 sites, injected). (Correction: the only prod `new SettingsRepository()` is `ConfigurationOptions.cs:556`, internal, never calls `Fill` — no `Program.cs` site exists.)
- [x] Non-interactive path **fails closed**; dedicated `SafeEnvironmentConfirmationRequiredException` used
- [x] `IInteractiveConsole`, `RealInteractiveConsole`, `NonInteractiveConsole`, the exception — all with XML doc comments
- [x] `IInteractiveConsole` registered in `BindingsModule.cs` → `RealInteractiveConsole` (single composition; `RealInteractiveConsole` self-guards on `Console.IsInputRedirected`, so the same binding is fail-closed for the MCP per-env containers — see Notes)
- [x] Regression tests prove (a) no command proceeds against a declined Safe env (AC-07) AND (b) an ordinary CLI command against a Safe env still prompts (AC-08)
- [x] Unit tests `[Category("Unit")]` — never `[Category("UnitTests")]`
- [x] **Smart regression**: full unit suite (touches `ConfigurationOptions.cs` + `BindingsModule.cs` — shared infra) → 3483 passed, 3 pre-existing macOS path failures (unrelated), 0 new failures
- [ ] PR description references this story file (no PR opened yet)

## Dev Agent Record

- Implementation started: 2026-06-10
- Implementation completed: 2026-06-10
- Tests passing: 8 new `SafeEnvironmentFillTests` + regression (`SettingsRepositoryGetEnvironmentTests`, `ToolCommandResolverTests`) green; full unit suite 3483 passed / 0 new failures (3 pre-existing macOS path failures unrelated)
- Files: `clio/Common/{IInteractiveConsole,RealInteractiveConsole,NonInteractiveConsole,SafeEnvironmentConfirmationRequiredException}.cs` (new); `clio/Environment/ConfigurationOptions.cs` (`Fill` required-param + `SettingsRepository` ctor dep + `GetEnvironment`); `clio/Command/McpServer/Tools/ToolCommandResolver.cs` (ctor dep + 3 `Fill` calls); `clio/BindingsModule.cs` (register + pass to `SettingsRepository`); `clio.tests/Common/SafeEnvironmentFillTests.cs` (new); `clio.tests/Command/McpServer/ToolCommandResolverTests.cs` (ctor call sites).
- Notes:
  - **Design refinement vs ADR Decision 4 (single composition root).** `IToolCommandResolver` and the per-env MCP containers are both built from the SAME `BindingsModule`, so a static CLI-vs-MCP console binding is impossible. Instead `RealInteractiveConsole.Prompt()` fails closed on `Console.IsInputRedirected` (true under MCP stdio / CI), so the single `BindingsModule` binding to `RealInteractiveConsole` is correct everywhere: interactive terminal → prompts; MCP/CI → fails closed → `SafeEnvironmentConfirmationRequiredException` → `BaseTool<T>.InternalExecute` structured error. `NonInteractiveConsole` is kept for explicit non-interactive use and tests. All ADR contracts hold (required `Fill` param, fail-closed, dedicated exception, both classes).
  - **MCP reviewed, no tool-contract update required**: `ToolCommandResolver` gained a DI-internal ctor dependency only; no MCP tool argument / description / safety-flag changes. The Safe-env-no-hang behavior is exercised by the existing `BaseTool` exception path (a CI guard for SM-03 is tracked as test-plan TC-I-05, to land with the MCP tool stories).
  - **Docs reviewed, no update required**: no new verb/option/command behavior visible to users; `IInteractiveConsole` is internal infrastructure.
