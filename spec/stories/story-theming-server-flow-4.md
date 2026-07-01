# Story 4: create / update / delete-theme env-aware MCP tools

**Feature**: theming-server-flow (ENG-91387 — Theming with AI, Toolkit / no-code server flow, Contour B)
**FR coverage**: FR-11, FR-12, FR-14, FR-18
**PRD**: [prd-theming-server-flow.md](../prd/prd-theming-server-flow.md)
**ADR**: [adr-theming-server-flow.md](../adr/adr-theming-server-flow.md)
**Status**: ready-for-dev
**Size**: L (full day)

> **Depends on Stories 2 + 3** (the three commands and the non-logging `CreateThemeCommand.TryCreateTheme`).
> Three `BaseTool<TOptions>` MCP tools, each exposing `-by-environment` + `-by-credentials`, executed via
> the env-aware path. The result shape is **asymmetric by design** (D5): create returns structured
> `CreateThemeResult { success, id, error? }` (the agent must learn the generated id); update/delete use the
> `CommandExecutionResult` log envelope. Mandatory `clio.mcp.e2e` for all six variants (NOT in CI — manual).

---

## As a

AI coding agent (vibe-coder) driving the Creatio AI Toolkit over MCP

## I want

`create-theme`, `update-theme`, and `delete-theme` MCP tools — each in the two standard connection-mode variants
(`-by-environment` / `-by-credentials`)

## So that

I can run the full no-code theme lifecycle (create → restyle → delete) from the agent flow against the correct
per-request environment, and read back the created theme id without shelling out to the CLI

---

## Acceptance Criteria

- [ ] **AC-01** — Given each tool class, when inspected, then it derives from `BaseTool<TOptions>`
  (`CreateThemeOptions` / `UpdateThemeOptions` / `DeleteThemeOptions`), carries `[McpServerToolType]`
  (inherited), and exposes the two connection-mode names (kebab-case): `create-theme-by-environment` /
  `create-theme-by-credentials`, `update-theme-by-environment` / `update-theme-by-credentials`,
  `delete-theme-by-environment` / `delete-theme-by-credentials`. (FR-11, AC-10.)
- [ ] **AC-02** — Given the create tool, when it runs, then it resolves a fresh `CreateThemeCommand` for the
  per-call environment (`ResolveCommand<CreateThemeCommand>` via `ExecuteWithCleanLog`), invokes the
  non-logging `TryCreateTheme`, and returns **structured** `CreateThemeResult { success, id, error? }` — `id`
  is the effective theme id (auto-generated when `--id` omitted), so the agent can chain update/delete/set-default
  without a `list-themes` round-trip. No command console output leaks onto the JSON-RPC channel (CM-02 — the
  data method is silent). (FR-11, D5, R-01.)
- [ ] **AC-03** — Given the update and delete tools, when they run, then each executes via the env-aware
  `InternalExecute<UpdateThemeCommand>` / `InternalExecute<DeleteThemeCommand>` log envelope and returns a
  `CommandExecutionResult` (id is caller-supplied — no generated value to surface). A `success:false` from the
  command surfaces as an `ErrorMessage` in the execution-log envelope with a non-zero exit code (R-07, R-09
  AC-ERR split). (FR-11, D5.)
- [ ] **AC-04** — Given the tools take inline CSS only, when their arguments are inspected, then create/update
  accept `cssContent` inline (no `--css-content-file` equivalent — A-06); the MCP path enforces the **identical**
  FR-10 limits as the CLI (incl. the 1 MiB `cssContent` cap) because validation lives in the command/data method
  (`ThemeRequestBuilder`), not the options layer (R-04). (FR-11, A-06.)
- [ ] **AC-05** — Given the tool metadata, when inspected, then safety flags are: create = `ReadOnly=false`,
  `Destructive=false`, `Idempotent=false`; update = `ReadOnly=false`, `Destructive=false`, `Idempotent=true`;
  delete = `ReadOnly=false`, `Destructive=true`, `Idempotent=false`; **`OpenWorld=false` on all three**. Each tool
  description routes the agent to `get-guidance theming`. (FR-12, AC-10.)
- [ ] **AC-06** — Given the options derive from `RemoteCommandOptions : EnvironmentOptions` (always
  environment-bound), when resolved in `BaseTool`, then they fall through the **generic `EnvironmentOptions`
  arm** of `BaseTool.ResolveFromCallContainer` — **no switch edit and no theme arm is added** (verified by a unit
  test asserting env-aware resolution; do not add a `ResolveWithoutEnvironment` arm — these are never local-only).
  (ADR D5, pre-impl checklist.)
- [ ] **AC-07** — Given the MCP server starts, when tools are registered, then all three tool types are
  registered via assembly scan through `McpFeatureToggleFilter.RegisterEnabledPrimitives` (the `IEnumerable<Type>`
  seam) — **no** `Type[]` overload and **no** `*FromAssembly` (RR-04 — either silently registers nothing). There
  is no manual tool list to append to; `[FeatureToggle("theming")]` on each tool class (added later — native-build consolidation, ADR D1 SUPERSEDED; originally shipped enabled). (FR-14.)
- [ ] **AC-08** — Given the by-credentials variants, when invoked with bad/missing credentials, then create
  validates via inline `CreateThemeResult.Failure(...)` guards (like `ListThemesTool`) and update/delete via
  `CommandExecutionResult.ValidateCredentials`, returning a graceful failure result — not an unhandled exception.
  (FR-11, D5.)
- [ ] **AC-ERR** — Given an MCP call with no resolvable environment or an empty required argument, when any tool
  runs, then it returns a graceful failure — create → `{ success:false, error }`; update/delete → `ErrorMessage`
  in `execution-log-messages` + non-zero `exit-code` — not an unhandled exception. (R-09 AC-ERR surface split.)

## Implementation Notes

Three `BaseTool<TOptions>` tools. The CLI commands and `CreateThemeCommand.TryCreateTheme` come from Stories 2/3
— this story only adds the MCP surface. **Review MCP artifacts per the AGENTS.md MCP maintenance policy; use the
`create-mcp-tool` skill for the tools and `test-mcp-tool` for the tests.**

**Key file (create): `clio/Command/McpServer/Tools/CreateThemeTool.cs`** (ADR D5, FR-12)
```csharp
[McpServerTool(Name = "create-theme-by-environment", ReadOnly = false, Destructive = false,
   Idempotent = false, OpenWorld = false),
 Description("Create a custom Creatio theme on a registered environment. Returns { success, id, error? } — " +
   "id is the created theme id (auto-generated when omitted). For the theme workflow, read get-guidance theming first.")]
public CreateThemeResult CreateThemeByName(
    [Required] string environmentName, [Required] string cssClassName, [Required] string cssContent,
    string caption = null, string id = null, string packageName = null) { /* ExecuteWithCleanLog + ResolveCommand<CreateThemeCommand>.TryCreateTheme; caption derived from cssClassName when omitted */ }
// + create-theme-by-credentials variant (inline CreateThemeResult.Failure(...) credential guard, like ListThemesTool)
// CreateThemeResult { bool success; string id; string error; } record (mirrors ListThemesResult)
```

**Key file (create): `clio/Command/McpServer/Tools/UpdateThemeTool.cs`** (ADR D5, FR-12)
- `BaseTool<UpdateThemeOptions>`; `update-theme-by-environment` / `-by-credentials`; `InternalExecute<UpdateThemeCommand>`
  (log envelope → `CommandExecutionResult`); flags `ReadOnly=false`/`Destructive=false`/`Idempotent=true`,
  `OpenWorld=false`; inline `cssContent` only (no file arg); description → `get-guidance theming`;
  by-credentials via `CommandExecutionResult.ValidateCredentials`.

**Key file (create): `clio/Command/McpServer/Tools/DeleteThemeTool.cs`** (ADR D5, FR-12)
- `BaseTool<DeleteThemeOptions>`; `delete-theme-by-environment` / `-by-credentials`; `InternalExecute<DeleteThemeCommand>`;
  flags `ReadOnly=false`/`Destructive=true`/`Idempotent=false`, `OpenWorld=false`; `{ id }` only; description →
  `get-guidance theming`.

**Registration:** gated MCP types flow through `McpFeatureToggleFilter.RegisterEnabledPrimitives`, which passes
`IEnumerable<Type>` to the SDK's `WithTools`. Do **NOT** pass a `Type[]` (binds to the generic `WithX<T>(T)`
overload — registers nothing) and do **NOT** revert to `*FromAssembly` (project-context.md + AGENTS.md MCP
caveat, RR-04). These now carry `[FeatureToggle("theming")]` (added later — ADR D1 SUPERSEDED; originally shipped enabled), and register through the same filter seam.

**No `BaseTool.ResolveFromCallContainer` switch edit** (ADR D5): the switch (ll. 110–127) has dedicated arms only
for the four local-resolution options (`CreateTestProjectOptions`, `AddPackageOptions`,
`CreateWorkspaceCommandOptions`, `CreateUiProjectOptions`) plus a generic `EnvironmentOptions` arm.
`Create/Update/DeleteThemeOptions` derive from `RemoteCommandOptions : EnvironmentOptions`, are always
environment-bound, and fall through the generic arm — do **not** add a theme arm. A unit test must assert
env-aware resolution.

Pattern to follow: `ListThemesTool` (structured-result + inline credential guard — model for `CreateThemeTool`);
`ClearThemesCacheTool` (`InternalExecute<TCommand>` log envelope — model for `UpdateThemeTool` / `DeleteThemeTool`);
`clio/Command/McpServer/Tools/BaseTool.cs`; `clio/Command/McpServer/AGENTS.md` (MCP `BaseTool` env-aware rule).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | create: MCP arg mapping → `CreateThemeOptions`; env-aware resolution (generic `EnvironmentOptions` arm, AC-06); structured `CreateThemeResult` carries the (generated) id; safety flags `false/false/false` + `OpenWorld=false`; description → `get-guidance theming`; by-credentials failure guard | `clio.tests/Command/McpServer/CreateThemeToolTests.cs` |
| Unit `[Category("Unit")]` | update: arg mapping; env-aware `InternalExecute<UpdateThemeCommand>`; flags `false/false/true` + `OpenWorld=false`; description → guidance; `ValidateCredentials` failure path | `clio.tests/Command/McpServer/UpdateThemeToolTests.cs` |
| Unit `[Category("Unit")]` | delete: arg mapping; env-aware `InternalExecute<DeleteThemeCommand>`; flags `false/true/false` + `OpenWorld=false`; description → guidance; `ValidateCredentials` failure path | `clio.tests/Command/McpServer/DeleteThemeToolTests.cs` |
| E2E `[Category("E2E")]` (manual, NOT in CI) | the real `clio mcp-server` advertises all **six** variants (`{create,update,delete}-theme-by-{environment,credentials}`) with the FR-12 safety flags (AC-10, R-09); representative create→update→delete round-trip | `clio.mcp.e2e/{Create,Update,Delete}ThemeToolE2ETests.cs` |

- MCP work is **incomplete without `clio.mcp.e2e` coverage** for all six variants — unit mapping tests alone are
  insufficient per AGENTS.md / the `test-mcp-tool` skill. **Flag in the test plan / PR: MCP E2E is NOT in CI yet —
  manual execution only.**
- AC-10 ("advertised by the real `clio mcp-server`") is satisfied **only** by the manual e2e tests; unit tests
  assert the safety-flag attribute values on the tool classes, not the live tool manifest (R-09).
- NSubstitute for unit; `[Category("Unit")]` (never `[Category("UnitTests")]`); naming
  `MethodName_ShouldBehavior_WhenCondition`; AAA + a `because` on every assertion + `[Description]` on every test.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO005)
- [ ] Three tools derive from `BaseTool<TOptions>`; six connection-mode names kebab-case (AC-01)
- [ ] create returns structured `CreateThemeResult { success, id, error? }` via non-logging `TryCreateTheme` (CM-02); update/delete use `InternalExecute` log envelope with `WriteError` on failure (R-07)
- [ ] Validation enforced via the command/data method (`ThemeRequestBuilder`), identical 1 MiB cap on MCP + CLI (R-04)
- [ ] Safety flags per FR-12; `OpenWorld=false` on all three; descriptions route to `get-guidance theming`
- [ ] Tools resolve through the generic `EnvironmentOptions` arm — **no `BaseTool` switch edit** (verified by a unit test, AC-06)
- [ ] Tools register via `McpFeatureToggleFilter.RegisterEnabledPrimitives` (`IEnumerable<Type>`) — no `Type[]`, no `*FromAssembly` (RR-04); `[FeatureToggle("theming")]` added later (ADR D1 SUPERSEDED)
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] `clio.mcp.e2e` coverage added for all **six** variants (flag: not in CI — manual)
- [ ] Targeted tests run: `dotnet test --filter "Category=Unit&Module=McpServer"`
- [ ] PR description references this story file (and states "MCP reviewed" outcome per AGENTS.md MCP policy)

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started:
- Implementation completed:
- Tests passing:
- MCP E2E (manual, six variants) run:
- Notes:
