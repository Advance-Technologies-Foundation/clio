# Story 2: Math port complete — `ColorNormalizer`/`PaletteGenerator`/`ColorMetrics`/`TextTokenResolver`/`FontImportBuilder` + `ThemeCssBuilder` + `Theming` module trait

**Feature**: theming-native-build (ENG-90636, epic ENG-26797 — Theming with AI, clio dev flow; native-build + bundled-template continuation)
**FR coverage**: D2, D3, D4, D7, R-09, R-10, R-11, R-14, R-17, R-18 — the full color-math + builder port
**PRD**: design substance in the approved migration plan ([adr-theming-native-build.md](../adr/adr-theming-native-build.md) §Context)
**ADR**: [adr-theming-native-build.md](../adr/adr-theming-native-build.md) — **Accepted**; refinements R-01…R-18 are authoritative and supersede the pre-review body
**Test plan**: [tp-theming-native-build.md](../test-plans/tp-theming-native-build.md)
**Status**: ready-for-dev
**Size**: L (full day)

> **Depends on Story 1** (the parity GATE). Completes the C# port: the remaining math
> (`ColorNormalizer`/full `PaletteGenerator`/`ColorMetrics`/`TextTokenResolver`/`FontImportBuilder`), the one DI
> behavior class `IThemeCssBuilder`/`ThemeCssBuilder` (verbatim `buildThemeCss` port — regex fill with the R-10
> hazards pinned + the post-fill guard + the bundled `theme.json.tpl` contract), the inline constants/records,
> the `clio/Theming/` namespace + new `Theming` module trait, every ported `*.spec.ts`
> anchor, and the **uniform five-palette verification (R-18, no secondary special-case)**. No template provider,
> no CLI/MCP surface here (Stories 3, 4) — `Build(templateCss, options)` takes the template **as an argument**.

---

## As a

developer building the native `build-theme` compute surface

## I want

the full deterministic OKLCH color math and the `IThemeCssBuilder` template-fill orchestrator ported to C#,
proven bit-exact against every `*.spec.ts` anchor and the uniform five-palette golden

## So that

`build-theme` (Stories 3/4) is a thin shell over verified, unit-tested, I/O-free math that reproduces the
retired `@creatio/theming` package exactly, with the template supplied as an argument so the math is testable
without reading from disk

---

## Acceptance Criteria

- [ ] **AC-01** — Given `ColorNormalizer.Normalize`, when given each TS-contract form, then: `#FFF` → `#ffffff`;
  `004FD6` (no `#`) → `#004fd6`; output lowercased; `rgb(...)`/`hsl(...)` normalize with
  `hsl(217,100%,42%)` → `#0052d6` (R-11d); a CSS named color → its hex via a **lowercase-then-ordinal-exact**
  lookup (R-11d); and **all alpha forms** (`#RRGGBBAA`, `rgba()`, `hsla()`) are **rejected**. All numeric
  parse/format use `CultureInfo.InvariantCulture` (R-09). (D3; TC-U-01, TC-U-03.)
- [ ] **AC-02** — Given `PaletteGenerator`, when run on the calibration anchor, then `generateScale('#004fd6')` returns
  all 12 published spec shades (incl. shade 10 `#f7f8fb`, shade 900 `#001c5a`); `deriveSecondary('#004fd6') ==
  '#0d2e4e'`; `accentCandidates('#004fd6')` returns all three `[#f94e11 @135°, #d29a16 @180°, #87b716 @225°]`;
  and `cuspL` is reproduced with the exact `double` `+=` loop (start `0.35`, step `0.01`, bound `0.85`) — not
  index arithmetic. (D3, R-11b; TC-U-02, TC-U-05, TC-U-06.)
- [ ] **AC-03** — Given `ColorMetrics`, when run on the anchors, then `relativeLuminance`/`contrastRatio`/
  `distanceOklab` match their TS anchor values exactly (the `≥`-threshold comparisons — 3.0 on white, 4.5 text
  — pinned at the boundary); `chooseBestAccent` uses **`OrderByDescending`** and **preserves original candidate
  order on ties** (compare `double`s, never cast to `int`; a deliberate tie-break anchor proves stability —
  JS `Array.sort` is stable, C# `Array.Sort` is not); `suggestAdaptedPrimary500('#cccccc') == '#949494'` and
  `('#000000') == null`, its loop reproduced with exact `double` accumulation (start/step/bound/comparison),
  not index arithmetic. (R-11a/R-11b; TC-U-07, TC-U-08.)
- [ ] **AC-04** — Given `TextTokenResolver` + `FontImportBuilder`, when resolved, then `resolveTextToken`/`resolveLinkHover`/
  `resolveTextOnColorToken` resolve to the expected passing palette step; `googleFontsImportUrl`/`Rule`
  produce the expected URLs (weights **sorted + deduped**; family validation); and the Montserrat **default**
  is compared **case-sensitively (ordinal)** so a default-Montserrat input yields **no** `@import`. (D3, R-11d;
  TC-U-10.)
- [ ] **AC-05** — Given `IThemeCssBuilder.Build(string templateCss, BuildThemeOptions options)`, when called with a
  valid input + the fixture template, then it is a **verbatim port of `buildThemeCss`**: strip the leading
  header comment, `replaceAll('<%themeCssClass%>', …)`, per-palette-name×step `--crt-palette-{name}-{step}`
  substitution, `finalizeTextTokens` (resolve each `--crt-color-*` role to a passing palette step + rewrite to
  `var(--crt-palette-…)`, plus `text-link-hover` and `text-on-*`), and `applyFonts`. The template is taken
  **as an argument** — **no disk read inside the math** (D2). Output **contains** the substituted palette +
  resolved roles + the supplied `<%themeCssClass%>`; **does not contain** the stripped comment or any literal
  `<%…%>`; the three documented throw cases raise the expected errors. (D2, D4; TC-U-15.)
- [ ] **AC-06** — Given the regex template fill, when ported, then the **R-10 hazards are pinned exactly**:
  (a) `applyPalettes`/`setColorDeclaration` use the **count-1** `Regex.Replace` overload (JS no-`g` replaces
  only the first match) — a duplicate `--crt-palette-…` line replaces **only the first**; (b) the font-family
  replacement (user input) uses a **`MatchEvaluator`**, not raw interpolation (a family containing `$1`/`$&` is
  inserted literally); (c) `THEME_CSS_CLASS_PATTERN` (and similar) use **`\z`, not `$`** — `"Foo\n"` is
  **rejected** (.NET `$` would wrongly pass); (d) the comment strip keeps `[\s\S]` verbatim, **no anchor**,
  matches `\n` only (never `\r?\n` — preserve stray `\r` to stay bit-exact), with **no** `RegexOptions.Multiline`
  / `RegexOptions.ECMAScript`; (e) every **regex** step carries a **match timeout** (Sonar S6444), while the
  literal `<%themeCssClass%>` swap is a plain `string.Replace` (no regex, no timeout). (R-10; TC-U-16.)
- [ ] **AC-07** — Given a deliberately drifted template (leftover `<%foo%>` or a missing `--crt-palette-…`
  target), when `Build` runs, then it **fails with a clear error** rather than emit a broken theme; a valid
  template asserts the post-fill guard passes (no `<%…%>` remains, every palette step substituted). (D4;
  TC-U-17.)
- [ ] **AC-08** — Given an input whose output exceeds `ThemeRequestBuilder.MaxCssContentBytes` (1 MiB =
  `1_048_576` bytes — heavy custom fonts / a large template), when built, then `Build` still returns the
  `theme.css` **string** and does **not** pre-check or warn — there is no `BuildThemeOutput` and no advisories;
  the cap itself is enforced downstream in `create-theme`. `ReadOnly`/`Idempotent` describe **environment**
  effects (R-17). (R-14; TC-U-23.)
- [ ] **AC-09** — Given the bundled `theme.json.tpl` (under `tpl/themes/{version}/` — **not** an embedded C#
  constant, **not** CDN-sourced, no freedom.scss data), when filled, then it ports the **exact** placeholder/field
  contract `{<%themeId%>, <%themeCaption%>, <%themeCssClass%>}` → `{id, caption, cssClassName}`, produces valid JSON
  with the supplied values, and leaves no `<%…%>`. (D5, R-17; TC-U-24.)
- [ ] **AC-10 (R-18, uniform five-palette)** — Given the five default anchors (`primary #004fd6`,
  `secondary #0d2e4e`, `accent #ff4013`, `success #0b8500`, `error #d2310d`), when `generateScale(-500)` runs
  on each, then C# output equals the TS golden **identically** — **same code path, full 10-shade comparison,
  NO secondary special-case, NO separate 600–900 handling**. (Primary's 12 shades are the published spec
  values; the other four goldens come from the parity fixture.) The guard explicitly does **NOT** assert
  `generateScale` reproduces the template's **baked secondary/accent** values — those are sample defaults +
  regex targets, overwritten by `applyPalettes`. (R-18, D7 layer 3; TC-U-P05.)
- [ ] **AC-11 (DI/CLIO005)** — Given the registrations introduced for the builder, when the analyzer runs, then
  `IThemeCssBuilder` is registered `AddSingleton` (justified: `Build(templateCss, options)` is a **pure function
  taking all inputs as args, holding no fields** — note the asymmetry with the `AddTransient` sibling theme
  commands so reviewers aren't surprised) and has a statically-visible injection site; the `internal static`
  math classes are **not** registrations and do not trip CLIO005. (R-06.)
- [ ] **AC-ERR** — Given malformed brand input (invalid hex / alpha form / invalid `cssClassName` / unknown
  named color), when `Build` runs, then it surfaces a user-friendly `Error: …` candidate string (no stack
  trace, no bare `catch (Exception)`) — matching the TS contract's throw cases.

## Implementation Notes

The parity GATE (Story 1) must be green (or its fallback applied) before this story starts. `Build` takes the
template **as an argument**; the template provider (Story 3) and the CLI/MCP surface (Story 4) are separate.

**Key files (create): `clio/Theming/ColorNormalizer.cs`, `PaletteGenerator.cs`, `ColorMetrics.cs`, `TextTokenResolver.cs`, `FontImportBuilder.cs`**
(ADR Implementation Plan, D2/D3) — `internal static` (no DI, reached via `InternalsVisibleTo`). Complete the
`ColorNormalizer` alpha-reject/rgb/hsl/named matrix (+ internal `hslToHex`); finish `PaletteGenerator` (`GenerateScale`,
`DeriveSecondary`, `GenerateAccentCandidates` + `cuspL`); `ColorMetrics` (`relativeLuminance`, `contrastRatio`,
`distanceOklab`, `chooseBestAccent` via `OrderByDescending` stable tie-break, `suggestAdaptedPrimary500` exact
loop); `TextTokenResolver` (`resolveTextToken`, `resolveLinkHover`, `resolveTextOnColorToken`); `FontImportBuilder`
(`BuildUrl`/`BuildRule`, sorted+deduped weights, family validation, ordinal Montserrat-default check).
All numeric parse/format invariant-culture (R-09).

**Key file (create): `clio/Theming/CssNamedColors.cs`** — CSS named-colors map (lowercase-then-ordinal-exact
lookup); the other constants live **inline** in their owning class (no `ThemingConstants.cs`, no `Models/` folder).
`BuildThemeOptions`, `FontsInput`, `FontFamilyEntry`, `AccentCandidate`/`ScoredAccentCandidate`,
`AdaptedPrimary`, `TextTokenResolution`, `TextOnColorResolution` and palette/color types are `record`s declared
**inline** in their owning files (there is no `BuildThemeOutput`; data carriers may use `new`).

**Key file (create): `clio/Theming/ThemeCssBuilder.cs`** (ADR Implementation Plan, D2, D4, R-10, R-14, R-17) —
`IThemeCssBuilder` + impl; `Build(string templateCss, BuildThemeOptions options)` verbatim port of `buildThemeCss`
returning the `theme.css` **string**; the regex fill with the R-10 hazards (count-1 `Regex.Replace`,
`MatchEvaluator` for user input, `\z` not `$`, `[\s\S]` comment strip `\n`-only, no `Multiline`/`ECMAScript`,
match timeouts on regex steps only) + the post-fill no-`<%`/all-steps guard (the 1 MiB cap is enforced downstream
in `create-theme`, not here). The bundled `theme.json.tpl` (under `tpl/themes/{version}/`) carries its own
placeholder contract. `AddSingleton` (pure, fieldless — R-06/AC-11).

**Key file (create): the ported `*.spec.ts` anchors as NUnit** in `clio.tests/Theming/` — `ColorNormalizerTests`,
`PaletteGeneratorTests`, `ColorMetricsTests`, `ColorSpaceTests` (extend Story 1's), `TextTokenResolverTests`, `FontImportBuilderTests`,
`ThemeCssBuilderTests`, and `ColorMathParityTests` (the five-palette uniform guard + the template/builder contract
live in the parity tests + the in-builder post-fill contract guard). `Fixtures/theme.css.tpl` is the
**actual creatio-ui freedom.scss-derived template** (not a clio hand-bless — R-15).

**Key files (modify): `CLAUDE.md`, `AGENTS.md`** — add the new **`Theming`** module trait to the
smart-regression maps (`Theming` → `clio/Theming/`), and register the `[Property("Module","Theming")]` usage so
targeted runs stay honest without triggering the full suite.

Pattern to follow: `PageSchemaMetadataHelper`/`ThemeRequestBuilder` (`internal static` math precedent);
`ComponentRegistrySnapshotTests` (the golden/contract drift-guard shape for `ColorMathParityTests`); the
TS `theme-builder.ts` / `color-space.ts` / `palette.ts` sources for the verbatim port.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` `[Property("Module","Theming")]` | **TC-U-01** `Normalize` matrix (shorthand/no-`#`/lowercase, rgb/hsl `#0052d6`, named lowercase-ordinal, alpha-reject); **TC-U-03** culture-parity under `de-DE` | `clio.tests/Theming/ColorNormalizerTests.cs` |
| Unit `[Category("Unit")]` `[Property("Module","Theming")]` | **TC-U-02** `generateScale('#004fd6')` 12 shades; **TC-U-05** `deriveSecondary`==`#0d2e4e`; **TC-U-06** `accentCandidates` all three + `cuspL` exact loop | `clio.tests/Theming/PaletteGeneratorTests.cs` |
| Unit `[Category("Unit")]` `[Property("Module","Theming")]` | **TC-U-07** `chooseBestAccent` stable tie-break + `suggestAdaptedPrimary500` exact loop; **TC-U-08** luminance/contrast/distance anchors + thresholds; **TC-U-09** `detectMode`/threshold boundary anchors | `clio.tests/Theming/ColorMetricsTests.cs`, `ColorSpaceTests.cs` |
| Unit `[Category("Unit")]` `[Property("Module","Theming")]` | **TC-U-10** text tokens + Google Fonts URLs (sorted/deduped, ordinal Montserrat default → no `@import`) | `clio.tests/Theming/TextTokenResolverTests.cs`, `FontImportBuilderTests.cs` |
| Unit `[Category("Unit")]` `[Property("Module","Theming")]` | **TC-U-15** `Build` contains/not-contains + three throw cases; **TC-U-16** R-10 regex hazards (count-1, `MatchEvaluator`, `\z`, `[\s\S]`/`\n`-only, timeouts); **TC-U-17** post-fill guard; **TC-U-23** output over 1 MiB still returns the string (no advisory; cap enforced in `create-theme`) | `clio.tests/Theming/ThemeCssBuilderTests.cs` |
| Unit `[Category("Unit")]` `[Property("Module","Theming")]` | **TC-U-24** `theme.json.tpl` placeholder contract guard | `clio.tests/Theming/ThemeCssBuilderTests.cs` |
| Unit `[Category("Unit")]` `[Property("Module","Theming")]` | **TC-U-P05** five-palette uniform calibration (R-18, no secondary special-case) + template/builder contract | `clio.tests/Theming/ColorMathParityTests.cs` |

- All `clio.tests/Theming/` tests carry `[Category("Unit")]` (never `[Category("UnitTests")]`) **AND**
  `[Property("Module","Theming")]` (R-06).
- Test naming `MethodName_ShouldExpectedBehavior_WhenCondition` (e.g.
  `NormalizeHex_ShouldRejectAlphaForms_WhenInputHasAlpha`,
  `ChooseBestAccent_ShouldPreserveOriginalOrder_WhenScoresTie`,
  `ApplyPalettes_ShouldReplaceFirstOccurrenceOnly_WhenTemplateHasDuplicate`,
  `Build_ShouldReturnCss_WhenOutputExceedsOneMiB`,
  `GenerateScale_ShouldMatchTsGolden_ForAllFivePalettesUniformly`).
- AAA + a `because` on every assertion + `[Description]` on every test.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO005); math classes are `internal static` (not registrations); `IThemeCssBuilder` `AddSingleton` is the only DI behavior class and has a statically-visible injection site (R-06)
- [ ] `Build(templateCss, options)` takes the template **as an argument** and returns the `theme.css` string — no disk read in the math (D2)
- [ ] R-07 rounding / R-09 invariant-culture + `byte.Parse(HexNumber)` carried through every math class
- [ ] R-10 regex hazards pinned exactly (count-1 `Regex.Replace`, `MatchEvaluator`, `\z` not `$`, `[\s\S]`/`\n`-only comment strip, no `Multiline`/`ECMAScript`, match timeouts on regex steps only)
- [ ] Post-fill guard (no `<%…%>` / all palette steps substituted) fails loudly on a drifted template (D4)
- [ ] No 1 MiB pre-check or advisory in `Build` — output over the cap still returns the string; the cap is enforced downstream in `create-theme` (R-14)
- [ ] Bundled `theme.json.tpl` ports the exact `{<%themeId%>,<%themeCaption%>,<%themeCssClass%>}` contract with its own guard test (R-17)
- [ ] **Uniform five-palette verification** asserts `generateScale(-500)` for all five anchors **identically** — no secondary special-case, no separate 600–900 handling (R-18)
- [ ] `Fixtures/theme.css.tpl` is the **actual creatio-ui freedom.scss-derived template** (not a clio hand-bless — R-15)
- [ ] New `Theming` module trait added to `CLAUDE.md` + `AGENTS.md` smart-regression maps (`Theming` → `clio/Theming/`)
- [ ] All Theming tests carry `[Category("Unit")]` **AND** `[Property("Module","Theming")]` — never `[Category("UnitTests")]`
- [ ] Targeted run: `dotnet test --filter "Category=Unit&Module=Theming"`. **Note:** no BindingsModule/Program wiring lands in this story (that is Story 4) — this story does **not** require the full-suite trigger; the full `Category=Unit` suite is mandatory in Story 4
- [ ] `.codex/workspace-diary.md` entry appended
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-06-26
- Implementation completed: 2026-06-26
- Tests passing: **52 passed, 0 failed** on **both** `net8.0` and `net10.0`
  (`dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=Theming"`). Production build
  clean — **0 new warnings** from `clio/Theming/**` (the only build warnings are the two pre-existing,
  unrelated CLIO005 on `CreateEntityBusinessRuleCommand`/`CreatePageBusinessRuleCommand`).
- Notes:
  - **`ThemeCssBuilder` is BIT-EXACT with the TS `buildThemeCss`** end-to-end: a 5-case builder golden
    (`Fixtures/theme-css-golden.json`, generated via `npx tsx builder-driver.mts` reading the exact
    committed `Fixtures/theme.css.tpl`) matches the C# output hex-for-hex (default, custom-font,
    vivid-derived, explicit-colors, two-fonts). Every ported `*.spec.ts` anchor passes (normalize /
    palette / metrics / text-tokens / fonts).
  - **R-18 uniform five-palette** guard (`ColorMathParityTests`) verifies `generateScale(-500)`
    for all five default anchors identically — no secondary special-case, no 600–900 carve-out.
  - **R-10 regex hazards pinned + tested:** count-1 `Regex.Replace` (duplicate-line test proves
    first-match-only), `MatchEvaluator` for the user font family, `\z` not `$` (`"Foo\n"` rejected test),
    `[\s\S]` comment strip `\n`-only with no `Multiline`/`ECMAScript`, `RegexTimeout = FromSeconds(1)`
    (mirrors `ThemeRequestBuilder`); the `<%themeCssClass%>` swap is a literal `string.Replace`.
  - **Post-fill guard** (`InvalidOperationException` on leftover `<%…%>` or a missing palette stop) tested;
    `Build` returns the `theme.css` string with no 1 MiB pre-check/advisory (cap enforced in `create-theme`).
  - **theme.json descriptor** is a bundled `theme.json.tpl` (under `tpl/themes/{version}/`) with its placeholder
    contract guarded; NOT an embedded constant, NOT CDN-sourced (R-17).
  - **`Theming` module trait** added to the `AGENTS.md` module-to-source map (`Theming → clio/Theming/`);
    `CLAUDE.md` `@import`s `AGENTS.md`, so no separate CLAUDE.md edit was needed.
  - **DI deferred to Story 4 (per this story's scope):** `IThemeCssBuilder`/`ThemeCssBuilder` are defined
    but NOT yet registered in `BindingsModule` (so no CLIO005, no full-suite trigger). AC-11's
    `AddSingleton`-pure-fieldless justification lands with the registration in Story 4. Tests construct
    `new ThemeCssBuilder()` (test code).
  - Deviation: `hexToOklab` lives in `ColorMetrics` (mirrors `metrics.ts`), not `ColorSpace` — Story 1 ported
    only `color-space.ts` (which has no `hexToOklab`).
  - New files: `clio/Theming/{ColorNormalizer,ColorMetrics,TextTokenResolver,FontImportBuilder,ThemeCssBuilder,CssNamedColors}.cs`
    (+ `ColorSpace,PaletteGenerator` extended; constants inline, no `Models/`); `clio.tests/Theming/{ColorNormalizer,PaletteGenerator,ColorMetrics,TextTokenResolver,FontImportBuilder,ThemeCssBuilder,ColorMathParity}Tests.cs`
    + `Fixtures/{theme.css.tpl,theme-css-golden.json}`. Nothing committed (no PR opened).
