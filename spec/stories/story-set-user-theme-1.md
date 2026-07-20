# Story 1: `set-user-theme` CLI command — apply a theme to the current user's profile

**Feature**: set-user-theme
**Jira**: ENG-93302
**FR coverage**: FR-1, FR-2, FR-5, FR-6
**SPEC**: [spec-set-user-theme.md](../prd/spec-set-user-theme.md)
**ADR**: [adr-theming.md](../adr/adr-theming.md) (D-D6 — amended by story 4)
**Status**: ready-for-dev
**Size**: M
**Depends on**: — (gating: Krylov confirmation of spec §6/§9 before implementation starts)

## As a
no-code creator who just created a custom theme via clio

## I want
`clio set-user-theme <theme>` to apply that theme to my (the authenticated user's) profile

## So that
I only need to refresh the page to see the new theme — no manual profile-page step

## Design

Server contract (verified against Creatio 10 source — spec §3): DataService
`SelectQuery` on virtual entity `SysUserProfile` returns exactly one row (the
current user; `Id` = SysAdminUnit Id); `UpdateQuery` sets column `Theme` to the
theme's **cssClassName** with filter `Id` = that Id. Existing
`ServiceUrlBuilder.KnownRoute.Select`/`.Update` routes are reused — **no new
routes**.

- New service `IUserThemeApplier` / `UserThemeApplier` in `clio/Command/Theming/`,
  registered in `BindingsModule` (CLIO001-compliant):
  - `TryGetCurrentProfile(...)` — `SelectQuery` (`Id`, `Theme`) → current
    SysAdminUnit Id + currently applied cssClassName.
  - `TryApplyTheme(cssClassName, ...)` — `UpdateQuery` + **read-back verify**:
    re-select and compare `Theme`; a silent mismatch means the server-side
    `ChangeTheme` feature is off (the listener no-ops) → actionable error, not
    false success (FR-5).
  - Error mapping for the two throwing gates: missing `CanCustomizeBranding`
    license, denied `CanChangeOwnTheme` operation.
- New `SetUserThemeOptions` (`[Verb("set-user-theme")]`,
  `[RequiresCreatioVersion(ThemeServiceRequirement.MinVersion)]`) +
  `SetUserThemeCommand : RemoteCommand<SetUserThemeOptions>` in
  `clio/Command/Theming/`:
  - Positional `theme` value: accepted as Id, CssClassName, or Caption
    (case-insensitive). Resolution order: exact Id → exact CssClassName →
    Caption; resolved via `ListThemesCommand.TryGetAvailableThemes` (already
    `public virtual` for reuse). Built-in themes accepted via a small alias map
    (`default`/`default-theme`, `dark`/`dark-mode`) since `list-themes` returns
    only custom themes.
  - `--reset` flag (kebab-case): writes empty `Theme`, restoring the
    `DefaultTheme`-setting fallback; mutually exclusive with the positional value.
  - Unknown theme → error listing available themes.
  - Success output: applied caption + cssClassName + "refresh the page" hint.
- Registration in `Program.cs` verb list + DI.

## Acceptance Criteria
- [x] AC-01 — `clio set-user-theme <caption|cssClassName|id>` applies the theme; re-reading `SysUserProfile.Theme` returns the resolved cssClassName.
- [x] AC-02 — `--reset` clears the profile theme (empty string written; verified by read-back).
- [x] AC-03 — Unknown theme name fails with the available-theme list in the message; nothing is written.
- [x] AC-04 — Silent no-op (feature `ChangeTheme` off) is detected via read-back and reported as an actionable error; license/operation failures map to distinct actionable messages.
- [x] AC-05 — Command is version-gated to Creatio 10.0.0+ like the rest of the theming surface.
- [x] AC-06 — No new `CLIO*` diagnostics; command registered in DI (`RemoteCommand` family style, matching `create-theme` — the reused `TryX` method is the seam the story-2 MCP tool consumes).

## Implementation notes
- Files: `clio/Command/Theming/SetUserThemeCommand.cs` (+ options + `AppliedUserTheme`), registered in `BindingsModule.cs` and dispatched in `Program.cs`.
- Reuses `SelectQueryHelper` (Select/read-back envelopes) and `ListThemesCommand.TryGetAvailableThemes` (theme resolution).
- Docs (the 4 targets the `BaseCommandTests` README gate enforces) were created here rather than deferred to story 4: `help/en/set-user-theme.txt`, `docs/commands/set-user-theme.md`, `Commands.md`, `Wiki/WikiAnchors.txt`. Story 4 now only owns the ADR D-D6 amendment + capability-map row.
- Tests: `clio.tests/Command/SetUserThemeCommandTests.cs` (13 cases). Validated: `dotnet test --filter "Category=Unit"` (full suite, both net8.0 + net10.0, 5890 passed / 0 failed — composition root touched).

## Tests
`clio.tests` (Module=Command, `BaseCommandTests<SetUserThemeOptions>` fixture): resolution order (id/cssClassName/caption/built-in alias), reset path, mutual-exclusivity validation, UpdateQuery envelope shape (rootSchemaName/column/filter Id from SelectQuery), read-back mismatch → feature-off error, license/operation error mapping, version-gate attribute presence.
Validated with: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=Command"`.
