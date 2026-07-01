# ADR: Native C# theme building in clio (`build-theme` + bundled template)

**Status**: Accepted — revised after adversarial bmad-reviewer pass 2026-06-26 (see "Post-review refinements"), then revised 2026-06-30 when the template became **bundled in clio** (`tpl/themes/{version}/`). That pivot supersedes the earlier remote-fetch design wherever D5/R-03/R-04/R-05/R-12 conflict; the rest of the refinements remain authoritative over the pre-review body.
**Author**: Architect (from approved migration plan)
**PRD**: design substance in the approved migration plan; promote to `spec/prd/prd-theming-native-build.md` if a separate PRD is required. Test plan `spec/test-plans/tp-theming-native-build.md` still to be produced (BMAD Phase 4 — R-13).
**Jira**: ENG-90636 (epic ENG-26797) — Theming with AI, clio dev flow; native-build + bundled-template continuation. Siblings: ENG-90636 Contour A (shipped), ENG-91387 Contour B / server flow (shipped)
**Created**: 2026-06-26
**stepsCompleted**: [1, 2, 3]

---

## Context

Custom-theme CSS is today produced by the `@creatio/theming` npm package (TypeScript, ESM):
deterministic OKLCH color math + a `buildThemeCss` orchestrator that fills a `theme.css.tpl`
template. clio never runs this — `ThemingGuidanceResource` (`docs://mcp/guides/theming`) *instructs
the agent* to `npm pack`/`npm i` the package and run `buildThemeCss` from a Node script, then feed
the result into the shipped theme CRUD (`create-theme-by-environment`, etc.). **We are retiring the
npm package.**

Target model: **`build-theme = math (clio C#) + bundled template + brand inputs → theme.css`.**
- **clio bundles** the freedom.scss-derived `theme.css.tpl` + `theme.json.tpl` under
  `tpl/themes/{version}/`, shipped in the tool exactly the way the `tpl/ui` project templates are, and
  version-pinned (the highest bundled version not newer than the target Creatio version is chosen).
- **clio** also holds the **ALGORITHM**: the deterministic color math, ported to C#, exposed via a
  new `build-theme` MCP tool + CLI verb. Template and math both ship in clio — no network fetch.
- The `--crt-*` **token catalog** (advanced hand-authoring) still belongs on the CDN/Academy, served
  later via `get-theme-tokens` (out of scope) — only the build-theme *template* moved into clio.

This ADR designs the **consumer + math** side: (1) the C# port of the color math, (2) the
`build-theme` surface, (3) the bundled version-pinned template provider, (4) bit-exact validation,
(5) the guidance swap, (6) decommission of clio's references to the npm package. It is **stacked on**
ENG-90636 / ENG-91387, which contribute the `ThemeService` route pattern, the theme CRUD
commands/tools, the `ThemingGuidanceResource`, and the `DefaultTheme` activation path; the bundled
template + version resolution mirror the `tpl/ui` project-template pattern.

The math source is **creatio-ui** `feature/ENG-90636-ai-theming-styling-guides` →
`libs/devkit/theming` (public surface: `normalizeHex`, `generateScale`, `deriveSecondary`,
`accentCandidates`, `chooseBestAccent`, `suggestAdaptedPrimary500`, `contrastRatio`,
`relativeLuminance`, `distanceOklab`, `resolveTextToken`, `resolveTextOnColorToken`,
`resolveLinkHover`, `googleFontsImportUrl`/`Rule`, and the `buildThemeCss` orchestrator). It carries
a calibration contract (`*.spec.ts`) that the C# port must reproduce **bit-exact at the hex level**:
e.g. `deriveSecondary('#004fd6') === '#0d2e4e'`, `generateScale('#004fd6')` shade 10 `#f7f8fb` /
shade 900 `#001c5a`, `accentCandidates` +135° candidate `#f94e11`. The default-theme palettes baked
into `theme.css.tpl` originate in freedom.scss; the C# port reproduces them from their `-500` values
and a drift guard fails loudly if it ever diverges.

## Decision

Add a new **`clio/Theming/` namespace (`Clio.Theming`)** holding the ported, deterministic OKLCH
color math (pure `internal static` helpers) plus one DI behavior service `IThemeCssBuilder` whose
`Build(templateCss, options)` is a verbatim port of `buildThemeCss` and which takes the template **as
an argument** (no built-in disk read). Expose it through a **`build-theme` CLI verb + flat MCP tool**
(a **pure, read-only compute** surface — not combined with create), both gated by
**`[FeatureToggle("theming")]`** until the surface is complete and go-live is approved. Source the
template from **clio's bundled `tpl/themes/{version}/`** (theme.css.tpl + theme.json.tpl) through a new
**`IThemeTemplateProvider`** (in `Clio.Command.Theming`) that picks the version-matched template
(highest bundled ≤ the target Creatio version) — the same shape as the `tpl/ui` project templates. Validate bit-exact parity with ported `*.spec.ts` anchors + a generated parity fixture. Swap the
`ThemingGuidanceResource` npm instructions for `build-theme`, and remove clio's only other reference
to the package (the `clio/tpl/ui-project/package.json` scaffold dependency). The `--crt-*` token
catalog and the prose authoring guides do **not** move into clio — they belong on the CDN/Academy.

---

## Decisions in detail

### D1 — `build-theme` is a pure, read-only compute tool — NOT combined with create

`ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false`. Inputs mirror
`BuildThemeOptions`; output is the `theme.css` string. It does **not** touch an environment.

Rationale: the math is environment-agnostic and deterministic, so a pure tool composes with **both**
flows like a pipe — workspace/dev writes the returned CSS into package files; no-code/server passes
it as `create-theme-by-environment`'s `css-content`. Folding it into a mutating create would force a
needless environment arg + `by-environment`/`by-credentials` duplication onto a pure function and
conflate "compute" with "deploy". This mirrors clio's existing split (`get-component-info` is
read-only compute; `create-theme` mutates); the `clio-run` dispatcher is for *mutating* combos, which
this is not. (Reading the bundled template is a local read-only file access; the optional
`--environment-name` version probe is read-only too — `get-component-info` is `ReadOnly=true` under the
same conditions, so the flag is consistent.)

**Ships as CLI verb + MCP tool** (prior art: every theme op has both), sharing one `IThemeCssBuilder`.

### D2 — `clio/Theming/` namespace; math = `internal static`, builder = DI service

The color math is deterministic, stateless computation — `internal static` helper classes
(`ColorSpace`, `ColorNormalizer`, `PaletteGenerator`, `ColorMetrics`, `TextTokenResolver`, `FontImportBuilder`), the same shape as the
existing `PageSchemaMetadataHelper` / `ThemeRequestBuilder` static-utility precedent. They have no
collaborators, no state, and no `new` of behavior classes, so CLIO001/CLIO005 do not apply; `clio.tests`
reaches them via the existing `InternalsVisibleTo`.

`ThemeCssBuilder` is the **one** behavior class — it orchestrates validation/derivation/palette
generation/template-fill — so it gets an interface `IThemeCssBuilder` and a DI registration
(`AddSingleton`, stateless). Its `Build(string templateCss, BuildThemeOptions options)` takes the
template **as an argument** (mirrors the TS `templateCss` arg); it has **no** built-in
default-template fallback. This keeps the math I/O-free and unit-testable without a template source.

A new **`Theming`** test-module trait (`[Property("Module","Theming")]`) is added, mapped in the
smart-regression tables in `CLAUDE.md`/`AGENTS.md` (`Theming` → `clio/Theming/`). Rationale over
nesting under `McpServer`: the math is consumed by a CLI command too, and a dedicated trait keeps
targeted test runs honest without triggering the full suite.

### D3 — Color-math port: operator-for-operator, `double`, with the parity hazards pinned

Each TS function maps 1:1 (full table in the Implementation Plan). The pipeline is HEX → sRGB →
linear RGB → OKLab → OKLCH with the fixed matrices verbatim from `color-space.ts`. JS `Number` is an
IEEE-754 double, so C# `double` throughout. The **non-obvious parity hazards** (this is where a naive
port silently diverges):

- `Math.round(x)` in `rgbToHex` → **`Math.Floor(x + 0.5)`**, NOT `Math.Round` (banker's rounding —
  diverges on `.5` midpoints and on slightly-negative pre-clamp channels).
- `lv ** 3` / `mv ** 3` / `sv ** 3` → **`Math.Pow(lv, 3)`** to match JS `**` semantics (not
  `lv*lv*lv`, which can differ in the last ULP and flip a hex boundary).
- `Math.cbrt`→`Math.Cbrt`, `Math.pow`→`Math.Pow`, `Math.atan2`→`Math.Atan2`; `%`→`double` `%` (keep
  the `((h % 360) + 360) % 360` guard verbatim).
- `Number.parseInt(s,16)`→`Convert.ToInt32(s,16)`; `n.toString(16).padStart(2,'0')`→`n.ToString("x2")`.
- The binary searches (`maxChromaInGamut` 24 iters in `[0,0.4]`, `cuspL` `[0.35,0.85]` 0.01 step) and
  the `≥`-threshold comparisons (3.0 on white, 4.5 text) are the precision-sensitive points where a
  one-ULP drift becomes a different hex — covered by the parity fixture (D7).

### D4 — Template-fill is regex-driven; port the patterns verbatim, with match timeouts

`buildThemeCss`'s template fill (`theme-builder.ts`) is **regex replacement**, not templating:
- strip the leading `/* … Creatio custom theme template … */` comment;
- `replaceAll('<%themeCssClass%>', …)`;
- per palette name×step, replace `--crt-palette-{name}-{step}: #hhhhhh;` with the generated color;
- `finalizeTextTokens`: parse `--crt-color-*` declarations, resolve each role to a passing palette
  step, rewrite to `var(--crt-palette-…)`, plus `text-link-hover` and the `text-on-*` tokens (which
  read the matching `--crt-color-background-*` declaration);
- `applyFonts`: rewrite `--crt-font-family-heading|body` and prepend the Google Fonts `@import` when
  not default Montserrat.

Port the regexes verbatim **with a `matchTimeout`** — the repo just added match timeouts to theme
regexes for Sonar S6444; every new `Regex` follows. Add a post-fill runtime guard in `ThemeCssBuilder`:
assert no `<%…%>` remains and every palette step was substituted — fail with a clear error rather
than emit a broken theme (defends against a producer template that drifts from the builder's contract).

### D5 — Template sourcing: bundled, version-pinned `IThemeTemplateProvider`

The freedom.scss-derived templates ship **inside clio** under `clio/tpl/themes/{version}/`
(`theme.css.tpl` + `theme.json.tpl`), copied to the build output by the existing
`<Content Include="tpl\**">` glob — the same mechanism that ships the `tpl/ui` project templates. A new
**`IThemeTemplateProvider`** (`clio/Command/Theming/ThemeTemplateProvider.cs`, namespace
`Clio.Command.Theming`) reads them off disk via `IWorkingDirectoriesProvider.TemplateDirectory`:

- **Version pinning.** `GetCssTemplate(creatioVersion)` / `GetJsonTemplate(creatioVersion)` resolve the
  compatible bundled folder: list the `tpl/themes/<Version>/` dirs, parse them as `Version`, and pick the
  **highest bundled version ≤ the target** (`LastOrDefault(v <= target)`); an empty/null target ⇒ the
  highest bundled. A target **below the lowest bundled** ⇒ `ArgumentException("Themes require Creatio
  {min} or newer; version {target} is not supported.")`. A missing template file ⇒ `InvalidOperationException`.
- **Read straight off disk.** There is no network fetch, no `~/.clio/cache` tier, and no override — the
  template is a shipped asset, read directly from the bundled folder.

`theme.json.tpl` (`{id, caption, cssClassName}`) is bundled alongside `theme.css.tpl` and filled by the
CLI workspace-output mode (`<%themeId%>`/`<%themeCaption%>`/`<%themeCssClass%>`); the MCP no-code flow
never needs it.

### D6 — Feature-toggle until the surface is complete and go-live is approved

`[FeatureToggle("theming")]` on `BuildThemeOptions` (the `[Verb]` options class) **and** on the
`[McpServerToolType]` `BuildThemeTool` (the MCP surface is gated separately — marking only the options
class leaves the tool exposed). Off by default; enable with `clio experimental --name theming
--enable`.

Rationale: the verb ships hidden while the surface is still being built out (tests, docs, the MCP tool)
and until a go-live decision. The template is **bundled in clio**, so the feature is **not** blocked on any
external producer; nothing external gates it. Flip the toggle on
(Phase 5) once the surface is complete and parity is verified.

MCP registration stays through `McpFeatureToggleFilter.RegisterEnabledPrimitives` (`IEnumerable<Type>`)
— never a `Type[]`, never `*FromAssembly`.

### D7 — Bit-exact validation: spec port + generated parity fixture + drift guard

Three layers:
1. **Ported `*.spec.ts` anchors** → NUnit in `clio.tests/Theming/` (`[Category("Unit")]`,
   `[Property("Module","Theming")]`, AAA + `because` + `[Description]`). All hard-coded expected
   values become C# assertions (full anchor list in the Test Strategy).
2. **Generated parity fixture** → a committed JSON of ≈200 random `hex → {generateScale,
   deriveSecondary, accentCandidates}` pairs + a few full `buildThemeCss` outputs, captured **once
   from the TS package**, asserted exactly in C#. Catches the binary-search / threshold drift the
   hand anchors miss; regeneration documented (run the TS package).
3. **Template/builder contract guard** + fixture `clio.tests/Theming/Fixtures/theme.css.tpl` (the
   committed copy of the bundled template):
   - **Uniform palette calibration (see R-18):** run `generateScale(-500)` on each of the five default
     anchors (`primary #004fd6`, `secondary #0d2e4e`, `accent #ff4013`, `success #0b8500`,
     `error #d2310d`) and assert C# output equals the TS golden output **identically** — no
     secondary special-case, no separate 600–900 handling. (The primary's 12 shades are the published
     spec values; the other four golden values come from the TS parity fixture.) The math reproduces
     the freedom.scss-derived *primary* ramp bit-exact (spec-confirmed); it does **not** reproduce the
     platform default theme's hand-tuned *secondary* (build-theme always generates it from
     `generateScale(deriveSecondary(primary))`), so the guard does NOT assert generateScale reproduces
     the template's baked secondary/accent values — those are sample defaults + regex targets,
     overwritten by `applyPalettes`.
   - **template/builder contract:** assert the bundled template contains every token the builder
     rewrites (all `--crt-palette-*`, the finalized `--crt-color-*` roles, `<%themeCssClass%>`, the
     strippable header comment) — a template change that breaks the fill fails here. The template is a
     bundled asset, so no cross-repo live-snapshot guard is needed.

### D8 — Guidance swap; token catalog + prose guides stay out of clio

Edit (not rewrite) `ThemingGuidanceResource.Guide`: keep "Which flow", deploy, "List themes",
"Get/set the default theme", and the `CanCustomizeBranding`/`CanManageThemes` preconditions
unchanged; replace only the npm bits (the XML summary + `[Description]`, the "Source of truth —
@creatio/theming" block, workspace step 2, no-code step 1) with "call `build-theme`". Update
`GuidanceCatalog` blurb + `docs/McpCapabilityMap.md` (resource description + new `build-theme` row).

The `--crt-*` **token catalog** is freedom.scss-derived DATA → its home is the CDN, served later via
`get-theme-tokens` (out of scope). Embedding it in clio would recreate the cross-repo drift the
Component Registry retired its in-DLL snapshot to avoid. The `THEMING_*_AI_GUIDE.md` **prose guides**:
the palette/fonts content dissolves into `build-theme`; the residual manual-restyle/descriptor
guidance serves the advanced hand-authoring path (out of scope for the conversational flow) and lives
on CDN/Academy. clio's guidance stays **lean** — a pointer to `build-theme`, nothing restated.

### D9 — Decommission `@creatio/theming` FULLY, including its component-styling use (see R-01)

Target state: **the dependency is gone, with its styling use handled** — not left in place. clio
never imported the package in C# (grep confirms: matches are docs, guidance, the scaffold dep, and
`spec/**` — no import/spawn); the agent's Node build script was its theme-build consumer. Three clio
removals:

1. **Guidance npm-fetch text** in `ThemingGuidanceResource` → "call `build-theme`" (D8).
2. **The `clio/tpl/ui-project/package.json` devDependency** (`"@creatio/theming": "^0.1000.0"`,
   [line 41](clio/tpl/ui-project/package.json)) → removed.
3. **The component-styling token-catalog reference** → rehomed (see R-01 for the exact scope, the
   verified narrowness — author-time docs only, no build/code/SCSS import — and the cross-repo +
   producer coupling).

The npm **deprecation/unpublish** is a creatio-ui workstream — out of clio's control; flag it.

### D10 — Out-of-scope seams (flagged, not designed here)

- **Upstream generator** for the `--crt-*` token catalog (freedom.scss → token catalog →
  `static-files-mcp` GitLab → academy mirror). The build-theme **template is bundled in clio** and does
  **not** depend on it; only the future token-catalog tooling does.
- **`get-theme-tokens`** — a future read-only MCP tool fetching the CDN `--crt-*` token catalog. Out
  of scope; its own cache→CDN client, a different artifact from the bundled template.

---

## Alternatives Considered

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| **Chosen: pure compute `build-theme` (CLI+MCP); math in `clio/Theming/`; template arg; bundled version-pinned template provider; feature-toggled** | Composes with both flows; math I/O-free + bit-exact testable; template ships in-tool (no network, no producer gate); mirrors `tpl/ui` | New `Theming` module trait | **Chosen** |
| Combine build+create into one mutating tool (`clio-run` dispatcher) | One call for the no-code flow | Forces env arg + by-env/by-cred duplication on a pure function; conflates compute with deploy; doesn't serve the workspace flow | Rejected (D1) |
| Embed the token catalog + prose guides in clio | Agent always has the catalog locally | Recreates the cross-repo drift the registry retired its in-DLL snapshot to avoid; clio release per token change | Rejected (D8) |
| Bundle the template but ship **un-toggled** | `build-theme` works day one | The surface (tests, docs, MCP tool) is still being built out; exposing the half-built verb is premature | Rejected in favor of bundled + feature toggle (D5/D6) |
| Fetch the template from the CDN (a dedicated client) instead of bundling | Single source of truth on the CDN | Adds a network dependency + cache/SWR/producer-gating for a small, stable, version-pinned asset that ships fine in-tool | Rejected in favor of bundling (D5) |
| Port `Math.round` as `Math.Round` / `**` as `x*x*x` | Idiomatic C# | Silently diverges from the TS calibration at hex boundaries (banker's rounding / last-ULP) | Rejected (D3) |

---

## Implementation Plan

### Files to create

| File | Purpose |
|------|---------|
| `clio/Theming/ColorSpace.cs` | `internal static`: srgb/linearize/delinearize, HexToOklch, HexToOklab, OklchToRgb, RgbToHex (`RoundHalfUp`, round-then-clamp), MaxChromaInGamut, OklchToHex, DetectMode — verbatim matrices, `Math.Pow` for `**` |
| `clio/Theming/ColorNormalizer.cs` | `Normalize` (+ internal HSL→hex); rejects alpha forms |
| `clio/Theming/PaletteGenerator.cs` | `GenerateScale`, `DeriveSecondary`, `GenerateAccentCandidates` (+ internal cusp lightness) |
| `clio/Theming/ColorMetrics.cs` | `RelativeLuminance`, `ContrastRatio`, `DistanceOklab`, `ChooseBestAccent`, `SuggestAdaptedPrimary500` |
| `clio/Theming/TextTokenResolver.cs` | `ResolveTextToken`, `ResolveLinkHover`, `ResolveTextOnColorToken` |
| `clio/Theming/FontImportBuilder.cs` | `BuildUrl`, `BuildRule` (sorted+deduped weights; family validation) |
| `clio/Theming/ThemeCssBuilder.cs` | `IThemeCssBuilder` + impl; `Build(templateCss, options)` verbatim port of `buildThemeCss` returning the `theme.css` **string**; regex fill (match timeouts) + post-fill guard |
| `clio/Theming/CssNamedColors.cs` | CSS named-colors map (the other constants live inline in their owning class) |
| (records, inline) | `BuildThemeOptions`, `FontsInput`, `FontFamilyEntry`, `AccentCandidate`/`ScoredAccentCandidate`, `AdaptedPrimary`, `TextTokenResolution`, `TextOnColorResolution` — declared in their owning files (no `Models/` folder, no `ThemingConstants.cs`, no `BuildThemeOutput`) |
| `clio/tpl/themes/{version}/theme.css.tpl`, `theme.json.tpl` | bundled, version-pinned templates (copied by the `tpl\**` content glob; `10.0` is the first bundled version) |
| `clio/Command/Theming/ThemeTemplateProvider.cs` | `IThemeTemplateProvider` (namespace `Clio.Command.Theming`); reads the version-matched bundled `theme.css.tpl`/`theme.json.tpl`; highest bundled ≤ target; too-old → `ArgumentException`, missing file → `InvalidOperationException` |
| `clio/Command/Theming/BuildThemeCommand.cs` | `BuildThemeOptions` (`[Verb("build-theme")]`, `[FeatureToggle("theming")]`) + `BuildThemeCommand : Command<BuildThemeOptions>`; local; resolve version (`--version` xor `--environment-name`) → `IThemeTemplateProvider.GetCssTemplate` → `IThemeCssBuilder.Build` → `--output`/stdout; workspace mode also writes `theme.json` |
| `clio/Command/McpServer/Tools/BuildThemeTool.cs` | `[McpServerToolType]` + `[FeatureToggle("theming")]`; flat `build-theme` tool (`ComponentInfoTool` shape — injects `IThemeCssBuilder` + `IThemeTemplateProvider`); `BuildThemeResult { success, css, descriptor, warnings?, error? }`; `ReadOnly=true/Destructive=false/Idempotent=true/OpenWorld=false`; description → `get-guidance theming` |
| `clio/help/en/build-theme.txt`, `clio/docs/commands/build-theme.md` | CLI `-H` help + GitHub docs |
| `clio.tests/Theming/*` (+ `Fixtures/{theme.css.tpl, color-math-parity.json, theme-css-golden.json}`) | ported spec anchors + `ColorMathParityTests` (frozen TS goldens) + the template/builder contract guard |
| `clio.tests/Command/ThemeTemplateProviderTests.cs` | version pick: highest ≤ target / highest-when-empty / too-old → error / missing-file → error |
| `clio.tests/Command/BuildThemeCommandTests.cs`, `clio.tests/Command/McpServer/BuildThemeToolTests.cs` | command/tool mapping, output modes (stdout / dir theme.css+theme.json), version resolution, feature-toggle gating, flags |
| `clio.mcp.e2e/BuildThemeToolE2ETests.cs` | tool advertised by real `clio mcp-server` (toggle enabled in harness; bundled template) — NOT in CI |

### Files to modify

| File | Change |
|------|--------|
| `clio/BindingsModule.cs` | register `IThemeCssBuilder`, `IThemeTemplateProvider`, `BuildThemeCommand`, `BuildThemeTool` (**full-suite trigger**) |
| `clio/Program.cs` | add `typeof(BuildThemeOptions)` to the `CommandOption` list + a dispatch arm (**full-suite trigger**) |
| `clio/Command/McpServer/Resources/ThemingGuidanceResource.cs` | swap npm instructions for `build-theme` (D8); keep all other sections |
| `clio/Command/McpServer/Resources/GuidanceCatalog.cs` | update the `theming` blurb (drop `@creatio/theming`) |
| `docs/McpCapabilityMap.md`, `clio/Commands.md`, `clio/Wiki/WikiAnchors.txt` | add `build-theme` (verb + tool) + updated theming-resource description |
| `clio/tpl/ui-project/package.json` | remove `"@creatio/theming": "^0.1000.0"` (line 41) — full removal (R-01) |
| `clio/tpl/ui-project/AGENTS.md` | repoint the component-styling `--crt-*` token-catalog reference to the catalog's rehomed CDN/Academy location (R-01); cross-repo: coordinate the `@creatio-devkit/common` REMOTE_COMPONENT_STYLING guide repoint |
| `clio.tests/Command/McpServer/GuidanceGetToolTests.cs` (+ `GuidanceGetToolE2ETests.cs`) | update expectations: `build-theme` referenced, npm not |
| `CLAUDE.md`, `AGENTS.md` | add the `Theming` module trait to the smart-regression map |

### Files to delete

None in clio (additive + edits). The `@creatio/theming` package itself is deprecated by creatio-ui.

### CLI flag specification (all kebab-case — CLIO001)

| Flag | Required | Notes |
|------|----------|-------|
| `--primary` | Yes | brand primary hex (or rgb()/hsl()/named — `ColorNormalizer.Normalize`) |
| `--secondary`, `--accent`, `--success`, `--error` | No | derived/defaulted when omitted |
| `--css-class-name` | Yes | `^[A-Za-z][A-Za-z0-9_-]*$`, ≤100 |
| `--heading-font`, `--body-font`, `--font-weights` | No | default Montserrat 400/500/600 ⇒ no `@import` |
| `--id`, `--caption` | No | written to `theme.json` (dir mode); auto-UUID / `--css-class-name` when omitted |
| `--version`, `--environment-name` | No | pick the version-matched bundled template; mutually exclusive; neither ⇒ highest bundled |
| `--output` | No | dir ⇒ writes `theme.css` + `theme.json`; omitted ⇒ stdout |

### Test strategy

| Layer | Framework | Coverage | File(s) |
|-------|-----------|----------|---------|
| Unit (math) | NUnit/FluentAssertions | every ported spec anchor: `normalizeHex` (#FFF→#ffffff, 004FD6→#004fd6, rgb/hsl/named, alpha-reject); `generateScale('#004fd6')` 12 shades; `deriveSecondary('#004fd6')==#0d2e4e`; `accentCandidates` +135 `#f94e11` / `chooseBestAccent`; `suggestAdaptedPrimary500('#cccccc')==#949494`/`('#000000')==null`; luminance/contrast/distance anchors; `resolveTextToken`/`resolveLinkHover`/`resolveTextOnColorToken`; google-fonts URLs; `buildThemeCss` contains/not-contains + the three throw cases | `clio.tests/Theming/*` |
| Unit (parity) | NUnit | committed TS-generated fixture matches C# exactly | `clio.tests/Theming/ColorMathParityTests.cs` |
| Unit (parity/contract) | NUnit | frozen TS goldens + freedom.scss `-500` reproduction + template/builder contract | `clio.tests/Theming/ColorMathParityTests.cs` + the contract guard |
| Unit (template provider) | NUnit | version pick: highest ≤ target / highest-when-empty / too-old → error / missing-file → error | `clio.tests/Command/ThemeTemplateProviderTests.cs` |
| Unit (surface) | `BaseCommandTests<BuildThemeOptions>` + NSubstitute | command output modes, tool arg mapping, feature-toggle gating, safety flags, description → `get-guidance theming` | `clio.tests/Command/{BuildThemeCommandTests,McpServer/BuildThemeToolTests}.cs` |
| E2E (MCP) | `clio.mcp.e2e` (NOT in CI — manual) | `build-theme` advertised by real `clio mcp-server`; valid `theme.css` produced over the protocol | `clio.mcp.e2e/BuildThemeToolE2ETests.cs` |
| Manual (end-to-end) | runbook | no-code: build-theme → create-theme → list → set DefaultTheme; workspace: build-theme --output → push-workspace → list | Manual |

Full-suite trigger (smart-regression rule 4): `BindingsModule.cs` + `Program.cs` change ⇒ run the
full `Category=Unit` suite in addition to targeted `Module=Theming|McpServer|Command`.

---

## Consequences

- **Positive**
  - Agents stop shelling out to Node; theme CSS is produced in-process, deterministically, bit-exact
    with the retired package; the template ships bundled in clio (no network dependency).
  - Pure compute tool composes with both the workspace/dev and no-code/server flows unchanged; the
    theme CRUD + `DefaultTheme` activation are untouched.
  - Retires clio's coupling to the npm package; the `--crt-*` catalog stays single-sourced on the CDN
    (no cross-repo drift).
- **Trade-offs / risks**
  - **Float parity** (matrices, `Math.round` banker's, `**`, binary searches) — mitigated by
    operator-for-operator port + bit-exact anchors + the generated parity fixture.
  - **Regex parity** (template fill, comment strip, `normalizeHex`) — port verbatim with match
    timeouts (S6444); covered by the builder tests.
  - **Template/builder coupling** — a template change can break the regex fill; the contract
    + reproduction guards + the post-fill runtime assertion fail loudly. The template is bundled, so a
    change lands in the same repo/PR (no cross-repo drift).
- **Breaking change**: No. Additive verb + tool (toggled off by default), edits to guidance/docs, and
  removal of a scaffold devDependency. A `RELEASE.md` note suffices once the toggle ships on.

## Open questions

1. **OQ-01 — RESOLVED.** The template is bundled in clio under `tpl/themes/{version}/` (D5).
2. **OQ-02 — RESOLVED.** The feature key is **`theming`** (not `build-theme`): one key gates the entire
   theming surface — `build-theme` **plus** the theme CRUD (`create-theme`/`update-theme`/`delete-theme`/
   `list-themes`/`clear-themes-cache`) and `ThemingGuidanceResource` — each carrying `[FeatureToggle("theming")]`
   on its options class **and** its MCP tool/resource type. Enable with `clio experimental --name theming --enable`.
   Go-live trigger = surface complete + parity verified.
3. **OQ-03 — RESOLVED.** `build-theme` emits `theme.json` in CLI workspace-output (dir) mode from the
   bundled `theme.json.tpl`; the MCP no-code flow does not.
4. **OQ-04 — RESOLVED.** No shared text-fetch helper is needed — the template is read from the bundled asset (D5).

## Pre-implementation Checklist

- [ ] `clio/Theming/` math is `internal static`; `IThemeCssBuilder` is the only DI behavior class; `Theming` module trait added to `CLAUDE.md`/`AGENTS.md`
- [ ] `Build(templateCss, options)` takes the template as an argument — no disk read in the math
- [ ] `RoundHalfUp` (JS `Math.round` semantics, round-then-clamp; not `Math.Floor(x+0.5)`); `Math.Pow` for `**`; `double` throughout
- [ ] template-fill regexes ported verbatim **with a match timeout** (S6444); post-fill no-`<%`/all-steps guard
- [ ] `IThemeTemplateProvider` reads the bundled `tpl/themes/{version}/`; highest bundled ≤ target; too-old → `ArgumentException` (CLI error / MCP `success:false`)
- [ ] `[FeatureToggle("theming")]` on BOTH the options class and the `[McpServerToolType]`; MCP via `RegisterEnabledPrimitives` (`IEnumerable<Type>`)
- [ ] `build-theme` flags: `ReadOnly=true/Destructive=false/Idempotent=true/OpenWorld=false`; description → `get-guidance theming`
- [ ] bit-exact: ported spec anchors + generated parity fixture + freedom.scss reproduction/template-contract guard
- [ ] `ThemingGuidanceResource` npm text swapped for `build-theme`; token catalog NOT restated; other sections kept
- [ ] `clio/tpl/ui-project/package.json` `@creatio/theming` dependency removed
- [ ] docs (`help/en`, `docs/commands`, `Commands.md`, `Wiki/WikiAnchors.txt`, `McpCapabilityMap.md`) updated; `GuidanceGetTool` tests updated
- [ ] full unit suite run (BindingsModule/Program touched) + targeted `Module=Theming|McpServer|Command`
- [ ] `clio.mcp.e2e` `build-theme` coverage added (NOT in CI — manual)
- [ ] `.codex/workspace-diary.md` entry appended

---

## Post-review refinements (bmad-reviewer, 2026-06-26)

Three parallel adversarial reviewers (parity, architecture, gaps/rollout) reviewed this ADR against
the actual code. These refinements are **binding and authoritative — they supersede any conflicting
text in the decisions above**. The implementer and story-writer treat them as the source of truth.
One item (**R-12**) needs a product-owner decision before stories are written.

### Decommission — full removal of `@creatio/theming` (re-opened scope)

**R-01 — Remove the `@creatio/theming` scaffold dependency FULLY and handle its styling use
(supersedes the earlier "keep the dep" resolution; corrects D9).** The review correctly flagged that
the dependency is consumed by **component styling**, not only theme authoring — so this is *not*
guidance-text-only. Investigation (verified against the code) scoped exactly what styling pulls and
how big the migration is:

**What component styling actually consumes — narrow, and author-time only:**
- The scaffold's `clio/tpl/ui-project/AGENTS.md` points to
  `node_modules/@creatio-devkit/common/AI_GUIDES_INDEX.md`; that devkit guide's REMOTE_COMPONENT_STYLING
  entry references the `--crt-*` **design-token catalog**, which is documented in
  `@creatio/theming`'s `THEMING_DESIGN_TOKENS_AI_GUIDE.md`. So the consumption is **author-time
  reference documentation** (an AI agent learning which `--crt-*` token names exist when styling a
  component).
- **No code/SCSS/build import.** `clio/tpl/ui-project/src/styles.scss` is empty; the scaffold's `.ts`
  files import only from `@creatio-devkit/common`, never `@creatio/theming` (verified). The `--crt-*`
  CSS variables themselves are **platform `:root` runtime primitives** shipped by Creatio, not by the
  npm package. **Removing the devDependency does not break the Angular build or runtime rendering** —
  `npm i` still succeeds and `--crt-*`-referencing component styles still resolve at runtime.

**Migration to remove it (target state: gone, styling handled):**
1. Remove `"@creatio/theming"` from `clio/tpl/ui-project/package.json` devDependencies (line 41) — 1 line.
2. Rehome the token-catalog *reference* for component styling. Two parts:
   - **(clio, small)** add an explicit `--crt-*` token-catalog pointer to the scaffold `AGENTS.md` (or
     a styling guidance resource) directing component styling to the catalog's surviving home — the
     **CDN/Academy** (same destination as the future `get-theme-tokens`), per
     the project decision that the token catalog does NOT live in clio.
   - **(cross-repo, flag)** `@creatio-devkit/common`'s REMOTE_COMPONENT_STYLING guide carries the
     transitive pointer into `@creatio/theming`; clio cannot edit it. Coordinate with the devkit team
     to repoint it at the rehomed catalog. This is the real coupling.
3. Verify component styling still works: `new-ui-project` scaffolds + `npm i` succeeds without the
   dep; a sample component referencing `--crt-*` tokens renders (tokens resolve from platform `:root`);
   the scaffold `AGENTS.md` styling path resolves to a token-catalog source.

**Size of the styling-migration:** clio-side **SMALL** — a 1-line dependency removal plus ~15–25 lines
of scaffold guidance to repoint the token-catalog reference. It is **not** a build-pipeline/SCSS
migration (nothing in the scaffold imports the package). The binding cost is **not in clio**: it is the
**cross-repo devkit-guide repoint** + the **token catalog's rehoming to CDN/Academy**, which is the
same out-of-scope producer workstream as the theme template (and the future `get-theme-tokens`).

**Coupling/gating:** because "styling use handled" means the token catalog must have a surviving online
home, the dependency removal is **gated on the catalog rehoming** (CDN/Academy) — the same producer
dependency family as R-12. Removing the dep *before* the catalog is rehomed leaves an **author-time
documentation gap** (the agent loses local `node_modules/@creatio/theming/THEMING_DESIGN_TOKENS_AI_GUIDE.md`).
This is tracked as its own **styling-migration story**, blocked on the token-catalog rehoming +
the devkit-guide repoint. (The earlier devflow ADR D3 / PRD CAP-04 "keep the dep" rationale is
explicitly superseded: the dep is being retired with its styling use migrated, not preserved.)

### Architecture (correct before stories — these change the tool shape, arg surface, and file lists)

**R-02 — `build-theme` MCP tool is a FLAT, constructor-injected tool (corrects D1/D6/Files).** The
"local-options path through `BaseTool`" claim is false: the cited prior art (`create-workspace`,
`new-ui-project`/`CreateUiProjectTool`) are all `BaseTool<EnvironmentOptions-subtype>` tools, and
`BaseTool.ResolveFromCallContainer` (`BaseTool.cs:107-134`) throws for any non-`EnvironmentOptions`
type. The correct prior art is **`ComponentInfoTool`** (`ComponentInfoTool.cs:28`): a
`[McpServerToolType]` that does **not** derive from `BaseTool` and constructor-injects its
collaborators. `BuildThemeTool` follows that shape — inject `IThemeCssBuilder` + `IThemeTemplateProvider`
directly, return `BuildThemeResult`, no command/resolver/`BaseTool` machinery, **no `BaseTool.cs`
edit**. The MCP `AGENTS.md` "prefer `BaseTool` unless a strong reason" rule is satisfied — a pure
compute tool with no command and no environment is that reason, and `ComponentInfoTool` already
exercises the exemption. (The CLI `BuildThemeCommand` may still be a local `Command<BuildThemeOptions>`,
but verify how local/no-environment commands are dispatched in `Program.cs` rather than assuming the
`EnvironmentOptions` path.)

**R-03 — Template version resolution must be designed, not deferred (corrects D1/D5).** The bundled
templates are version-pinned, so `build-theme` must resolve which one to use. It takes **optional
`--version` (explicit) XOR `--environment-name`** (CLI + MCP): explicit wins; `--environment-name`
resolves the version from that environment (`ISettingsRepository.FindEnvironment` →
`IPlatformVersionResolverFactory`/`PlatformVersionResolver`; an unresolvable/`latest` result ⇒ highest
bundled); **both ⇒ a mutually-exclusive error; neither ⇒ the highest bundled** template. The provider then
picks the highest bundled version ≤ the resolved target. Reconcile D1's "does not touch an environment"
wording accordingly (it touches an environment only to *resolve the version*, never to mutate).

**R-04 — SUPERSEDED by the 2026-06-30 bundled-template pivot (D5).** The template is a bundled
`tpl/themes/{version}/` asset read by `IThemeTemplateProvider`, so the earlier "standalone client vs shared
helper" question is moot.

**R-05 — SUPERSEDED by the 2026-06-30 bundled-template pivot (D5).** No network fetch, so there is no
staleness/SWR policy — the bundled template is read directly off disk.

**R-06 — DI/CLIO005 note retargeted (corrects D2).** CLIO005 fires on unresolved *registrations*, not
on `internal static` math. The real surface is the new registrations (`IThemeCssBuilder`,
`IThemeTemplateProvider`); ensure each has a statically-visible injection site (the gated tool/command
counts). Keep `IThemeCssBuilder` as `AddSingleton` justified by **`Build(templateCss, options)` being a
pure function taking all inputs as args and holding no fields** (note the asymmetry with the
`AddTransient` sibling theme commands so reviewers aren't surprised). All `clio.tests/Theming/` tests
carry `[Category("Unit")]` **and** `[Property("Module","Theming")]` (no uncategorized tests).

### Parity (correct before/within the math story — these are hex-flipping divergences)

**R-07 — Rounding recipe is wrong (corrects D3).** `Math.Floor(x + 0.5)` is NOT JS `Math.round`:
for `x = 0.49999999999999994`, `x + 0.5 == 1.0` (FP error) so `Floor` gives `1`, while JS
`Math.round` gives `0`. Implement JS `Math.round` semantics precisely (round half toward +∞ with the
ULP guard), and pin the **order**: in `rgbToHex`, **round THEN clamp** (`color-space.ts:60-66`), not
clamp-then-round. Add a parity anchor at the `0.49999999999999994`-class input.

**R-08 — `Math.Pow`/`Math.Cbrt` are NOT bit-guaranteed across V8↔.NET — this is the single highest
parity risk and is currently treated as solved (corrects D3/D7).** For fractional exponents (`2.4`,
`1/2.4`, `0.8`) and `cbrt`, V8 (fdlibm) and .NET libm can differ in the last ULP, and these feed the
entire OKLab pipeline. "Use `Math.Pow`/`Math.Cbrt`" gives operator semantics, **not** bit-exact
results. Therefore: **the generated parity fixture (D7 layer 2) is the load-bearing gate and is
MANDATORY, broad, and adversarially seeded** (not "≈200 random hex") — include inputs whose channels
land near `x.5·255` rounding boundaries, near-gamut-boundary chromas, and the `detectMode`/contrast
threshold boundaries. **Decide the fallback now** (run a real parity spike during the math story): if
the fixture shows ULP divergence, either port fdlibm `pow`/`cbrt` or document an explicit tolerance —
do not discover this at the end. This is a **spike to run first**, gating the rest of the math story.

**R-09 — Numeric culture + hex parsing (corrects D3).** Add a hard rule: **all numeric parse/format in
`Clio.Theming` uses `CultureInfo.InvariantCulture`; no implicit `ToString()`/`Parse` on numbers**
(else `de-DE` etc. break font-weight joining and `rgb()/hsl()` parsing in the field while passing on an
invariant dev box). Replace `Convert.ToInt32(s,16)` with `byte.Parse(s, NumberStyles.HexNumber,
CultureInfo.InvariantCulture)` and document that `srgbChannels` assumes a normalized 6-hex input
(`Number.parseInt` tolerates garbage / yields `NaN`; `Convert.ToInt32` throws — divergent on malformed
fixture inputs).

**R-10 — Regex porting hazards (corrects D4).** (a) JS `String.replace(regex)` **without `g` replaces
only the first match**; .NET `Regex.Replace` replaces all — use the **count-1 overload** for every
non-global JS replace (`applyPalettes`, `setColorDeclaration`). (b) For replacements carrying user
input (font family in `applyFonts`), use a **`MatchEvaluator`**, not raw string interpolation —
`$`-substitution tokens differ between JS and .NET. (c) Use **`\z`, not `$`**, for end-of-string in the
`THEME_CSS_CLASS_PATTERN` and similar — .NET `$` matches before a trailing `\n` (JS `$` does not), so
`"Foo\n"` would wrongly pass; add that anchor. (d) Comment strip: keep `[\s\S]` verbatim, **no anchor**,
match `\n` only (never `\r?\n` — preserve any stray `\r` to stay bit-exact); do **not** add
`RegexOptions.Multiline` or `RegexOptions.ECMAScript` (ECMAScript does NOT make .NET match JS and
changes `\d`/`\w`/`\s`). (e) S6444 match-timeouts apply to **regex** steps only — `replaceAll('<%themeCssClass%>',…)`
is a literal `string.Replace` (no regex, no timeout); and note the S6444 prior art lives in
theme-service/section-palette code, not the theme tools.

**R-11 — Stable sort + float-accumulation loops (corrects D3).** (a) JS `Array.sort` is stable; C#
`List.Sort`/`Array.Sort` are not — use **`OrderByDescending`** in `chooseBestAccent` and preserve the
original candidate order on ties (compare `double`s, never cast to `int`); add a tie-break anchor. (b)
Reproduce `cuspL` and **`suggestAdaptedPrimary500`** with the exact `double` `+=`/`-=` accumulation,
start, step, bound, and comparison — do **not** refactor to index arithmetic (`0.35 + i*0.01`), which
evaluates at different points and flips hexes. Add `suggestAdaptedPrimary500`'s loop to the
precision-sensitive list. (c) The gamut test `channel >= -0.001 && channel <= 1.001`
(`color-space.ts:74`) is the most precision-sensitive comparison in the library — pin the literals and
operators exactly; it was missing from D3's list. (d) Complete the anchor set: all three
`accentCandidates` (`#f94e11`/`#d29a16`/`#87b716`), `normalizeHex('hsl(217,100%,42%)')==#0052d6`, and
`detectMode`/threshold **boundary** anchors (`l=0.38`/`0.78`, `h=80`/`105`, `c=0.06`). Pin the
named-colors lookup as lowercase-then-ordinal-exact and compare `applyFonts`' Montserrat default
**case-sensitively** (ordinal).

### Rollout / process

**R-12 — REVISED 2026-06-30 (rollout): toggle only, dark until the surface is complete.** The original
"dark until the producer publishes" decision is **reversed by the bundled-template
pivot (D5)** — the template now ships **inside** clio, so nothing external gates the feature.
`[FeatureToggle("theming")]` stays the sole gate; the verb is hidden until the surface (tests, docs,
MCP tool) is complete and go-live is approved (Phase 5). Consequences:
- **The template is a shipped asset** under `tpl/themes/{version}/`; tests/dev use it directly.
- **No blocking producer dependency.** The Phase 5 "flip the toggle" story is gated only on the clio
  surface being complete + parity verified, not on any creatio-ui producer ticket.
- Phases 1–5 are all clio-internal; nothing is externally gated.

**R-13 — Produce the BMAD Phase 4 test plan.** Add `spec/test-plans/tp-theming-native-build.md` (the
siblings have one) with TC-U-*/TC-I-* cases, the full-suite trigger, and an explicit "MCP E2E NOT in
CI — manual" flag, before stories leave `ready-for-dev`.

**R-14 — Output may exceed the downstream 1 MiB cap.** `build-theme` output feeding `create-theme` can
exceed the 1 MiB `cssContent` cap (custom fonts / a larger producer template). That cap is enforced in
`create-theme`; `build-theme` itself does not pre-check or warn.

**R-15 — Drift-guard + parity-fixture provenance.** (a) The bundled `tpl/themes/{version}/theme.css.tpl`
(and its committed test copy `Fixtures/theme.css.tpl`) must be the **actual creatio-ui freedom.scss-derived
template**, or the reproduction guard means nothing; since it is now bundled in-repo, a change lands in the
same PR (no cross-repo live-snapshot guard exists or is needed). (b) The parity goldens were generated
**once** from the TS source and are **frozen** — NOT regenerable once `@creatio/theming` is deleted
(`freedom.scss` carries the default values, not the `generateScale` algorithm); the C# port is the authority.

**R-16 — Guidance-swap test breakage is enumerated.** D8's swap breaks committed assertions in
`GuidanceGetToolTests.cs` (the `@creatio/theming` / `palette engine` `Contain` assertions and the
`--crt` count guard) and the not-in-CI `GuidanceGetToolE2ETests.cs`; the story must enumerate each
changed assertion and the new contract (`build-theme` present, npm/palette-engine removed), and flag
the E2E-not-in-CI gap. Also update the `GuidanceCatalog` blurb + `McpCapabilityMap` string together
(add a drift test or accept the gap explicitly). Clarify in D8 that the workspace flow now gets
`theme.json` from `build-theme --output` workspace mode (no package template remains).

**R-17 — Safety-flag semantics.** State that `ReadOnly`/`Idempotent` on `build-theme` describe
**environment** effects; reading the bundled template and the optional `--environment-name` version probe
are read-only and outside the flag's scope (mirror the `get-component-info` wording).

### Palette verification (product-owner correction, 2026-06-26)

**R-18 — Verify all five palettes uniformly; drop the secondary special-case.** The parity/calibration
tests assert `generateScale(-500)` for `primary`, `secondary`, `accent`, `success`, and `error`
**identically** — same code path, same full 10-shade comparison, **no separate 600–900 handling** for
the secondary. Rationale: `build-theme` always generates the secondary algorithmically
(`generateScale(deriveSecondary(primary))`) and **never** reproduces the platform default theme's
hand-tuned secondary, so a special-case that reconciles generateScale against the template's hand-tuned
secondary darker shades is both unnecessary and wrong — uniform verification against the TS golden
output is correct and simpler. Corrects D7's earlier "for each default -500 … reproduces the template's
12 shades" framing, which only holds for the primary (spec-confirmed); the template's baked
secondary/accent are sample defaults + regex targets, fully overwritten by `applyPalettes`, not a
reproduction target.

### Items deliberately deferred

The token-catalog producer-side generator and `get-theme-tokens` remain out of scope (D10). The
earlier remote-fetch template design (D5/R-04/R-05) is retired — the build-theme template is bundled in clio.
