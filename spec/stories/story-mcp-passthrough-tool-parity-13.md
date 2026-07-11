# Story 13: `get-component-info` — guard the mixed-input path only (matrix tool, already header-only compliant)

**Feature**: mcp-passthrough-tool-parity
**FR coverage**: FR-03, FR-10
**PRD**: [prd-mcp-passthrough-tool-parity.md](../prd/prd-mcp-passthrough-tool-parity.md)
**ADR**: [adr-mcp-passthrough-tool-parity.md](../adr/adr-mcp-passthrough-tool-parity.md)
**Status**: ready-for-dev
**Size**: S (< 2h)

---

## As a

CI pipeline author (AI-Platform gateway operator)

## I want

`get-component-info`'s (`ComponentInfoTool`) **mixed-input** path (header + explicit `environment-name`/
`uri`) to stop probing the named registered tenant with stored credentials

## So that

the one matrix tool that is **already compliant** on its header-only path does not regress, while its
one genuinely non-compliant path (mixed input) is closed

---

## Acceptance Criteria

- [x] **AC-01** — **Do not regress the compliant path.** Given authorized passthrough header-only (neither
  `environment-name` nor `uri`), when `get-component-info` runs, then it continues to return
  `CreateNoActiveEnvironmentFallback` with the loud `latest-fallback` flag exactly as it does today
  (`ComponentInfoTool.cs:267`, proven by the existing `ComponentInfoToolTests.cs:606`) — this story must
  **not** touch this branch. (PRD explicitly warns: "do not fix it into a regression.") Verified: the
  no-environment branch of `ResolveVersionAsync` was not touched, its original test passes unchanged, and a
  new regression-guard test (`ComponentInfoTool_Should_NeverCallCommandResolver_WhenHeaderOnly`) asserts
  `IToolCommandResolver` is never invoked on this path.
- [x] **AC-02** (PRD AC-04, decision-matrix "mixed-input must be guarded") — Given authorized passthrough
  with `hasEnvironment` true (env-name OR uri supplied, today `:172`), when `get-component-info` runs, then
  `ResolveEnvironmentSettings` uses `commandResolver.Resolve<EnvironmentSettings>(...)` instead of the root
  `GetEnvironment` call (today `:261,279`) — it must **never** probe the named registered tenant with stored
  credentials under passthrough. Verified: `ISettingsRepository` was removed from `ComponentInfoTool`'s
  constructor and replaced with `IToolCommandResolver`; both the `environment-name` and the `uri`-only
  spellings of `hasEnvironment` now route through `commandResolver.Resolve<EnvironmentSettings>`.
- [x] **AC-03** — **Mixed input (PRD AC-06).** Given authorized passthrough with both the header and an
  explicit `environment-name` naming a different registered environment, when `get-component-info` runs,
  then it is rejected by `HasExplicitCredentialArgs` before any named-tenant lookup — it never uses the named
  environment's stored credentials. Verified at the tool level: `ComponentInfoTool_Should_RejectMixedInput_BeforeNamedTenantProbe`
  simulates the resolver's existing transport-policy rejection and asserts the platform-version resolver
  factory is never invoked (no probe reaches the named tenant). The `HasExplicitCredentialArgs` guard itself
  lives in `ToolCommandResolver` (ENG-93208, already covered by `ToolCommandResolver*Tests`) and is reused
  unchanged — this story does not duplicate that coverage.
- [x] **AC-04** (PRD AC-09 / SM-03) — Given stdio or registered-environment `mcp-http`, when
  `get-component-info` is called with `environment-name`/`uri`, then behavior matches the pre-change baseline
  exactly. Verified: `ComponentInfoTool_Should_Resolve_Version_From_Passed_Environment` (extended) and
  `ComponentInfoTool_Should_Resolve_Version_From_Passed_Uri` (new) show the registered-environment/explicit-uri
  probe still resolves and reports the `environment` tier exactly as before, now via `commandResolver`.
- [x] **AC-ERR** — **Error semantics respect the ENG-93208 middleware boundary (PRD AC-ERR).**
  (a) A **malformed/unusable** header is out of scope: the middleware returns HTTP 400 **before** any tool
  is invoked (`McpHttpServerCommand.cs:317`) — the tool is never entered and must not add handling for it.
  Not touched by this story. (b) Given a **valid** header whose mixed-input resolution fails, when the tool
  executes, then it returns the typed error envelope (or the documented fallback flag, per the sibling matrix
  tools' fail-soft shape) with `SensitiveErrorTextRedactor`-redacted text. The tool correctly delegates to the
  shared `SensitiveErrorTextRedactor` (no new catch was added; the existing outer try/catch in
  `GetComponentInfo` already redacts via `SensitiveErrorTextRedactor.Redact(ex.Message)`), and
  `ComponentInfoTool_Should_RejectMixedInput_BeforeNamedTenantProbe` exercises the envelope shape on this
  path — but that test's `EnvironmentResolutionException` message carries no secret, so this story has NOT
  independently re-proven redactor correctness with a secret-bearing message; redactor correctness is shared
  infrastructure (`SensitiveErrorTextRedactor`) outside this story's file scope and is exercised elsewhere
  (e.g. `ApplicationGetInfoToolPassthroughTests`).

## Implementation Notes

Pattern-A swap, scoped **only** to the `hasEnvironment` branch of `ResolveEnvironmentSettings`
(`ComponentInfoTool.cs:261,279`). The header-only, no-environment branch (`CreateNoActiveEnvironmentFallback`,
`:267`) is out of scope for this story — leave it untouched.

```csharp
// hasEnvironment branch — before: root settingsRepository.GetEnvironment(...)  (:261,279)
// after:
EnvironmentSettings settings = commandResolver.Resolve<EnvironmentSettings>(options);
```

Key file: `clio/Command/McpServer/Tools/ComponentInfoTool.cs`.
Pattern to follow: Story 11 (`update-page`) Pattern-A swap — same shape, but only touching the
environment-supplied branch here.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | **Regression test**: header-only stays on `CreateNoActiveEnvironmentFallback`/`latest-fallback` unchanged; mixed-input (env-name or uri present) resolves against header tenant or is rejected before any named-tenant probe; registered-env/stdio unchanged | `ComponentInfoToolTests.cs` (extend — do not remove `:606`'s existing coverage) |
| Integration `[Category("Integration")]` | none required | — |
| E2E `[Category("E2E")]` | Owned by **Story 15**: two-tenant isolation for the newly routed mixed-input path + stdio/`-e` no-regression. Manual only — MCP e2e not in CI | `clio.mcp.e2e/` (Story 15) |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [x] All `CLIO*` diagnostics clean in changed files — including CLIO005 (FR-10)
- [x] Targeted tests green before commit: `dotnet test clio.tests/clio.tests.csproj --filter
  "Category=Unit&Module=McpServer" --no-build` (ADR slice 9) — final run: **0 failed, 2115 passed, 1
  skipped, 2116 total** (see Dev Agent Record for the intermediate-run history while sibling stories were
  mid-edit in this shared worktree).
- [x] All new CLI flags are kebab-case (no new CLI flags in this story)
- [x] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]` (fixture-level
  `[Category("Unit")]` on `ComponentInfoToolTests`, unchanged)
- [x] Existing `ComponentInfoToolTests.cs:606` (header-only compliance,
  `ComponentInfoTool_Should_Emit_Version_Warning_On_Latest_Fallback`) passes unchanged
- [ ] PR description references this story file (no PR opened in this session — left to the architect/
  integration step)

## Dev Agent Record

- Implementation started: 2026-07-11
- Implementation completed: 2026-07-11
- Tests passing:
  - `dotnet test clio.tests/clio.tests.csproj -c Release -f net10.0 --filter
    "FullyQualifiedName~ComponentInfoToolTests" --no-build` → **74/74 passed, 0 failed** (isolated,
    clean) — the load-bearing evidence for this story: it proves every test in this story's own file is
    green regardless of the shared worktree's churn.
  - `dotnet test clio.tests/clio.tests.csproj -c Release -f net10.0 --filter
    "Category=Unit&Module=McpServer" --no-build` → run four times across the session while 4 sibling
    coder agents (stories 10/11/12/14) were concurrently editing `GetUserCultureTool.cs`,
    `PageUpdateTool.cs`, `PageSyncTool.cs`, `BuildThemeTool.cs`/`BuildThemeCommand.cs` in this SAME
    worktree. Interim runs showed 1-4 transient failures, all by name in sibling stories' own new test
    files (e.g. `GetUserCultureToolPassthroughTests.GetUserCulture_ShouldReturnRedactedFailure_WhenHeaderTenantOperationFails`),
    never in `ComponentInfoToolTests.cs`. **Final run, after the sibling stories settled: 0 failed, 2115
    passed, 1 skipped, 2116 total** — fully green.
- Notes:
  - Swapped `ComponentInfoTool`'s constructor dependency from `ISettingsRepository settingsRepository` to
    `IToolCommandResolver commandResolver` (the field had exactly one call site: the `hasEnvironment`
    branch of `ResolveEnvironmentSettings`). No other member of the class referenced
    `settingsRepository`, so no dead dependency was left behind and no `[ResolvedDynamically]` /
    CLIO005 suppression was needed.
  - `IToolCommandResolver` is already registered in `BindingsModule.cs`
    (`services.AddTransient<IToolCommandResolver, ToolCommandResolver>()`), so no DI wiring changes were
    required beyond the constructor-parameter swap.
  - The no-environment (`CreateNoActiveEnvironmentFallback`) branch of `ResolveVersionAsync` was not
    touched, per the story's explicit boundary.
  - Added 3 new tests (uri-only hasEnvironment routing, mixed-input rejection before the platform-version
    probe, and a header-only regression guard asserting `IToolCommandResolver` is never called) plus
    updated the shared `BuildTool` test helper and one existing test
    (`ComponentInfoTool_Should_Resolve_Version_From_Passed_Environment`) to substitute
    `IToolCommandResolver` instead of `ISettingsRepository`. The cited `:606` test's body/assertions were
    left byte-for-byte unchanged.
  - MCP surface (`[Description]`, docs, `Commands.md`, `help/en/get-component-info.txt`,
    `docs/commands/get-component-info.md`) reviewed — no update required. This is a pure internal
    routing/plumbing fix: no argument, requiredness, response-shape, or safety-flag change. The MCP
    argument contract for `environment-name` was already non-`[Required]` before this story (confirmed by
    inspection), so the ADR's "remove `[Required]`" note for resolver-routed tools required no action here.
  - Deviation from the story's cited line numbers: at the time of implementation the `hasEnvironment`
    branch was at `ResolveVersionAsync` (`:261`) calling `ResolveEnvironmentSettings` (`:279-287`), a
    handful of lines off the story's `:261,279` — same method shape, no behavior drift, confirmed by
    reading the file before editing.
