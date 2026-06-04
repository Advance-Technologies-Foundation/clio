# Story 2 — new-theme CLI command (Contour A scaffold)

| Field | Value |
|-------|-------|
| Issue | ENG-89624 |
| Status | ready-for-dev |
| Depends on | Story 1 |

## Goal

`clio new-theme` scaffolds a theme into a workspace package using the shared core.

## Scope

- `IThemeCreator` / `ThemeCreator` in `clio/Package/` (mirrors `UiProjectCreator`, simpler —
  no esproj/solution). Writes `packages/<package>/Files/themes/<cssClassName>/{theme.json,theme.css}`.
  Package handling like `new-ui-project` (reuse if present, create if missing).
- `CreateThemeOptions` (`[Verb("new-theme")]`, NO `create-theme` alias):
  `cssClassName` (`[Value(0)]`), `--package` (required), `--caption` (optional),
  `--id` (optional). All option long-names kebab-case.
- `CreateThemeOptionsValidator` (FluentValidation) mirroring core validation.
- `CreateThemeCommand : Command<CreateThemeOptions>`.
- Registration: type in `Program.cs` verb-types array + switch case; DI in `BindingsModule.cs`.
- Docs: `clio/help/en/new-theme.txt`, `clio/docs/commands/new-theme.md`, `clio/Commands.md`.

## Acceptance criteria

- AC1: `clio new-theme my-brand-theme --package UsrThemes` creates the two files at the
  expected path; `theme.css` scoped under `.my-brand-theme`; `theme.json` =
  `{id:<uuid>, caption:"My Brand", cssClassName:"my-brand-theme"}`.
- AC2: `--caption`/`--id` override derived values.
- AC3: invalid `cssClassName`/`--id`/missing `--package` produce clear validation errors,
  non-zero exit.
- AC4: missing package is created; existing package is reused.

## Definition of Done

- Integration test scaffolds into a temp workspace and asserts file paths + content.
- Unit tests for options validation (`BaseCommandTests<CreateThemeOptions>` where applicable).
- Command docs updated (3 targets); `Commands.md` index entry added.
- No new `CLIO*` warnings; `[Category]`, AAA + `because` + `[Description]` on tests.
