# Story 2: `get-user-culture` CLI Verb

**Feature**: user-profile-language-detection
**FR coverage**: FR-11 (OQ-01)
**PRD**: [prd-user-profile-language-detection.md](../prd/prd-user-profile-language-detection.md)
**ADR**: [adr-user-profile-language-detection.md](../adr/adr-user-profile-language-detection.md)
**Status**: ready-for-dev
**Size**: S (< 2h)
**Depends on**: story-user-profile-language-detection-1
**Blocks**: none

---

## As a

developer / QA engineer

## I want

a diagnostic CLI verb `get-user-culture` that prints the resolved profile culture for an environment

## So that

I have an observable, testable resolution path (independent of any third-party MCP server) to confirm which culture clio will apply to created entities

---

## Acceptance Criteria

- [ ] **AC-01 (signal)** — Given a connected environment with profile culture `uk-UA`, when `clio get-user-culture -e <env>` runs, then it prints the resolved culture (`uk-UA`) and exits zero.
- [ ] **AC-ERR** — Given an invalid environment or a `Failed` resolution (no override available — this verb has none), when the command runs, then clio prints `Error: {user-friendly message}` (no stack trace) and exits non-zero.
- [ ] **AC-ALIAS** — Given the alias `profile-language`, when invoked, then it behaves identically to `get-user-culture`.

## Implementation Notes

Pattern to follow: `Command<TOptions>` + constructor-injected services (reference: `SkillCommands.cs`). No MediatR.

Files to create:
- `clio/Command/GetUserCultureCommand.cs` — verb `get-user-culture` (alias `profile-language`). Options class `GetUserCultureCommandOptions` derives from `RemoteCommandOptions` (standard `--environment/-e`). Inject `ICurrentUserCultureResolverFactory`; in `Execute`, resolve `EnvironmentSettings`, `factory.Create(settings)`, then call `ResolveAsync(...).GetAwaiter().GetResult()` (the only sync bridge — M-6). On `Resolved`, print the culture; on `Failed`, print `Error: {message}` and return non-zero.
- `clio.tests/Command/GetUserCultureCommandTests.cs` — `BaseCommandTests<GetUserCultureCommandOptions>`.

Files to modify:
- `clio/BindingsModule.cs` — register `GetUserCultureCommand`.
- `clio/Program.cs` — wire the `get-user-culture` verb.
- `clio/help/en/get-user-culture.txt`, `clio/docs/commands/get-user-culture.md`, `clio/Commands.md` — new verb docs (FR-11). Use the `document-command` skill.

The verb itself has NO `--caption-culture` override, so a `Failed` resolution is always a hard `Error:` + non-zero (M-4 hard-abort path).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | prints resolved culture and exits zero on `Resolved`; prints `Error:` + non-zero on `Failed`; alias resolves to same command | `clio.tests/Command/GetUserCultureCommandTests.cs` |

Use `BaseCommandTests<GetUserCultureCommandOptions>`; do NOT add `[Category("UnitTests")]`. Register the `ICurrentUserCultureResolverFactory` substitute in `AdditionalRegistrations`; resolve the SUT from the container; `ClearReceivedCalls` in teardown. AAA + `because` + `[Description]`.
Test naming: `Execute_ShouldPrintResolvedCulture_WhenResolutionSucceeds`, `Execute_ShouldPrintErrorAndReturnNonZero_WhenResolutionFails`.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004)
- [ ] Verb name + alias are kebab-case (`get-user-culture`, `profile-language`)
- [ ] No MediatR; `Command<TOptions>` + constructor-injected resolver factory; registered in `BindingsModule.cs` and wired in `Program.cs`
- [ ] Error message is user-friendly `Error: {message}`; non-zero exit on failure (AC-ERR)
- [ ] Docs updated: `help/en/get-user-culture.txt`, `docs/commands/get-user-culture.md`, `Commands.md` (FR-11)
- [ ] Unit tests added with `[Category("Unit")]` via `BaseCommandTests<TOptions>`
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
