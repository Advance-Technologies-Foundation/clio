# Story 5: GetBrowserSessionCommand — CLI Verb

**Feature**: browser-session-handoff
**FR coverage**: FR-01, FR-11, FR-12
**PRD**: [prd-browser-session-handoff.md](../prd/prd-browser-session-handoff.md)
**ADR**: [adr-browser-session-handoff.md](../adr/adr-browser-session-handoff.md)
**Status**: ready-for-dev
**Size**: M (half day)

---

## As a

developer (CLI user)

## I want

to run `clio get-browser-session -e <env>` and get an absolute path to a Playwright-compatible storageState file

## So that

I can pass the file directly to a Playwright script without implementing Creatio auth myself

---

## Acceptance Criteria

- [ ] **AC-01** — Given a registered environment with valid credentials, when `clio get-browser-session -e <env>` is called, then a storageState JSON file is written to disk and its absolute path is printed to stdout; exit code is 0
- [ ] **AC-02** — Given a valid cached session, when `clio get-browser-session -e <env>` is called again, then **no POST is made to `AuthService.svc/Login`** (a lightweight validation GET to the env root is permitted) and the cached file path is returned; exit code is 0
- [ ] **AC-03** — Given `--output-path /tmp/my-session.json` is supplied, when the command runs, then the storageState file is written to `/tmp/my-session.json` and that path is printed to stdout
- [ ] **AC-04** — Given `--force-refresh` is supplied with a valid cached session, when the command runs, then a fresh login is performed and a new storageState file is written
- [ ] **AC-05** — Given the command options class, when inspected via Roslyn analyzer, then all `[Option]` names are kebab-case (`--output-path`, `--force-refresh`) and CLIO001 emits no warnings
- [ ] **AC-ERR** — Given invalid or missing credentials in env config, when `clio get-browser-session -e <env>` is called, then clio prints `Error: authentication failed for environment '<env>' — check username and password in env config` and exits non-zero

---

## Implementation Notes

**Files to create:**
- `clio/Command/BrowserSession/GetBrowserSessionOptions.cs`:
  ```csharp
  [Verb("get-browser-session", HelpText = "Obtain a Playwright-compatible storageState for a Creatio environment")]
  public class GetBrowserSessionOptions : EnvironmentOptions
  {
      [Option("output-path", Required = false, HelpText = "File path to write storageState JSON")]
      public string OutputPath { get; set; }

      [Option("force-refresh", Required = false, HelpText = "Bypass cache and perform a fresh login")]
      public bool ForceRefresh { get; set; }
  }
  ```
- `clio/Command/BrowserSession/GetBrowserSessionCommand.cs` — `Command<GetBrowserSessionOptions>`:
  - Constructor injects `IBrowserSessionService`, `ISettingsRepository`
  - `Execute()` calls `IBrowserSessionService.GetSessionPathAsync(env, options.OutputPath, options.ForceRefresh)` and prints the returned path to stdout
  - On error: catches the auth exception and prints `Error: {message}` before exiting non-zero
  - **Do not print cookie values; print only the file path**

**Files to modify:**
- `clio/BindingsModule.cs` — register `GetBrowserSessionCommand`
- `clio/Program.cs` — wire `GetBrowserSessionOptions` verb: `typeof(GetBrowserSessionOptions)` in verb list and a `case GetBrowserSessionOptions:` branch in the parser switch

**Reference pattern:** `clio/Command/SkillCommands.cs` for constructor injection and `Execute()` structure

**Depends on:** Story 4 (`IBrowserSessionService` must exist)

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | `Execute()` with valid env → calls `GetSessionPathAsync`, prints path to stdout, returns 0 | `clio.tests/Command/GetBrowserSessionCommandTests.cs` |
| Unit `[Category("Unit")]` | `Execute()` with `--force-refresh` → `forceRefresh = true` propagated to service | `clio.tests/Command/GetBrowserSessionCommandTests.cs` |
| Unit `[Category("Unit")]` | `Execute()` with `--output-path` → `overrideOutputPath` propagated to service | `clio.tests/Command/GetBrowserSessionCommandTests.cs` |
| Unit `[Category("Unit")]` | `Execute()` when service throws → error message printed, non-zero exit | `clio.tests/Command/GetBrowserSessionCommandTests.cs` |

Use `BaseCommandTests<GetBrowserSessionOptions>` as the fixture base class.
Test naming: `Execute_ShouldReturnFilePath_WhenEnvironmentIsValid`, `Execute_ShouldExitNonZero_WhenAuthFails`

## Definition of Done

- [x] Code compiles without `CLIO*` analyzer warnings
- [x] All CLI option names are kebab-case; CLIO001 passes (`--output-path`, `--force-refresh`)
- [x] `GetBrowserSessionCommand` registered in `BindingsModule.cs` and wired in `Program.cs` (verb list + switch)
- [x] Command does not print cookie values — only the file path (`logger.WriteInfo(path)`)
- [x] `--output-path` is **CLI-only** (NOT on the MCP surface — Story 7) and invalid paths surface an error from the cache layer (Story 3 FR-11a)
- [x] **MCP reviewed**: no MCP tool consumes this command yet — Story 7 adds `GetBrowserSessionTool` aligned to these options; stated here
- [x] **Docs reviewed**: docs created with the command (ReadmeChecker requires them) — `help/en/get-browser-session.txt`, `docs/commands/get-browser-session.md`, `Commands.md` index entry, `Wiki/WikiAnchors.txt`
- [x] Unit tests use `BaseCommandTests<GetBrowserSessionOptions>` and `[Category("Unit")]`
- [x] **Smart regression**: full unit suite (BindingsModule + Program.cs touched) → 3515 passed, 0 new failures (3 pre-existing macOS)
- [ ] PR description references this story file (single PR at the end)

## Dev Agent Record

- Implementation started: 2026-06-10
- Implementation completed: 2026-06-10
- Tests passing: 4 `GetBrowserSessionCommandTests` (valid env → 0 + delegates; `--output-path`/`--force-refresh` forwarded; auth failure → exit 1; inherited ReadmeChecker doc-block test); full unit suite 3515 passed / 0 new failures
- Files: `clio/Command/BrowserSession/GetBrowserSessionCommand.cs` (options + command); `clio/BindingsModule.cs` (1 registration); `clio/Program.cs` (verb list + switch); docs: `clio/help/en/get-browser-session.txt`, `clio/docs/commands/get-browser-session.md`, `clio/Commands.md`, `clio/Wiki/WikiAnchors.txt`; `clio.tests/Command/GetBrowserSessionCommandTests.cs`.
- Notes:
  - **Docs pulled forward from Story 10**: `BaseCommandTests<T>.Command_ShouldHave_DescriptionBlock_InReadmeFile` requires ALL of `Commands.md` index link, `help/en/<verb>.txt`, `docs/commands/<verb>.md`, and a `Wiki/WikiAnchors.txt` line — so the per-command docs are created here (alongside the verb), not deferred. Story 10 keeps the MCP capability map + cross-cutting polish.
  - `Command<GetBrowserSessionOptions>` resolves the env via `ISettingsRepository.GetEnvironment(options)` (Safe prompt fires via the Story-1 console) and prints only the path; sanitized `CreatioAuthenticationException` → exit 1.
