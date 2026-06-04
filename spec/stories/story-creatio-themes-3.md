# Story 3 — new-theme MCP tool (Contour A)

| Field | Value |
|-------|-------|
| Issue | ENG-89624 |
| Status | ready-for-dev |
| Depends on | Story 2 |

## Goal

Expose `new-theme` over MCP so an agent can scaffold a theme into a workspace.

## Scope

- `CreateThemeTool : BaseTool<CreateThemeOptions>` in `clio/Command/McpServer/Tools/`,
  `[McpServerTool(Name="new-theme", ReadOnly=false, Destructive=false, Idempotent=false,
  OpenWorld=false)]`. Mirrors `CreateUiProjectTool`.
- Args record (camelCase JSON): `workspaceDirectory`, `cssClassName`, `packageName`,
  `caption?`, `id?`. Pin process working dir to the workspace; `InternalExecute<CreateThemeCommand>`.
- Validate workspace marker (`.clio/workspaceSettings.json`) like `CreateUiProjectTool`.
- DI registration in `BindingsModule.cs`.

## Acceptance criteria

- AC1: tool produces the same artifact as the CLI command.
- AC2: refuses a non-existent / non-workspace path with a clear error pointing at `create-workspace`.
- AC3: argument mapping (camelCase → options) verified; `[Required]` on args.

## Definition of Done

- Unit tests (`Module=McpServer`): argument mapping, validation, workspace-marker refusal.
- **Mandatory MCP e2e** in `clio.mcp.e2e` exercising `new-theme` through the real
  `clio mcp-server` (per AGENTS.md MCP policy).
- `docs/McpCapabilityMap.md` updated; change summary states MCP review outcome.
- No new `CLIO*` warnings.
