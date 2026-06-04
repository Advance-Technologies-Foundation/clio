# PRD — Creatio Theme Management in clio

| Field | Value |
|-------|-------|
| Feature | Creatio theme create/edit/delete via clio |
| Issue | ENG-89624 |
| Status | approved |
| Date | 2026-06-03 |
| Phase | BMAD Phase 1 — Requirements |

## Problem

Creatio Freedom UI custom themes are a file artifact inside a package
(`Files/themes/<cssClassName>/theme.json` + `theme.css`). Today clio has **no**
support for creating or editing them — neither for professional developers working
through a clio workspace, nor for the no-code / vibecoding flow driven by ADAC/CAADT
over clio MCP. Authors currently hand-write the files and the orchestration by prompt.

## Goals

Give clio first-class theme support across **two delivery contours** that share one
artifact-generation core:

- **Contour A — workspace (professional dev):** scaffold a theme into a workspace
  package, deliver with the existing `push-workspace` + `clear-redis-db`. Mirrors the
  `new-ui-project` flow.
- **Contour B — server API (no-code / ADAC):** create/update/delete/list themes
  directly on an environment through the native `ThemeService.svc`. No workspace.

## Scope of this iteration (ENG-89624)

In scope:

1. Shared core that derives theme identifiers and generates `theme.json` + `theme.css`.
2. Contour A only: the `new-theme` command (CLI) + MCP tool that scaffolds a theme into
   a workspace package.
3. A general agent-facing guidance article describing what a theme is, the token
   contract, fonts, activation, and how an agent chooses its working mode.
4. Tests (unit + integration + MCP e2e) and command documentation.

Out of scope (later iteration):

- Contour B tools (`create-theme` / `update-theme` / `delete-theme` / `list-themes`
  over `ThemeService`).
- Structured `update` / `delete` commands for Contour A — in the workspace the agent
  edits files directly per the guidance; clio only scaffolds and delivers.
- Pinpoint Redis `HDEL` (we use the existing full `clear-redis-db` for MVP).

## Users

- **Professional developer** — works in a clio workspace, version-controls the package,
  pushes to environments. Uses Contour A.
- **No-code author / AI agent (ADAC)** — no workspace, mutates the environment directly.
  Uses Contour B (next iteration). The guidance must let an agent detect which mode it is in.

## Functional requirements

- FR1: `new-theme` accepts `cssClassName` (positional) + required `--package`, optional
  `--caption`, optional `--id`.
- FR2: Identifier derivation — `caption` = Title Case of `cssClassName` when
  `--caption` is absent; `id` = UUID v4 when `--id` is absent
  (an explicit `id` must match `^[A-Za-z0-9_-]+$`, ≤100).
- FR3: `theme.json` holds exactly `{id, caption, cssClassName}`.
- FR4: A new theme is created fully from the canonical baseline template; the default
  font is `Montserrat`. No palette/colour/font parameters at creation time.
- FR5: Files land at `packages/<package>/Files/themes/<cssClassName>/{theme.json,theme.css}`.
- FR6: Package handling matches `new-ui-project` (reuse if present, create if missing).
- FR7: The MCP `new-theme` tool requires an existing workspace (like `new-ui-project`).
- FR8: A general theme guidance article is exposed via `get-guidance` and the resource catalog.

## Acceptance criteria

- AC1: `clio new-theme my-brand-theme --package UsrThemes` creates
  `packages/UsrThemes/Files/themes/my-brand-theme/theme.json` and `theme.css`, with
  `theme.css` scoped under `.my-brand-theme` and `theme.json` = `{id:<uuid>,
  caption:"My Brand", cssClassName:"my-brand-theme"}`.
- AC2: `--caption "Acme Dark"` and `--id AcmeDark` override the derived values.
- AC3: Invalid `cssClassName` (not `^[A-Za-z][A-Za-z0-9_-]*$` or >100) is rejected with a
  clear message; invalid `--id` (not `^[A-Za-z0-9_-]+$` or >100) is rejected.
- AC4: The MCP `new-theme` tool produces the same artifact and refuses a non-workspace path.
- AC5: `get-guidance` returns the theme guidance; it documents the token contract, the
  `:root` primitives that must not be redefined, fonts (local/Google), and activation
  (`push-workspace` + `clear-redis-db`). Workspace-vs-server mode selection is deferred to the
  Contour B iteration (the guide stays Contour-A-only until server tools exist).
- AC6: Themes are supported only on Creatio 10.x+ (no template versioning).

## Non-functional notes

- CLI option names are kebab-case (CLIO001).
- No new `CLIO*` analyzer warnings in added/modified files.
- Tests carry `[Category("Unit"|"Integration"|"E2E")]`, AAA + `because` + `[Description]`.
