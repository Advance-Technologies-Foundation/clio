# Story 3: `IThemeTemplateProvider` — bundled, version-pinned template sourcing

**Feature**: theming-native-build (ENG-90636, epic ENG-26797 — Theming with AI, clio dev flow; native-build + bundled-template continuation)
**FR coverage**: D5, R-03 — the bundled `tpl/themes/{version}/` template provider
**PRD**: design substance in the approved migration plan ([adr-theming-native-build.md](../adr/adr-theming-native-build.md) §Context)
**ADR**: [adr-theming-native-build.md](../adr/adr-theming-native-build.md) — **Accepted**; refinements R-01…R-18 are authoritative and supersede the pre-review body
**Test plan**: [tp-theming-native-build.md](../test-plans/tp-theming-native-build.md)
**Status**: ready-for-dev
**Size**: M (half day)

> **Independent — can run in parallel with Story 2.** An `IThemeTemplateProvider`
> (`Clio.Command.Theming`) that reads the **bundled, version-pinned** `theme.css.tpl` (and `theme.json.tpl`)
> off disk from `tpl/themes/{version}/` — shipped in the tool by the existing `tpl\**` content glob, exactly
> like the `tpl/ui` project templates. It picks the highest bundled version ≤ the target Creatio version
> (empty/null target ⇒ the highest bundled), throws `ArgumentException` when the target is below the lowest
> bundled, and `InvalidOperationException` when the resolved template file is missing. No network, no cache,
> no override — the template is a shipped asset.

---

## As a

developer wiring `build-theme` to source its `theme.css.tpl` template from clio's bundled `tpl/themes/{version}/`
the way the `tpl/ui` project templates are shipped

## I want

an `IThemeTemplateProvider` that reads the bundled `theme.css.tpl` / `theme.json.tpl` off disk and picks the
version-matched folder for the target Creatio version

## So that

`build-theme` (Story 4) gets a stable, in-tool template source with no network dependency — resolving the
correct per-version template, and failing clearly when the target version predates the lowest bundled template
or the template file is missing

---

## Acceptance Criteria

- [ ] **AC-01** — Given the bundled `tpl/themes/{version}/theme.css.tpl` + `theme.json.tpl` shipped by the
  existing `<Content Include="tpl\**">` glob (same as `tpl/ui`), when `GetCssTemplate` / `GetJsonTemplate` is
  called, then the provider reads the version-matched template off disk via
  `IWorkingDirectoriesProvider.TemplateDirectory` — no network, no cache, no override. (D5; TC-U-25–28.)
- [ ] **AC-02 (highest-when-empty)** — Given an empty/null target Creatio version, when a template is requested,
  then the provider returns the **highest bundled** version's template. (D5; TC-U-25.)
- [ ] **AC-03 (R-03, version pinning)** — Given a target Creatio version, when a template is requested, then the
  provider lists the `tpl/themes/<Version>/` dirs, parses them as `Version`, and picks the **highest bundled
  version ≤ the target** (`LastOrDefault(v <= target)`). (D5, R-03; TC-U-26.)
- [ ] **AC-04 (too-old target)** — Given a target version **below the lowest bundled**, when a template is
  requested, then the provider throws `ArgumentException("Themes require Creatio {min} or newer; version
  {target} is not supported.")`. (D5; TC-U-27.)
- [ ] **AC-05 (missing file)** — Given a resolved version folder whose `theme.css.tpl` / `theme.json.tpl` is
  absent, when that template is requested, then the provider throws `InvalidOperationException`. (D5; TC-U-28.)
- [ ] **AC-ERR** — Given a too-old version or a missing template file, when surfaced, then the message is a
  user-friendly `Error: …` candidate (no stack trace, no bare `catch (Exception)`); the provider performs the
  read and throws, and `BuildThemeCommand` / `BuildThemeTool` (Story 4) turn it into a CLI error /
  `success:false`.

## Implementation Notes

A bundled-template provider. The CLI/MCP surface that consumes it is Story 4; the math/`IThemeCssBuilder` is
Story 2. No MCP artifact exists for the provider itself (it has no tool); MCP review for the consuming
`BuildThemeTool` belongs to Story 4.

**Key files (create):**
- **`clio/tpl/themes/{version}/theme.css.tpl`, `theme.json.tpl`** — the freedom.scss-derived templates, copied
  to the build output by the existing `<Content Include="tpl\**">` glob (`10.0` is the first bundled version).
- **`clio/Command/Theming/ThemeTemplateProvider.cs`** (namespace `Clio.Command.Theming`) → `IThemeTemplateProvider`:
  - `GetCssTemplate(creatioVersion)` / `GetJsonTemplate(creatioVersion)` read the version-matched bundled file
    off disk via `IWorkingDirectoriesProvider.TemplateDirectory`.
  - **Version pinning.** List the `tpl/themes/<Version>/` dirs, parse them as `Version`, pick the highest
    bundled version ≤ the target (`LastOrDefault(v <= target)`); empty/null target ⇒ the highest bundled.
  - A target **below the lowest bundled** ⇒ `ArgumentException("Themes require Creatio {min} or newer; version
    {target} is not supported.")`; a missing template file ⇒ `InvalidOperationException`.
  - No network, no `~/.clio/cache` tier, no override, no retry — the template is a shipped asset.

The actual version-source flags (`--version` XOR `--environment-name`) are owned by Story 4; this
provider just takes the already-resolved target `Version`.

**DI:** register `IThemeTemplateProvider` (the gated tool/command in Story 4 is the statically-visible
injection site — CLIO005 clean). The BindingsModule wiring may co-land here or in Story 4; if it lands here,
run the full unit suite (BindingsModule touched — rule 4). Recommended: register in Story 4 alongside the rest,
keeping this story's surface to the provider + its tests.

Pattern to follow: the `tpl/ui` project-template shipping mechanism (`<Content Include="tpl\**">` glob +
`IWorkingDirectoriesProvider.TemplateDirectory`).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` `[Property("Module","Command")]` | **TC-U-25** empty/null target ⇒ the **highest bundled** version's template is returned | `clio.tests/Command/ThemeTemplateProviderTests.cs` |
| Unit `[Category("Unit")]` `[Property("Module","Command")]` | **TC-U-26** a target between/at bundled versions ⇒ the **highest bundled ≤ target** is picked (`LastOrDefault(v <= target)`) | `ThemeTemplateProviderTests.cs` |
| Unit `[Category("Unit")]` `[Property("Module","Command")]` | **TC-U-27** a target **below the lowest bundled** ⇒ `ArgumentException` ("Themes require Creatio {min} or newer…") | `ThemeTemplateProviderTests.cs` |
| Unit `[Category("Unit")]` `[Property("Module","Command")]` | **TC-U-28** a resolved version folder with a missing template file ⇒ `InvalidOperationException` | `ThemeTemplateProviderTests.cs` |

- Substitute `IWorkingDirectoriesProvider` over a temp template dir seeded with fake version folders for the
  unit cases.
- `[Category("Unit")]` (never `[Category("UnitTests")]`) **AND** `[Property("Module","Command")]` (the provider
  lives in `Clio.Command.Theming`); naming `MethodName_ShouldBehavior_WhenCondition` (e.g.
  `GetCssTemplate_ShouldReturnHighestBundled_WhenTargetIsEmpty`,
  `GetCssTemplate_ShouldPickHighestNotNewerThanTarget_WhenTargetBetweenVersions`,
  `GetCssTemplate_ShouldThrowArgumentException_WhenTargetBelowLowestBundled`,
  `GetCssTemplate_ShouldThrowInvalidOperationException_WhenTemplateFileMissing`).
- AAA + a `because` on every assertion + `[Description]` on every test; the unit cases seed a temp template dir
  with fake version folders (UTF-8) and delete it in teardown (OS-portable).

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO005); `IThemeTemplateProvider` registration has a statically-visible injection site (the gated tool/command, Story 4)
- [ ] Bundled `tpl/themes/{version}/theme.css.tpl` + `theme.json.tpl` shipped by the `<Content Include="tpl\**">` glob (`10.0` is the first bundled version) (D5)
- [ ] `GetCssTemplate` / `GetJsonTemplate` read off disk via `IWorkingDirectoriesProvider.TemplateDirectory` — no network, no cache, no override (D5)
- [ ] Version pinning: highest bundled ≤ target (`LastOrDefault(v <= target)`); empty/null target ⇒ highest bundled (D5, R-03)
- [ ] Too-old target ⇒ `ArgumentException` ("Themes require Creatio {min} or newer…"); missing template file ⇒ `InvalidOperationException` (D5)
- [ ] Unit tests added with `[Category("Unit")]` (never `UnitTests`) **AND** `[Property("Module","Command")]`
- [ ] Targeted run: `dotnet test --filter "Category=Unit&Module=Command"`. Run the full unit suite **only if** BindingsModule wiring lands here (otherwise it lands in Story 4)
- [ ] MCP reviewed: the provider has no MCP tool of its own; it is consumed by `BuildThemeTool` (Story 4), whose MCP review belongs to that story
- [ ] PR description references this story file

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started:
- Implementation completed:
- Tests passing:
- First bundled version + version-pinning decision (D5, R-03):
- Notes:
