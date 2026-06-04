# ADR — Creatio Theme Management in clio

| Field | Value |
|-------|-------|
| Issue | ENG-89624 |
| Status | accepted |
| Date | 2026-06-03 |
| Phase | BMAD Phase 2 — Design |
| Supersedes | — |

## Context

A Creatio Freedom UI custom theme is a **file artifact inside a package**, not a DB
entity:

```
<package>/Files/themes/<cssClassName>/
    theme.json   →  { id, caption, cssClassName }   (exactly these 3 fields)
    theme.css    →  .<cssClassName> { --crt-* semantic tokens + typography + palettes }
<package>/Files/fonts/<fontCode>/                    (optional, local fonts)
    <fontCode>.css  (@font-face) + .woff files
```

Two consumers need theme operations:

1. **Professional developers** work through a clio **workspace**, version-control the
   package and deliver with `push-workspace`. This is the same shape as `new-ui-project`.
2. **No-code / vibecoding via ADAC/CAADT** (`C:\DATA\projects\ai-driven-app-creation`) is
   **DB-first / remote-first** over clio MCP: it has no workspace and never pushes. For
   themes it uses the native Creatio **`ThemeService.svc`** (`GetAvailableThemes`,
   `CreateTheme`, `UpdateTheme`, `DeleteTheme`).

Two key platform facts that shape the design:

- `ThemeService` identifies a theme by the `id` inside `theme.json`; the **folder name is
  irrelevant** to it. So Contour A is free to name the theme folder from `cssClassName`.
- `ThemeService` invalidates its theme cache itself. Contour A writes files directly
  (bypassing `ThemeService`), so it must invalidate the cache after delivery; locally
  this is `HDEL Workspace 0:AvailableThemes 0:SyncItem:AvailableThemes`.

## Decision

### 1. One shared core + two contours

A pure-logic core, `ThemeArtifactBuilder`, owns identifier derivation, baseline
`cssContent` generation, and validation. Both contours consume it:

- **Contour A (this iteration):** `new-theme` command + MCP tool → write files into the
  workspace package → deliver via existing `push-workspace` + `clear-redis-db`.
- **Contour B (later):** `create/update/delete/list-themes` MCP tools → call
  `ThemeService.svc` with the `cssContent` produced by the same core.

The CSS template and token rules live **only** in the core/template, never duplicated.

### 2. Naming convention

Follows clio's de-facto split (`new-*` = local workspace scaffold; `create/update/
delete/list-*` = server-side API):

| Contour | Command(s) | Analogue |
|---------|-----------|----------|
| A — workspace | `new-theme` | `new-ui-project`, `new-pkg` |
| B — server | `create-theme` / `update-theme` / `delete-theme` / `list-themes` | `create-page` / `update-page` / `list-pages` |

`new-theme` gets **no** `create-theme` alias (avoids collision with Contour B), even
though `new-ui-project` happens to alias `create-ui-project`.

### 3. Artifact + identifiers

- Path: `Files/themes/<cssClassName>/` (no `src/`). Fonts: `Files/fonts/<fontCode>/`,
  imported from `theme.css` via `@import '../../fonts/<fontCode>/<fontCode>.css'`.
- `theme.json` = `{id, caption, cssClassName}`.
- Derivation: `caption` = Title Case of `cssClassName`
  (overridable by `--caption`); `id` = UUID v4 (overridable by `--id`; explicit id must
  match `^[A-Za-z0-9_-]+$`, ≤100). `cssClassName` must match `^[A-Za-z][A-Za-z0-9_-]*$`, ≤100.
- Create is **template-only**: full canonical baseline, default font `Montserrat`.
  Colour/token/font customization is a **separate** operation (Contour A: the agent edits
  files per guidance; Contour B: `update-theme`).

### 4. CSS token contract

The baseline `theme.css` is scoped under `.<cssClassName>` and contains semantic colours
(background/border/text/icon × base+role), typography mapping, and full palettes
(primary/secondary/accent/neutral/error/success, shades 10–900). It references — but must
**not** redefine — the platform `:root` primitives (`--crt-radius-*`, `--crt-spacing-*`,
`--crt-font-size-*`, `--crt-line-height-*`, `--crt-font-weight-*`, base colours, glass).
The canonical baseline + primitives list is captured in `spec/theme/theme-css-baseline.md`.

### 5. No template versioning

Themes are supported only on Creatio 10.x+, so a single baseline lives flat under
`clio/tpl/themes/` (no `<version>` split, no `--version` option) — unlike `tpl/ui`.

### 6. Implementation pattern

Mirror the `new-ui-project` stack, simpler (no esproj/solution integration):

- `IThemeArtifactBuilder` / `ThemeArtifactBuilder` — `clio/Package/` (pure logic).
- `clio/tpl/themes/theme.json.tpl` + `theme.css.tpl`, placeholders `<%themeId%>`,
  `<%themeCaption%>`, `<%themeCssClass%>`; delivered through
  `TemplateProvider.CopyTemplateFolder(macros)` (macro substitution in folder names too).
- `IThemeCreator` / `ThemeCreator` — `clio/Package/`, writes into the workspace package.
- `CreateThemeOptions` (`[Verb("new-theme")]`) + `CreateThemeOptionsValidator`
  (FluentValidation) + `CreateThemeCommand : Command<CreateThemeOptions>`.
  Registered in `Program.cs` (type list + switch) and `BindingsModule.cs` (DI).
- `CreateThemeTool : BaseTool<CreateThemeOptions>` — MCP `new-theme`, mirrors
  `CreateUiProjectTool` (pin workspace dir, `InternalExecute<CreateThemeCommand>`).
- `CreatioThemeGuidanceResource` registered in `GuidanceCatalog`.

## Alternatives considered

- **cliogate file-content API for Contour B** (`SavePackageFileContent`/`UploadFile`/
  `DeleteFile`). Rejected: the native `ThemeService` is purpose-built, invalidates cache,
  and is the channel ADAC actually uses. (cliogate `UploadFile` may still be the only
  path for *local* fonts in a future Contour B variant.)
- **Pinpoint Redis `HDEL`** vs full `clear-redis-db`. Chose full flush for MVP (no new
  code); pinpoint `HDEL` (or a cliogate `ClearThemesCache` endpoint) is a later refinement.
- **Single command with `--workspace/--remote` mode** (like `delete-schema`). Rejected:
  mechanisms differ sharply (files+push vs API); separate `new-*` / `create-*` names are
  clearer for both humans and the agent.
- **Template versioning by Creatio version** (like `tpl/ui`). Rejected: themes are 10.x+ only.

## Consequences

- Contour A is fully self-contained (files + existing delivery commands), testable
  without `ThemeService`.
- The guidance article becomes the center of gravity: it teaches the agent to detect its
  mode and, in workspace mode, to perform update/delete/rename/colour edits directly.
- A↔B compatibility holds because both keep the same `id` in `theme.json`.
- Touching command classes triggers the repo MCP + docs review policy (AGENTS.md).

## Open items

- Confirm on a live 10.x environment that static theme files are picked up after
  `push-workspace` + `clear-redis-db` (no compile needed).
- Contour B design (ThemeService tools, local-font handling) is a separate ADR/iteration.
