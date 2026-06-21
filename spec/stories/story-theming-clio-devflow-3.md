# Story 3: clear-themes-cache env-aware MCP tool

**Feature**: theming-clio-devflow (ENG-90636 — Theming with AI, Clio dev flow, Contour A)
**Capability coverage**: CAP-02 (theme-cache invalidation as an environment-aware MCP tool)
**SPEC**: [spec-theming-clio-devflow.md](../prd/spec-theming-clio-devflow.md)
**ADR**: [adr-theming-clio-devflow.md](../adr/adr-theming-clio-devflow.md)
**Status**: review
**Size**: M (half day)

---

## As a

coding agent driving the Creatio AI Toolkit over MCP

## I want

a `clear-themes-cache` MCP tool that resolves the target environment, executes the command, and returns a success result

## So that

I can activate a theme change from the agent flow without shelling out to the CLI, and against the correct per-request environment

---

## Acceptance Criteria

- [ ] **AC-01** — Given the MCP tool class, when inspected, then it derives from `BaseTool<ClearThemesCacheOptions>` and executes via the env-aware path `InternalExecute<ClearThemesCacheCommand>(options)` (resolves a fresh command for the current request's environment — not the startup-time instance).
- [ ] **AC-02** — Given an MCP call with an environment argument, when the tool runs, then the command executes against that environment's settings and returns a success result.
- [ ] **AC-03** — Given the tool metadata, when inspected, then safety flags are `ReadOnly=false`, `Destructive=false`, `Idempotent=true`, and the tool exposes the two standard connection-mode names `clear-themes-cache-by-environment` and `clear-themes-cache-by-credentials` (both kebab-case), matching the convention of other environment-sensitive `BaseTool<TOptions>` tools.
- [ ] **AC-04** — Given the MCP server starts, when tools are registered, then `clear-themes-cache` is registered via `McpFeatureToggleFilter.RegisterEnabledPrimitives` (the `IEnumerable<Type>` seam) — **not** a `Type[]` overload, and **not** `*FromAssembly`.
- [ ] **AC-05** — Given a running `clio mcp-server`, when an `clio.mcp.e2e` test invokes the `clear-themes-cache` tool, then the tool resolves the environment and returns a result (manual run — E2E is not in CI).
- [ ] **AC-ERR** — Given an MCP call with no resolvable environment, when the tool runs, then it returns a graceful MCP error response (`success: false` + message), not an unhandled exception.

## Implementation Notes

The CLI command (`ClearThemesCacheCommand` / `ClearThemesCacheOptions`) comes from Story 1 — this story only adds the MCP surface. CAP-02 is independent of the R2 open dependency: the tool execution path is wired even if the backend body (Story 2) is not finalised.

Key file (create): `clio/Command/McpServer/Tools/ClearThemesCacheTool.cs`
- `[McpServerToolType]` class, derive from `BaseTool<ClearThemesCacheOptions>`.
- The tool is **environment-sensitive** (it accepts an environment / URI-login-password), so use `InternalExecute<ClearThemesCacheCommand>(options)` per the MCP `BaseTool` rule (`clio/Command/McpServer/AGENTS.md`) — do NOT execute the injected command directly.
- Tool method = MCP arg mapping + select the env-aware execution path; no duplicated local `InternalExecute`.
- Safety flags: `ReadOnly=false`, `Destructive=false`, `Idempotent=true`.

Registration: gated MCP types flow through `McpFeatureToggleFilter.RegisterEnabledPrimitives`, which passes `IEnumerable<Type>` to the SDK's `WithTools`. Do NOT pass a `Type[]` (binds to the generic `WithX<T>(T)` overload and registers nothing) and do NOT revert to `*FromAssembly` (project-context.md + AGENTS.md MCP caveat). This feature is **not** behind a `[FeatureToggle]` per the ADR (it ships on), so no toggle attribute is added — but it still registers through the same filter seam.

Pattern to follow: existing env-aware `BaseTool<TOptions>` tools that call `InternalExecute<TCommand>` (the MCP tools for other remote commands); `clio/Command/McpServer/Tools/BaseTool.cs`.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | MCP arg mapping -> `ClearThemesCacheOptions`; env-aware `InternalExecute<ClearThemesCacheCommand>` selection; safety flags (`ReadOnly`/`Destructive`/`Idempotent`) | `clio.tests/Command/McpServer/ClearThemesCacheToolTests.cs` |
| E2E `[Category("E2E")]` (manual, NOT in CI) | drive the `clear-themes-cache` tool against a real `clio mcp-server`; assert env resolution + success result | `clio.mcp.e2e/` (new E2E test; extend the harness if needed) |

- MCP work is incomplete without `clio.mcp.e2e` coverage — unit mapping tests alone are insufficient (`test-mcp-tool` skill / AGENTS.md MCP test requirement).
- Use NSubstitute for unit; `[Category("Unit")]`; naming `MethodName_ShouldBehavior_WhenCondition`; AAA + `because` + `[Description]`.
- Flag in the test plan / PR: MCP E2E is NOT in CI yet — manual execution only.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO005)
- [ ] Tool derives from `BaseTool<ClearThemesCacheOptions>` and uses `InternalExecute<ClearThemesCacheCommand>` (env-aware)
- [ ] Safety flags `ReadOnly=false` / `Destructive=false` / `Idempotent=true`; tool name kebab-case
- [ ] Registered via `McpFeatureToggleFilter.RegisterEnabledPrimitives` (`IEnumerable<Type>`) — no `Type[]`, no `*FromAssembly`
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] `clio.mcp.e2e` coverage added for the tool (flag: not in CI — manual)
- [ ] Targeted tests run: `dotnet test --filter "Category=Unit&Module=McpServer"`
- [ ] PR description references this story file

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started:
- Implementation completed:
- Tests passing:
- MCP E2E (manual) run:
- Notes:
