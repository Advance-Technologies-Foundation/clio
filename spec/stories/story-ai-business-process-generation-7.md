# Story 7: process-add-element Command + MCP Tool + Prompt + Docs + Live E2E

**Feature**: ai-business-process-generation
**FR coverage**: FR-09, FR-11, FR-12, FR-13, FR-14, FR-15, FR-16, FR-17, FR-18
**PRD**: [prd-ai-business-process-generation.md](../prd/prd-ai-business-process-generation.md)
**ADR**: [adr-ai-business-process-generation.md](../adr/adr-ai-business-process-generation.md) (Decision 3, "CLI flag specification", "MCP surface — Tool 2", "Program.cs wiring", "Error / recovery handling")
**Status**: ready-for-dev
**Size**: L (full day)

---

## As a

developer (via AI agent / MCP) and CLI user

## I want

a `process-add-element` command (and matching env-sensitive MCP tool) that validates my planned graph, then deterministically drives the live Process Designer over CDP to append + configure a Read data element and SAVE, returning the saved process identity

## So that

I get a runnable `Start → Read data → End` process without hand-drawing BPMN — proven via readback (`generate-process-model` / `execute-esq` on `VwProcessLib`)

---

## Acceptance Criteria

- [ ] **AC-01** — Given a registered forms-auth environment and a fresh process, when `process-add-element --element-type read-data --read-object Contact` runs, then clio opens the authenticated designer via CDP, appends a Read data element onto the Start→End flow, configures the "Which object to read data from?" lookup to Contact, SAVEs, and reports success with the saved process code/UId. (PRD AC-07)
- [ ] **AC-02** (live readback) — Given AC-01 succeeded, when `generate-process-model --code <code>` runs against the same environment, then it exits 0 and emits `[BusinessProcess("<code>")]`; and `execute-esq` on `VwProcessLib` filtered by the process caption returns exactly one row. (PRD AC-08, OQ-04)
- [ ] **AC-03** — Given the append/connect produced a connection the designer flagged with `.djs-validate-outline`, when the command runs, then it does **not** SAVE, does **not** report success, and returns an `Error:` naming the invalid connection. (PRD AC-09, FR-13b)
- [ ] **AC-04** — Given the validator reports an `error` on the planned graph, when the command runs, then it aborts **before** driving the designer and returns the validator finding(s) — no browser is opened. (PRD AC-10, FR-13a)
- [ ] **AC-05** — Given the MCP tool, when inspected, then `ReadOnly = false`, `Idempotent = false`; `Destructive = false` for a new process and `Destructive = true` when `--process-id` is supplied (conservative static default + documented in the description); it is **environment-sensitive** and uses `InternalExecute<ProcessAddElementCommand>(options)`. (FR-12, OQ-01)
- [ ] **AC-06** — Given the CLI verb, when defined, then all long-name options are kebab-case: `--element-type`, `--read-object`, `--process-id`, `--process-caption`, `--headed`, plus reused `-e`/`--environment` from `EnvironmentOptions`; no camelCase/PascalCase (CLIO001), no aliases needed. (FR-11)
- [ ] **AC-07** — Given `--element-type` is anything other than `read-data`, when the command runs, then it returns an `Error:` (slice supports only Read data). (FR-10)
- [ ] **AC-08** — Given the MCP tool returns, when the call completes, then the response is `{success, code, uId, caption, error}` so the caller/E2E can read it back. (FR-14, OQ-04)
- [ ] **AC-09** — Given `docs/McpCapabilityMap.md`, when this story merges, then `process-add-element` is listed (env-sensitive) with correct safety flags, and the PR records "MCP reviewed, no update required" for `generate-process-model`/`execute-esq`. (PRD AC-11, FR-18)
- [ ] **AC-10** — Given documentation, when this story merges, then `help/en/process-add-element.txt`, `docs/commands/process-add-element.md`, a `Commands.md` entry, and the capability-map entry all exist and match source behavior. (FR-18)
- [ ] **AC-ERR** — Given an environment with no obtainable forms-auth browser session (or OAuth-only), or Chromium not installed, when the command runs, then it prints a specific `Error:` (e.g. "Error: a forms-auth browser session is required to drive the Process Designer for environment '<env>'" / "Error: Chromium not found …"), exits non-zero, opens no partial/blank designer, and reports no success. (PRD AC-ERR, FR-15, NFR-01, NFR-02)

## Implementation Notes

`ProcessAddElementCommand : Command<ProcessAddElementOptions>` orchestrates: resolve env → **validate planned graph** (`Start → readDataUserTask → End`) and abort on any `error` **before** launch (FR-13a, AC-10) → obtain forms-auth session (`IBrowserSessionService`) → launch authenticated browser (`IAuthenticatedBrowserLauncher`, get `DevToolsPort`) → drive (`IProcessDesignerDriver.AddReadDataElementAsync`) → return `{code, uId, caption}` or a user-friendly `Error:`. Auto-generate `--process-caption` (e.g. `clio-pae-<utc>-<short>`) when omitted (OQ-04). Constructor injection only (no `new`, no MediatR).

CLI flag spec (ADR — kebab-case, CLIO001): `--element-type` (Required, only `read-data`), `--read-object` (Required), `--process-id` (optional → new process when omitted), `--process-caption` (optional), `--headed` (default `true`; headless unverified — NFR-02). `[Verb("process-add-element", Aliases = ["pae"])]`.

Error classes (FR-15, NFR-04): Chromium not found (`ChromiumNotFoundException`); no forms-auth session (`CreatioAuthenticationException` / fail closed for OAuth-only); designer never rendered (`.djs-shape` timeout); object lookup not found; append/connect rejected (`.djs-validate-outline`); SAVE failed. Non-transactional: pre-SAVE failure discards the unsaved designer (browser closed via launcher cleanup); post-SAVE failure reports the saved identity. Never report success on a partial/failed run.

Files to create:
- `clio/Command/ProcessDesigner/ProcessAddElementCommand.cs` (+ `ProcessAddElementOptions`)
- `clio/Command/McpServer/Tools/ProcessAddElementTool.cs` (+ `ProcessAddElementArgs`)
- `clio/Command/McpServer/Prompts/ProcessAddElementPrompt.cs`
- `clio/help/en/process-add-element.txt`
- `clio/docs/commands/process-add-element.md`
- `clio.tests/Command/McpServer/ProcessAddElementToolTests.cs`
- `clio.tests/Command/ProcessDesigner/ProcessAddElementCommandTests.cs` (`BaseCommandTests<ProcessAddElementOptions>`)
- `clio.mcp.e2e/ProcessAddElementToolE2ETests.cs` (live build-and-readback; NOT in CI)

Files to modify:
- `clio/BindingsModule.cs` — `services.AddTransient<ProcessAddElementCommand>();`
- `clio/Program.cs` — add `typeof(ProcessAddElementOptions)` to the verb `Types[]` array + the arm `ProcessAddElementOptions opts => Resolve<ProcessAddElementCommand>(opts).Execute(opts)`. `validate-process-graph` stays MCP-only (not added).
- `clio/Commands.md` — add a `process-add-element` row/section.
- `docs/McpCapabilityMap.md` — add `process-add-element` (env-sensitive) safety flags.

Use the `$document-command` skill for docs and the `$create-mcp-tool` / `$test-mcp-tool` skills for the MCP tool + tests. Pattern to follow: skill-command family (`clio/Command/SkillCommands.cs`) for the `Command<TOptions>` shape; the env-aware `InternalExecute<TCommand>` `BaseTool` pattern (e.g. `GetBrowserSessionTool` / application tool family). Depends on Story 4 (validator) and Story 6 (driver); also relies on Story 1 (launcher port handoff). `BindingsModule.cs`/`Program.cs` changed → **full unit suite trigger**. Feasibility reference: env `krestov-test`, `UsrProcess_493d4c9`.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` (`BaseCommandTests<ProcessAddElementOptions>`) | validate-abort-before-browser (AC-04, mocked `IProcessGraphValidator` returns error → `IAuthenticatedBrowserLauncher` never called); error classes (AC-ERR) with mocked driver/session/launcher; `read-data` guard (AC-07); caption auto-generation | `clio.tests/Command/ProcessDesigner/ProcessAddElementCommandTests.cs` |
| Unit `[Category("Unit")]` | arg → options mapping; Destructive semantics (new vs `--process-id`); `read-data` guard; env-sensitive path uses `InternalExecute<TCommand>` | `clio.tests/Command/McpServer/ProcessAddElementToolTests.cs` |
| E2E `[Category("E2E")]` | Live `Start → Read data → End` build + readback via `generate-process-model` / `execute-esq` on `VwProcessLib` (AC-01/02/03); requires Chromium + a live forms-auth env (`krestov-test`) | `clio.mcp.e2e/ProcessAddElementToolE2ETests.cs` (NOT in CI) |

Test naming: `MethodName_ShouldBehavior_WhenCondition` (AAA + `because` + `[Description]`). Register test doubles in `AdditionalRegistrations`; resolve the SUT from the container; `ClearReceivedCalls` in teardown.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004)
- [ ] All CLI flags are kebab-case (`--element-type`, `--read-object`, `--process-id`, `--process-caption`, `--headed`); no aliases needed
- [ ] `ProcessAddElementCommand` registered in `BindingsModule.cs`; verb wired in `Program.cs` `Types[]` + execution arm
- [ ] MCP tool env-sensitive via `InternalExecute<ProcessAddElementCommand>`; Destructive semantics per OQ-01; prompt added
- [ ] Pre-SAVE validate aborts before opening a browser on any `error`; `.djs-validate-outline` gate enforced; no false-positive save
- [ ] User-friendly `Error:` per failure class (no stack traces unless `--debug`); cookie values never logged
- [ ] Docs updated: `help/en/process-add-element.txt`, `docs/commands/process-add-element.md`, `Commands.md`, `docs/McpCapabilityMap.md`; "MCP reviewed, no update required" recorded for `generate-process-model`/`execute-esq`
- [ ] `clio.mcp.e2e` coverage added (flagged: not in CI)
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`; command tests use `BaseCommandTests<ProcessAddElementOptions>`
- [ ] Full unit suite run (BindingsModule.cs/Program.cs changed): `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit"` — 0 new failures
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-06-12
- Implementation completed: 2026-06-12 (unit + wiring + docs); LIVE build-and-readback E2E GREEN on krestov-test via the clio MCP tools — process-add-element built+saved UsrProcess_c13f45c ("AI e2e read contact v4"), describe-process read it back as Start→ReadDataUserTask1→End. Required 2 driver-recipe tuning fixes (append materializing-mousemove; explicit trusted-select of the appended element + fillObject poll for the async card) — see diary 2026-06-12 "LIVE e2e GREEN".
- Tests passing: yes — `ProcessAddElementCommandTests` (7, BaseCommandTests: read-data guard, validate-abort-before-browser, no-session, chromium-not-found, driver-failure, caption auto-gen, caption forwarded), `ProcessAddElementToolTests` (3: safety flags, arg mapping, prompt). Full unit suite 3920 passed, 0 failed, 20 skipped; `clio.mcp.e2e` builds (2 tests, not in CI).
- Notes: `ProcessAddElementCommand`+`ProcessAddElementOptions` (kebab `--element-type/--read-object/--process-id/--process-caption/--headed`) orchestrates: read-data guard → settingsRepository.GetEnvironment → validator.Validate(Start→readDataUserTask→End) abort-before-browser on error → IBrowserSessionService.GetSessionPathAsync (CreatioAuthenticationException → forms-auth Error) → IAuthenticatedBrowserLauncher.LaunchAndKeepOpenAsync (ChromiumNotFoundException → Error) → IProcessDesignerDriver.AddReadDataElementAsync → JSON {success,code,uId,caption} on success / Error: on failure (no false-positive save). Caption auto-gen `clio-pae-<utc>-<short>` when omitted. Env-aware MCP tool `process-add-element` (InternalExecute<ProcessAddElementCommand>; ReadOnly=false/Idempotent=false/Destructive=false static default per OQ-01 + documented) + prompt. Wired Program.cs verb+arm, BindingsModule (command+tool); added `using Clio.Command.ProcessDesigner;` to Program.cs+BindingsModule (folder-matched namespace). Docs: help/en, docs/commands, Commands.md, WikiAnchors, McpCapabilityMap §11. MCP reviewed for generate-process-model/execute-esq: no update required. Built/tested in Release (clio MCP server locks bin/Debug). LIVE e2e (build+readback on krestov-test) is the remaining manual gate — the driver recipe (Story 6) gets tuned there.
