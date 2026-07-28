# Story 2: `set-user-theme` MCP tool + auto-apply step in theming guidance

**Feature**: set-user-theme
**Jira**: ENG-93302
**FR coverage**: FR-3, FR-4
**SPEC**: [spec-set-user-theme.md](../prd/spec-set-user-theme.md)
**ADR**: [adr-theming.md](../adr/adr-theming.md) (D-D6)
**Status**: ready-for-dev
**Size**: S
**Depends on**: story-set-user-theme-1

## As a
coding agent driving the branding flow through the clio MCP server

## I want
a `set-user-theme` MCP tool and guidance that tells me to call it right after a successful no-code `create-theme`

## So that
the ENG-93302 acceptance criterion holds: the theme is applied to the current user automatically, and the user only refreshes the page

## Design
- `clio/Command/McpServer/Tools/SetUserThemeTool.cs` patterned on the existing
  theme tools (e.g. `CreateThemeTool`): environment-aware `BaseTool` execution
  pattern, same argument surface as the command (`theme`, `reset`), classified as
  a state-changing write **without** an extra confirmation gate (affects only the
  authenticated account — unlike the global `DefaultTheme`, which keeps its
  confirmation per ADR D-D6).
- Resident vs long-tail: follow the existing theme tools' membership in
  `McpCoreToolProfile` (long-tail expected); no `McpToolCompatibilityCatalog`
  entry needed (new tool, no rename).
- `ThemingGuidanceResource.cs` (`docs://mcp/guides/theming`): in the no-code
  flow, add the apply step after `create-theme` — call `set-user-theme` with the
  new theme **by default**, then tell the user to refresh; **skip the apply step
  when the user's request indicates they don't want to switch** (create-only,
  preparing themes for others) and tell them how to apply later. Keep the global
  `DefaultTheme` section separate and confirmation-gated. Mention `--reset` and
  the three server gates (license / operation / `ChangeTheme` feature). State
  explicitly that no session-refresh/workplace-cache calls are needed (spec FR-7).
- Per MCP policy: review `GuidanceCatalog` trigger lines and the theme tools'
  `[Description]` pointers; update the routing map only if a guide is
  added/renamed (none is).
- ClioRing gate: inspect `clio-ring/ClioRing.Ipc`, `clio-ring/ClioRing`,
  `clio-ring/ClioRing.Desktop/actions.json` for consumption of theme tools;
  expected statement: "ClioRing compatibility reviewed, no Ring-consumed contract
  changed" (additive tool), cited in the PR.

## Acceptance Criteria
- [ ] AC-01 — MCP tool `set-user-theme` exists, maps 1:1 to the command surface, and executes environment-aware.
- [ ] AC-02 — Tool description carries the guidance trigger pointer consistent with the other theme tools.
- [ ] AC-03 — `docs://mcp/guides/theming` instructs auto-apply after no-code `create-theme` and keeps global `DefaultTheme` confirmation-gated.
- [ ] AC-04 — `WorkspaceTemplateGuidanceDriftTests` (resident-or-bridged oracle) stays green; no shipped template names the tool imperatively without the bridge.
- [ ] AC-05 — ClioRing gate statement present in the change summary/PR.

## Tests
`clio.tests/Command/McpServer/`: tool→command mapping, argument surface, destructive classification, guidance-resource content assertions (auto-apply step present; DefaultTheme still gated). Validated with: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer"`.
