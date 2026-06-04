# Story 4 — General theme guidance article

| Field | Value |
|-------|-------|
| Issue | ENG-89624 |
| Status | ready-for-dev |
| Depends on | Story 1 (token contract) |

## Goal

An agent-facing guidance article that explains Creatio themes and how to work with them
across both contours. This is the center of gravity for Contour A edits (which have no
dedicated commands) and the entry point an agent reads to choose its mode.

## Scope

- `CreatioThemeGuidanceResource` in `clio/Command/McpServer/Resources/`, registered in
  `GuidanceCatalog` (name e.g. `creatio-theme`), exposed via `get-guidance` and as an MCP resource.

## Content requirements

- What a theme is; artifact layout (`Files/themes/<cssClassName>/theme.json` + `theme.css`,
  `theme.json` = `{id, caption, cssClassName}`).
- Token contract: scoped `.<cssClassName>`; semantic colours, typography, palettes; the
  `:root` platform primitives that must NOT be redefined (radius/spacing/font-size/
  line-height/font-weight/base/glass) — agent should know they exist and only consume them.
- Fonts: local (`@font-face` stylesheet under `Files/fonts/<code>/` + `@import`) vs Google
  (`@import url(...)`), driving `--crt-font-family-*`. Local fonts are Contour A only.
- Activation: Contour A → `push-workspace` then `clear-redis-db`; Contour B → ThemeService
  invalidates cache itself.
- **Mode selection:** how the agent detects workspace vs server/ADAC mode and picks
  `new-theme` + manual file edits (A) vs `create/update/delete/list-theme` tools (B, later).
- What is safe to change vs strongly discouraged.

## Acceptance criteria

- AC1: `get-guidance` returns the article by its catalog name.
- AC2: article covers token contract, `:root` primitives, fonts, activation, and mode selection.

## Definition of Done

- Registered in `GuidanceCatalog`; MCP e2e asserts discovery + key phrases (per AGENTS.md MCP policy).
- `docs/McpCapabilityMap.md` updated.
