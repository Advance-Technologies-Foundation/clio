# Story 1 — Shared core: ThemeArtifactBuilder + baseline templates

| Field | Value |
|-------|-------|
| Issue | ENG-89624 |
| Status | ready-for-dev |
| Depends on | — |

## Goal

Pure-logic core that derives theme identifiers, generates `theme.json` + `theme.css`
from the canonical baseline, and validates inputs. Shared by both contours.

## Scope

- `IThemeArtifactBuilder` / `ThemeArtifactBuilder` in `clio/Package/` (no I/O, no environment).
- `clio/tpl/themes/theme.json.tpl` + `theme.css.tpl` with placeholders `<%themeId%>`,
  `<%themeCaption%>`, `<%themeCssClass%>` (baseline from `spec/theme/theme-css-baseline.md`).
- Methods: `DeriveIdentifiers(cssClassName, caption?, id?)`, `BuildThemeJson(ids)`,
  `BuildThemeCss(ids)`, `Validate(ids)`.

## Acceptance criteria

- AC1: `DeriveIdentifiers("acme-dark-theme", null, null)` → `caption="Acme Dark"`,
  `cssClassName="acme-dark-theme"`, `id` = a new UUID v4.
- AC2: explicit `caption`/`id` override the derived values.
- AC3: `Validate` rejects `cssClassName` not matching `^[A-Za-z][A-Za-z0-9_-]*$` or >100,
  `id` not matching `^[A-Za-z0-9_-]+$` or >100, `caption` empty or >250.
- AC4: `BuildThemeCss` returns the baseline scoped under `.<cssClassName>`, default font
  Montserrat; `BuildThemeJson` returns exactly `{id, caption, cssClassName}`.

## Definition of Done

- Interface documented with XML comments; class registered in DI via the interface.
- Unit tests (`[Category("Unit")]`, `Module=Package`) cover derive/validate/build, AAA +
  `because` + `[Description]`, cross-OS.
- No new `CLIO*` warnings.
