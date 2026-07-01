# Test Plan: Native C# theme building in clio (`build-theme` + bundled template)

**Feature**: theming-native-build (ENG-90636, epic ENG-26797 — Theming with AI, clio dev flow; native-build + bundled-template continuation)
**Stories**: to be written (BMAD Phase 3) — `spec/stories/story-theming-native-build-*.md` (not yet created)
**PRD**: design substance in the approved migration plan ([adr-theming-native-build.md](../adr/adr-theming-native-build.md) §Context); promote to `spec/prd/prd-theming-native-build.md` if a separate PRD is required.
**ADR**: [adr-theming-native-build.md](../adr/adr-theming-native-build.md) — **Accepted**, revised by the bmad-reviewer pass (R-01 … R-18). The refinements are **authoritative and supersede the pre-review body**; this plan reflects the refinements.
**Author**: QA Planner Agent
**Status**: Draft
**Created**: 2026-06-26

---

## Scope

### In scope
- The **C# color-math port** in `clio/Theming/` (`Clio.Theming`): `normalizeHex`, `generateScale`, `deriveSecondary`, `accentCandidates`, `chooseBestAccent`, `suggestAdaptedPrimary500`, `relativeLuminance`, `contrastRatio`, `distanceOklab`, `resolveTextToken`, `resolveLinkHover`, `resolveTextOnColorToken`, `googleFontsImportUrl`/`Rule`, and the `IThemeCssBuilder.Build(input, templateCss)` orchestrator (verbatim port of `buildThemeCss`) — proven **bit-exact at the hex level** against the creatio-ui TS package.
- The **parity gate** (R-08): the seeded, adversarially-seeded parity fixture that proves `Math.Pow`/`Math.Cbrt`-driven OKLab output matches V8 bit-for-bit, plus the pre-decided fallback (port fdlibm `pow`/`cbrt` OR a documented tolerance) if it shows ULP drift.
- **Uniform five-palette verification** (R-18): `generateScale(-500)` on `primary`/`secondary`/`accent`/`success`/`error` asserted **identically** against TS golden output — no secondary special-case, no separate 600–900 handling.
- The bundled **`IThemeTemplateProvider`** (D5/R-03): reads version-pinned `clio/tpl/themes/{version}/theme.css.tpl` + `theme.json.tpl` (shipped via the `tpl\**` content glob); picks the **highest bundled version ≤ target** (empty ⇒ highest bundled); too-old target → `ArgumentException`, missing template file → `InvalidOperationException`; optional `--version` XOR `--environment-name` version resolution (neither ⇒ highest bundled).
- The **`build-theme` CLI verb** (`Command<BuildThemeOptions>`; output modes incl. workspace `theme.css` + `theme.json`) and the **flat `ComponentInfoTool`-style MCP tool** `BuildThemeTool` (R-02 — NOT `BaseTool`), `BuildThemeResult { success, css, descriptor, warnings?, error? }`, safety flags `ReadOnly=true/Destructive=false/Idempotent=true/OpenWorld=false`.
- **Feature-toggle gating** (R-12/D6): `[FeatureToggle("theming")]` on **both** `BuildThemeOptions` and the `[McpServerToolType] BuildThemeTool` — dark by default until the surface (tests/docs/MCP tool) is complete and go-live is approved (templates ship in clio; not producer-gated).
- The **guidance swap** (D8) and the **enumerated breakage** of committed `GuidanceGetToolTests` assertions (R-16).
- The **decommission / styling-migration verification** (R-01): `new-ui-project` scaffolds + `npm i` succeeds without `@creatio/theming`; `--crt-*` tokens resolve from platform `:root`; scaffold `AGENTS.md` styling path resolves to a token-catalog source.
- The downstream 1 MiB cap, enforced in `create-theme` (R-14); `theme.json.tpl` is a bundled, version-pinned template filled by CLI dir-output mode (R-15).

### Out of scope (with reason)
- **The upstream `--crt-*` token catalog rehoming** (freedom.scss-derived) and the **cross-repo devkit-guide repoint** (R-01) — external creatio-ui workstreams. The `build-theme` template itself is bundled in clio, not produced externally.
- **`get-theme-tokens`** — a future read-only MCP tool for the token catalog (D10), a separate artifact from the bundled template.
- **Go-live (flip `[FeatureToggle("theming")]` on)** — deferred until the surface (tests/docs/MCP tool) is complete and approved (R-12); all phases are fully testable with the toggle off via the bundled template.
- **Theme activation / CRUD behavior** — `create-theme(-by-*)`, `list-themes`, `set DefaultTheme` are untouched (ENG-90636/ENG-91387); they appear only as **regression guard** and in the manual runbook (build-theme composes with them as a pure pipe — D1).
- **The npm `@creatio/theming` deprecation/unpublish** itself — a creatio-ui workstream out of clio's control (D9); flagged, not tested here.

---

## Regression Scope (smart-regression policy)

The change touches **two full-suite triggers** (smart-regression rule 4): `clio/BindingsModule.cs` (DI composition root — registers `IThemeCssBuilder`, `IThemeTemplateProvider`, `BuildThemeCommand`, `BuildThemeTool`) and `clio/Program.cs` (dispatch chokepoint — adds `typeof(BuildThemeOptions)` to `CommandOption` + a dispatch arm). The full `Category=Unit` suite is therefore **mandatory** in addition to the targeted module filter.

A new **`Theming`** module trait (`[Property("Module","Theming")]`) is added and mapped in the smart-regression tables of `CLAUDE.md`/`AGENTS.md` (`Theming` → `clio/Theming/`). All `clio.tests/Theming/` tests carry **`[Category("Unit")]` AND `[Property("Module","Theming")]`** (R-06 — no uncategorized tests).

```bash
# Targeted (run first — fast feedback on the changed modules)
dotnet test clio.tests/clio.tests.csproj \
  --filter "Category=Unit&(Module=Theming|Module=McpServer|Module=Command)"

# Parity gate alone (run FIRST during the math story — see Phase 0)
dotnet test clio.tests/clio.tests.csproj \
  --filter "Category=Unit&Module=Theming"

# Full unit suite (MANDATORY — BindingsModule.cs + Program.cs touched, rule 4)
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit"
```

MCP E2E (`clio.mcp.e2e/`) is **mandatory per repo policy (AGENTS.md MCP maintenance policy)** but is **NOT in CI** — run it manually (see TC-E2E-* and the PR checklist gate below). The toggle is enabled in the E2E harness and the template comes from the bundled `tpl/themes/{version}/` asset.

---

## Risk Assessment

> Lead risk: the **`Math.Pow`/`Math.Cbrt` cross-runtime parity (libm)** risk (R-08).

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| **`Math.Pow`/`Math.Cbrt` are NOT bit-guaranteed V8↔.NET (R-08)** — fractional exponents (`2.4`, `1/2.4`, `0.8`) and `cbrt` feed the whole OKLab pipeline; a one-ULP drift flips a hex. Currently treated as "solved" by "use `Math.Pow`". | **High** | **High** | **Phase 0 parity spike runs FIRST and gates the rest of the math story** (TC-U-P01..P03). The generated parity fixture is **mandatory, broad, adversarially seeded** (near `x.5·255` rounding boundaries, near-gamut chromas, `detectMode`/contrast thresholds) — **not** "≈200 random hex". **Fallback decided up front:** if the fixture shows ULP divergence, either port fdlibm `pow`/`cbrt` or document an explicit tolerance — do not discover it at the end. |
| **Dark until go-live (R-12)** — the feature is hidden by the toggle until the surface (tests/docs/MCP tool) is complete and go-live is approved. The template is bundled, so there is **no** producer dependency. | Med | Low | Toggle-only (`[FeatureToggle("theming")]` on options class AND tool type). All phases fully testable with the toggle off via the bundled template (TC-U-* / TC-I-*). Go-live (flip the toggle) tracked as a pending story in `sprint-status.yaml`. |
| **Rounding recipe wrong (R-07)** — `Math.Floor(x+0.5)` is NOT JS `Math.round`: at `x = 0.49999999999999994`, `x+0.5 == 1.0` so `Floor`→1 while JS→0. Order also matters (round THEN clamp). | Med | High | TC-U-04 pins the `0.49999999999999994`-class anchor + the round-then-clamp order (`color-space.ts:60-66`); the parity fixture seeds inputs landing on `x.5·255` boundaries. |
| **Gamut-epsilon + float-accumulation drift (R-11)** — the `channel >= -0.001 && channel <= 1.001` test (`color-space.ts:74`) is the most precision-sensitive comparison; `cuspL` + `suggestAdaptedPrimary500` `+=`/`-=` loops flip hexes if refactored to index arithmetic. | Med | High | TC-U-09 pins the epsilon literals/operators; TC-U-06/07 reproduce the loops with exact `double` accumulation, start/step/bound; the parity fixture seeds near-gamut chromas. |
| **Stable-sort tie-break (R-11a)** — JS `Array.sort` is stable; C# `List.Sort`/`Array.Sort` are not, so `chooseBestAccent` can pick a different equal-score candidate. | Med | Med | TC-U-08 uses `OrderByDescending` semantics, preserves original order on ties (compare `double`s, never cast to `int`), and asserts a deliberate tie-break anchor. |
| **CultureInfo leakage (R-09)** — implicit `ToString()`/`Parse` on numbers break `rgb()/hsl()` parsing + font-weight joining under `de-DE` while passing on an invariant dev box. | Med | High | TC-U-03 runs the parse/format anchors under a non-invariant culture (`de-DE`) and asserts `CultureInfo.InvariantCulture` everywhere; hex parse uses `byte.Parse(..., NumberStyles.HexNumber, InvariantCulture)`. |
| **Regex porting hazards (R-10)** — JS `replace(regex)` w/o `g` replaces only the FIRST match (.NET replaces all); `$` matches before a trailing `\n` in .NET; `$`-substitution tokens differ; ECMAScript/Multiline change `\d`/`\w`/`\s`. | Med | High | TC-U-15/16 assert count-1 `Regex.Replace`, `\z` (not `$`) end-anchor (`"Foo\n"` rejected), `MatchEvaluator` for user-input (font family), `[\s\S]` comment strip matching `\n` only, no `Multiline`/`ECMAScript`. Match-timeouts on regex steps only (literal `<%themeCssClass%>` is `string.Replace`). |
| **Template/builder coupling (D4/D7)** — a producer template that drifts from the builder's regex contract emits a broken theme (leftover `<%…%>` / un-substituted palette step). | Med | Med | TC-U-17 (post-fill runtime guard: no `<%…%>` remains, every palette step substituted → clear error); TC-U-P05 (template/builder contract: fixture contains every token the builder rewrites). |
| **Drift-guard circularity / fixture provenance (R-15)** — the bundled `tpl/themes/{version}/theme.css.tpl` (and its committed test copy) must be the ACTUAL creatio-ui freedom.scss-derived template, not a clio hand-bless, or the reproduction guard means nothing (R-15a). The parity goldens are **frozen** — generated once from the TS source, NOT regenerable after `@creatio/theming` is deleted (R-15b). | Med | Med | TC-U-P04 documents the fixture provenance + frozen-goldens reality; the bundled template is an in-repo asset, so no cross-repo live-snapshot guard is needed. |
| **Guidance-swap test breakage (R-16)** — D8's swap breaks committed `Contain("@creatio/theming")`, `Contain("palette engine")`, `NotContain("not yet available")`, and the `--crt` count guard in `GuidanceGetToolTests.cs` + the not-in-CI `GuidanceGetToolE2ETests.cs`. | High | Med | TC-U-19..21 enumerate each changed assertion + the new contract (`build-theme` present, npm/palette-engine removed); `GuidanceCatalog` blurb + `McpCapabilityMap` updated together (TC-U-22 drift). |
| **MCP `IEnumerable<Type>` registration regressed to `Type[]` / `*FromAssembly` → tool silently registers nothing** (project-context, D6). | Low | High | TC-U-13 asserts toggle gating through `McpFeatureToggleFilter.RegisterEnabledPrimitives`; TC-E2E-01 (manual) is the **only** check that the real `mcp-server` actually advertises `build-theme` when the toggle is on. |
| **Flat-tool shape regression (R-02)** — implementing `BuildThemeTool` on `BaseTool` would throw (`BaseTool.ResolveFromCallContainer` rejects non-`EnvironmentOptions`); the correct prior art is `ComponentInfoTool` (constructor-injected, no `BaseTool`). | Low | High | TC-U-11 asserts the tool constructor-injects `IThemeCssBuilder` + `IThemeTemplateProvider` and does not derive from `BaseTool`; no `BaseTool.cs` edit. |
| **Output exceeds the downstream 1 MiB cap (R-14)** — `build-theme` CSS feeding `create-theme` can exceed the 1 MiB cap. | Low | Med | The cap is enforced in `create-theme` (a clear failure there); `build-theme` does not pre-check or warn. |
| **Styling-migration author-time gap (R-01)** — removing the `@creatio/theming` devDependency before the token catalog is rehomed loses the local `THEMING_DESIGN_TOKENS_AI_GUIDE.md` reference for component styling. | Med | Med | TC-I-04..06 verify `npm i` still succeeds + `--crt-*` renders from platform `:root` + the scaffold `AGENTS.md` styling path resolves to a token-catalog source; the dep-removal story is **blocked** on the cross-repo catalog rehoming + devkit-guide repoint (external). |

---

## Traceability Matrix (case → ADR decision / refinement)

| Area | ADR decision / refinement | Cases |
|------|---------------------------|-------|
| Parity gate (FIRST) | R-08, D7 layer 2, R-15 | TC-U-P01..P05 |
| Color math anchors | D3, R-07, R-09, R-11, R-18 | TC-U-01..10 |
| Builder / regex fill | D4, R-10, R-14, R-17 | TC-U-15..17, TC-U-23, TC-U-24 |
| Template provider (bundled) | D5, R-03, R-12 | TC-U-25..28 |
| `build-theme` CLI | D1, D2, R-03, OQ-03 | TC-U-30..33, TC-I-01..02 |
| `build-theme` MCP tool | D1, D6, R-02, R-03, R-17 | TC-U-11..14 |
| Feature-toggle gating | D6, R-12 | TC-U-12, TC-U-13 |
| Guidance swap | D8, R-16 | TC-U-19..22, TC-E2E-02 |
| Decommission / styling migration | D9, R-01 | TC-I-04..06, TC-M-05 |
| MCP advertised live | D1, D6 (live manifest) | TC-E2E-01, TC-M-02 |
| End-to-end (no-code + workspace) | D1 | TC-M-01, TC-M-03 |

---

## Ordered Test Phases

> The math story is **gated** by a parity spike. Phases run in order; Phase 0 must pass (or its fallback be applied) before the rest of the math story proceeds.

| Phase | Gate | Content | Testable toggle-off? |
|-------|------|---------|-------------------|
| **0 — Parity spike (GATE, FIRST)** | **Blocks the math story** | Run the broad, adversarially-seeded parity fixture (TC-U-P01..P03); if ULP drift appears, apply the pre-decided fallback (fdlibm port OR documented tolerance). **Do not write the rest of the math until this passes.** | Yes (fixture is committed) |
| **1 — Color math** | Phase 0 green | All ported spec anchors (TC-U-01..10); five-palette uniform verification (TC-U-P05 / TC-U-10) | Yes |
| **2 — Builder + regex fill** | Phase 1 green | `IThemeCssBuilder.Build` orchestration, regex porting, post-fill guard (TC-U-15..17) | Yes (template via fixture) |
| **3 — Template provider (bundled)** | independent | Version pick: highest-when-empty, highest ≤ target, too-old → `ArgumentException`, missing file → `InvalidOperationException` (TC-U-25..28) | Yes (bundled tpl / fixture) |
| **4 — Surface (CLI + flat MCP tool + guidance swap + decommission)** | Phases 1–3 green | CLI output modes, flat tool mapping, `BuildThemeResult`, safety flags, toggle gating, guidance swap, scaffold dep removal (TC-U-11..14, TC-U-19..22, TC-U-30..33, TC-I-01..06) | Yes (bundled template) |
| **5 — Go-live (enable toggle)** | **Deferred until surface complete + approved (R-12)** | Flip `[FeatureToggle("theming")]` on; live runbook (TC-M-*) | Yes (no external gate) |

---

## Unit Tests — `clio.tests/`

> Conventions for every case: `[Category("Unit")]` (NEVER `UnitTests`) **AND** `[Property("Module","Theming")]` for math/builder, `Module=McpServer` for the MCP tool, `Module=Command` for the CLI command + template provider (R-06); `MethodName_ShouldExpectedBehavior_WhenCondition`; explicit AAA; a `because` on every assertion; `[Description]` on every test. Math reaches `internal static` helpers via the existing `InternalsVisibleTo`. Command fixtures derive from `BaseCommandTests<BuildThemeOptions>`, resolve the SUT from `Container`, register `IThemeCssBuilder`/`IThemeTemplateProvider` doubles in `AdditionalRegistrations`, `ClearReceivedCalls` in teardown. The flat MCP-tool fixture mirrors `ComponentInfoToolTests` (constructor-injected substitutes — NOT the `BaseTool`/`IToolCommandResolver` pattern).

### Phase 0 — Parity gate (`clio.tests/Theming/ColorMathParityTests.cs`) — RUN FIRST

#### TC-U-P01 — generated parity fixture matches C# exactly (the load-bearing gate)
- **Level**: Unit · **File**: `clio.tests/Theming/ColorMathParityTests.cs` · **Module**: Theming
- **Traces**: R-08, D7 layer 2
- **Preconditions**: committed `Fixtures/color-math-parity.json` of `hex → {generateScale, deriveSecondary, accentCandidates}` pairs + a few full `buildThemeCss` outputs, captured once from the durable TS source (R-15).
- **Steps**: deserialize each fixture entry; run the C# math; compare hex-for-hex.
- **Expected**: every C# output **equals** the TS golden output exactly. **If any entry diverges → the gate fails → apply the pre-decided fallback (TC-U-P03).** Name e.g. `GenerateScale_ShouldMatchTsGolden_ForEveryParityFixtureEntry`.

#### TC-U-P02 — fixture is broad + adversarially seeded (gate quality assertion)
- **Level**: Unit · **File**: `ColorMathParityTests.cs` · **Module**: Theming
- **Traces**: R-08
- **Steps / Expected**: assert the fixture set **includes** inputs whose channels land near `x.5·255` rounding boundaries, near-gamut-boundary chromas, and `detectMode`/contrast-threshold boundaries — **not** merely "≈200 random hex". A coverage/count guard fails if the adversarial seeds are dropped. Name e.g. `ParityFixture_ShouldContainAdversarialBoundarySeeds_WhenLoaded`.

#### TC-U-P03 — pre-decided fallback applied when ULP drift is detected (decision record, not deferred)
- **Level**: Unit · **File**: `ColorMathParityTests.cs` (+ the math under test) · **Module**: Theming
- **Traces**: R-08
- **Steps / Expected**: if the spike shows ULP divergence, the implemented fallback is exercised here: **either** fdlibm `pow`/`cbrt` is ported (and TC-U-P01 then passes exactly) **OR** an explicit documented tolerance is applied (and the tolerance constant + its justification are asserted). The plan does not allow "discover at the end" — this case records which branch was taken. Name e.g. `ColorMath_ShouldMatchTsWithinAgreedFallback_WhenLibmDiffers`.

#### TC-U-P04 — fixture provenance + frozen goldens (R-15)
- **Level**: Unit / doc-assert · **File**: `ColorMathParityTests.cs` · **Module**: Theming
- **Traces**: R-15
- **Steps / Expected**: the committed `Fixtures/theme.css.tpl` (a copy of the bundled `tpl/themes/{version}/theme.css.tpl`) is the **actual creatio-ui freedom.scss-derived template** (not a clio hand-bless) — R-15(a). The parity goldens were generated **once** from the TS source and are **frozen** — NOT regenerable after `@creatio/theming` is deleted (`freedom.scss` carries the default values, not the `generateScale` algorithm); the C# port is canonical (R-15b). No `Fixtures/README` note (removed).

#### TC-U-P05 — five-palette uniform calibration + template/builder contract (drift guard)
- **Level**: Unit · **File**: `clio.tests/Theming/ThemeCssBuilderTests.cs` · **Module**: Theming
- **Traces**: R-18, D7 layer 3
- **Preconditions**: `Fixtures/theme.css.tpl` (the real freedom.scss-derived bundled template, R-15).
- **Steps / Expected**:
  - **Uniform five-palette (R-18)**: run `generateScale(-500)` on each of the five anchors (`primary #004fd6`, `secondary #0d2e4e`, `accent #ff4013`, `success #0b8500`, `error #d2310d`) and assert C# output equals the TS golden **identically** — **same code path, full 10-shade comparison, NO secondary special-case, NO separate 600–900 handling**. (Primary's 12 shades are the published spec values; the other four goldens come from the parity fixture.)
  - **Template/builder contract**: assert the fixture **contains every token the builder rewrites** — all `--crt-palette-*`, the finalized `--crt-color-*` roles, `<%themeCssClass%>`, and the strippable header comment. A producer template change that breaks the fill fails here.
  - **Explicitly does NOT** assert `generateScale` reproduces the template's **baked secondary/accent** values — those are sample defaults + regex targets, overwritten by `applyPalettes`.

### Phase 1 — Color math (`clio.tests/Theming/*`)

#### TC-U-01 — `normalizeHex` matrix (incl. rgb/hsl/named, alpha-reject)
- **Level**: Unit · **File**: `clio.tests/Theming/ColorNormalizerTests.cs` · **Module**: Theming
- **Traces**: D3, Test strategy
- **Steps / Expected** (one `[TestCase]` per row):
  - `#FFF` → `#ffffff`; `004FD6` (no `#`) → `#004fd6`; lowercases output.
  - `rgb(...)` and `hsl(...)` forms normalize; **`hsl(217,100%,42%)` → `#0052d6`** (R-11d anchor).
  - CSS named color → its hex (named-colors lookup is **lowercase-then-ordinal-exact**, R-11d).
  - alpha forms (`#RRGGBBAA`, `rgba()`, `hsla()`) → **rejected** (throw / fail per the TS contract).
- Name e.g. `NormalizeHex_ShouldReturnLowercaseSixHex_WhenInputIsShorthand`.

#### TC-U-02 — `generateScale('#004fd6')` 12 published shades (primary, spec-confirmed)
- **Level**: Unit · **File**: `clio.tests/Theming/PaletteGeneratorTests.cs` · **Module**: Theming
- **Traces**: D3, D7, R-18 (primary is the spec-confirmed reproduction)
- **Steps / Expected**: assert all 12 shades equal the published spec values — including shade 10 `#f7f8fb` and shade 900 `#001c5a`. Name e.g. `GenerateScale_ShouldReturnPublishedShades_WhenPrimaryIsCalibrationAnchor`.

#### TC-U-03 — numeric culture + hex parsing under a non-invariant culture (R-09)
- **Level**: Unit · **File**: `clio.tests/Theming/ColorNormalizerTests.cs` · **Module**: Theming
- **Traces**: R-09
- **Preconditions**: set `CultureInfo.CurrentCulture`/`CurrentUICulture` to `de-DE` for the test scope (restore in teardown).
- **Steps / Expected**: `normalizeHex('rgb(...)'/'hsl(...)')` parse and font-weight joining produce the **same** result as under invariant culture (decimal-comma culture must not break them); hex parsing uses `byte.Parse(s, NumberStyles.HexNumber, InvariantCulture)`; document that `srgbChannels` assumes normalized 6-hex input (`Convert.ToInt32` throws on garbage where JS `parseInt` yields `NaN` — divergent on malformed fixture inputs). Name e.g. `NormalizeHex_ShouldParseInvariantly_WhenCultureIsGerman`.

#### TC-U-04 — rounding recipe + round-then-clamp order (R-07)
- **Level**: Unit · **File**: `clio.tests/Theming/ColorSpaceTests.cs` · **Module**: Theming
- **Traces**: R-07
- **Steps / Expected**:
  - JS `Math.round` semantics (round half toward +∞ with the ULP guard) — assert the **`x = 0.49999999999999994`-class** input rounds to **0** (not 1, which a naive `Math.Floor(x+0.5)` gives because `x+0.5 == 1.0`).
  - In `rgbToHex`, assert **round THEN clamp** (`color-space.ts:60-66`), not clamp-then-round — exercise a slightly-negative and a slightly->1 pre-clamp channel.
- Name e.g. `RgbToHex_ShouldRoundThenClamp_WhenChannelIsHalfBoundary`.

#### TC-U-05 — `deriveSecondary('#004fd6') == '#0d2e4e'`
- **Level**: Unit · **File**: `PaletteGeneratorTests.cs` · **Module**: Theming
- **Traces**: D3
- **Steps / Expected**: assert exact equality. Name e.g. `DeriveSecondary_ShouldReturnExpectedHex_WhenPrimaryIsAnchor`.

#### TC-U-06 — `accentCandidates('#004fd6')` all three + float-loop reproduction
- **Level**: Unit · **File**: `PaletteGeneratorTests.cs` · **Module**: Theming
- **Traces**: D3, R-11b, R-11d
- **Steps / Expected**: assert the full candidate set `[#f94e11 @135°, #d29a16 @180°, #87b716 @225°]`; the `cuspL` loop is reproduced with the exact `double` `+=` accumulation (start `0.35`, step `0.01`, bound `0.85`) — **not** index arithmetic `0.35 + i*0.01`. Name e.g. `AccentCandidates_ShouldReturnThreeHueRotations_WhenPrimaryIsAnchor`.

#### TC-U-07 — `chooseBestAccent` stable tie-break + `suggestAdaptedPrimary500` (R-11a/R-11b)
- **Level**: Unit · **File**: `clio.tests/Theming/ColorMetricsTests.cs` · **Module**: Theming
- **Traces**: R-11a, R-11b
- **Steps / Expected**:
  - `chooseBestAccent` on the anchor candidates → `#f94e11`; use `OrderByDescending` and preserve **original candidate order on ties** (compare `double`s, never cast to `int`); a deliberate equal-score tie-break anchor proves stability.
  - `suggestAdaptedPrimary500('#cccccc') == '#949494'` and `suggestAdaptedPrimary500('#000000') == null`; the loop is reproduced with exact `double` accumulation (start/step/bound/comparison), **not** refactored to index arithmetic.
- Name e.g. `ChooseBestAccent_ShouldPreserveOriginalOrder_WhenScoresTie`.

#### TC-U-08 — luminance / contrast / distance anchors
- **Level**: Unit · **File**: `ColorMetricsTests.cs` · **Module**: Theming
- **Traces**: D3, Test strategy
- **Steps / Expected**: `relativeLuminance`, `contrastRatio`, `distanceOklab` match their TS anchor values exactly; the `≥`-threshold comparisons (3.0 on white, 4.5 text) are pinned at the boundary.

#### TC-U-09 — gamut epsilon + `detectMode`/threshold boundary anchors (R-11c/R-11d)
- **Level**: Unit · **File**: `ColorSpaceTests.cs` · **Module**: Theming
- **Traces**: R-11c, R-11d
- **Steps / Expected**:
  - Pin the gamut test literals/operators **exactly**: `channel >= -0.001 && channel <= 1.001` (`color-space.ts:74`) — the most precision-sensitive comparison in the library.
  - `detectMode`/threshold **boundary** anchors: `l = 0.38` / `0.78`, `h = 80` / `105`, `c = 0.06`.
- Name e.g. `IsInGamut_ShouldUseExactEpsilon_WhenChannelAtBoundary`.

#### TC-U-10 — text tokens + Google Fonts URLs
- **Level**: Unit · **File**: `clio.tests/Theming/TextTokenResolverTests.cs`, `clio.tests/Theming/FontImportBuilderTests.cs` · **Module**: Theming
- **Traces**: D3, Test strategy
- **Steps / Expected**: `resolveTextToken`, `resolveLinkHover`, `resolveTextOnColorToken` resolve to the expected passing palette step; `googleFontsImportUrl`/`Rule` produce the expected URLs (weights **sorted + deduped**; family validation); the Montserrat **default** is compared **case-sensitively (ordinal)** so a default-Montserrat input yields **no** `@import` (R-11d).

### Phase 2 — Builder + regex fill (`clio.tests/Theming/ThemeCssBuilderTests.cs`)

#### TC-U-15 — `buildThemeCss` contains / not-contains + the three throw cases
- **Level**: Unit · **File**: `ThemeCssBuilderTests.cs` · **Module**: Theming
- **Traces**: D4, Test strategy
- **Preconditions**: `IThemeCssBuilder.Build(input, templateCss)` with the fixture template (no disk read inside the math — D2).
- **Steps / Expected**: output **contains** the substituted `--crt-palette-*` colors, the resolved `--crt-color-*` roles, and the supplied `<%themeCssClass%>` value; **does not contain** the stripped header comment or any literal `<%…%>`; the three documented throw cases (per the TS spec) raise the expected errors. Name e.g. `Build_ShouldContainSubstitutedPalette_WhenInputIsValid`.

#### TC-U-16 — regex porting hazards pinned (R-10)
- **Level**: Unit · **File**: `ThemeCssBuilderTests.cs` · **Module**: Theming
- **Traces**: R-10 (a)–(e)
- **Steps / Expected**:
  - (a) `applyPalettes` / `setColorDeclaration` use the **count-1** `Regex.Replace` overload — a template with a duplicate `--crt-palette-…` line replaces **only the first** (matching JS no-`g`).
  - (b) the font-family replacement uses a **`MatchEvaluator`** (not raw interpolation) — a family containing `$1`/`$&` is inserted literally, not treated as a substitution token.
  - (c) `THEME_CSS_CLASS_PATTERN` uses **`\z`, not `$`** — `"Foo\n"` is **rejected** (a .NET `$` would wrongly pass).
  - (d) comment strip keeps `[\s\S]` verbatim, **no anchor**, matches `\n` only (never `\r?\n`); **no** `RegexOptions.Multiline` / `RegexOptions.ECMAScript`.
  - (e) regex steps carry a match timeout (S6444); the literal `<%themeCssClass%>` swap is a plain `string.Replace` (no regex, no timeout).
- Name e.g. `ApplyPalettes_ShouldReplaceFirstOccurrenceOnly_WhenTemplateHasDuplicate`.

#### TC-U-17 — post-fill runtime guard (D4)
- **Level**: Unit · **File**: `ThemeCssBuilderTests.cs` · **Module**: Theming
- **Traces**: D4
- **Steps / Expected**: a deliberately drifted template (leftover `<%foo%>` / a missing `--crt-palette-…` target) makes `Build` **fail with a clear error** rather than emit a broken theme; a valid template asserts no `<%…%>` remains and every palette step was substituted.

### Phase 3 — Template provider (`clio.tests/Command/ThemeTemplateProviderTests.cs`)

#### TC-U-25 — highest bundled returned when target version empty
- **Level**: Unit · **File**: `clio.tests/Command/ThemeTemplateProviderTests.cs` · **Module**: Command
- **Traces**: D5, R-03
- **Steps / Expected**: with an empty/null target version, `ResolveCompatibleVersion` returns the **highest** bundled `tpl/themes/<version>/` folder; the provider reads its `theme.css.tpl`. Name e.g. `GetCssTemplate_ShouldReturnHighestBundled_WhenVersionEmpty`.

#### TC-U-26 — highest bundled ≤ target picked
- **Level**: Unit · **File**: `ThemeTemplateProviderTests.cs` · **Module**: Command
- **Traces**: D5, R-03
- **Steps / Expected**: given several bundled versions, `ResolveCompatibleVersion` picks the **highest bundled version ≤ target** (`LastOrDefault(v <= target)`) — a target between two bundled versions resolves to the lower one; an exact match resolves to itself. Name e.g. `GetCssTemplate_ShouldPickHighestBundledNotNewerThanTarget_WhenVersionProvided`.

#### TC-U-27 — too-old target → `ArgumentException`
- **Level**: Unit · **File**: `ThemeTemplateProviderTests.cs` · **Module**: Command
- **Traces**: D5, R-12
- **Steps / Expected**: a target **below the lowest bundled** version throws `ArgumentException("Themes require Creatio {min} or newer; version {target} is not supported.")`. Name e.g. `GetCssTemplate_ShouldThrowArgumentException_WhenVersionBelowLowestBundled`.

#### TC-U-28 — missing template file → `InvalidOperationException`
- **Level**: Unit · **File**: `ThemeTemplateProviderTests.cs` · **Module**: Command
- **Traces**: D5
- **Steps / Expected**: when the resolved version folder is missing its `theme.css.tpl` (or `theme.json.tpl`), the provider throws `InvalidOperationException`. Name e.g. `GetCssTemplate_ShouldThrowInvalidOperation_WhenTemplateFileMissing`.

### Phase 4 — MCP tool (`clio.tests/Command/McpServer/BuildThemeToolTests.cs`)

#### TC-U-11 — flat `ComponentInfoTool`-style tool shape (R-02)
- **Level**: Unit · **File**: `clio.tests/Command/McpServer/BuildThemeToolTests.cs` · **Module**: McpServer
- **Traces**: R-02
- **Steps / Expected**: `BuildThemeTool` is a `[McpServerToolType]` that **does NOT derive from `BaseTool`** and **constructor-injects** `IThemeCssBuilder` + `IThemeTemplateProvider` (assert via reflection on base type + ctor params); it returns `BuildThemeResult`, with no command/resolver/`BaseTool` machinery and no `BaseTool.cs` edit. Name e.g. `BuildThemeTool_ShouldNotDeriveFromBaseTool_WhenInspected`.

#### TC-U-12 — arg mapping → `IThemeCssBuilder.Build`; safety flags
- **Level**: Unit · **File**: `BuildThemeToolTests.cs` · **Module**: McpServer
- **Traces**: D1, R-03, R-17
- **Steps / Expected**:
  - `primary`, `secondary`, `accent`, `success`, `error`, `css-class-name`, `heading-font`, `body-font`, `font-weights`, optional `version`/`environment-name` map onto the build input; the tool resolves the bundled template via `IThemeTemplateProvider` then calls `Build` and returns `BuildThemeResult { success, css, descriptor, warnings?, error? }`.
  - `[McpServerTool]` flags via reflection: **`ReadOnly=true, Destructive=false, Idempotent=true, OpenWorld=false`**; `[Description]` routes to `get-guidance theming`.
  - Document (R-17) that `ReadOnly`/`Idempotent` describe **environment** effects; reading the bundled template and the optional `--environment-name` version probe are read-only and within scope (mirror `get-component-info` wording).
- Name e.g. `BuildThemeTool_ShouldMapArgsAndReturnCss_WhenInputValid`.

#### TC-U-13 — feature-toggle gating on the tool type (D6/R-12)
- **Level**: Unit · **File**: `BuildThemeToolTests.cs` · **Module**: McpServer
- **Traces**: D6, R-12
- **Steps / Expected**: `BuildThemeTool` carries `[FeatureToggle("theming")]`; with the toggle **off**, `McpFeatureToggleFilter.RegisterEnabledPrimitives` (`IEnumerable<Type>`) does **not** register it; with it **on**, it does. Assert the predicate is `IFeatureToggleService.IsEnabled(typeof(BuildThemeTool))` (case-insensitive key); never a `Type[]`/`*FromAssembly` path. Name e.g. `RegisterEnabledPrimitives_ShouldOmitBuildThemeTool_WhenToggleOff`.

#### TC-U-14 — too-old / invalid `version` → graceful `success:false` (no throw to the protocol)
- **Level**: Unit · **File**: `BuildThemeToolTests.cs` · **Module**: McpServer
- **Traces**: D5, R-02
- **Steps / Expected**: a faked `IThemeTemplateProvider` throwing `ArgumentException` (target below the lowest bundled) makes the tool return `BuildThemeResult { Success=false, Error=<the "Themes require Creatio {min} or newer…" message> }` — no unhandled exception on the JSON-RPC channel. Name e.g. `BuildTheme_ShouldReturnFailure_WhenVersionTooOld`.

### Phase 4 — Guidance swap (`clio.tests/Command/McpServer/GuidanceGetToolTests.cs` — extend)

> **R-16: enumerate each changed assertion.** The committed `theming` test (`GuidanceGetToolTests.cs:124-165`) currently asserts the npm/Delegate contract; D8's swap changes it. The not-in-CI `GuidanceGetToolE2ETests.cs` mirrors these.

#### TC-U-19 — `get-guidance theming` still resolves (resolution guard)
- **Level**: Unit · **File**: `clio.tests/Command/McpServer/GuidanceGetToolTests.cs` (extend) · **Module**: McpServer
- **Traces**: D8, RR (resolution)
- **Steps / Expected**: `get-guidance theming` returns `Success=true` and article URI `docs://mcp/guides/theming` (unbroken). The kept sections survive: "Which flow", deploy, "List themes", "Get/set the default theme", and the `CanCustomizeBranding`/`CanManageThemes` preconditions.

#### TC-U-20 — npm / palette-engine assertions removed; `build-theme` present (R-16)
- **Level**: Unit · **File**: `GuidanceGetToolTests.cs` (modify in place) · **Module**: McpServer
- **Traces**: D8, R-16
- **Steps / Expected** (each is an enumerated **test-update** item — the current line is removed/changed and replaced):
  - **Remove** `Contain("@creatio/theming")` (currently `:128`) → assert `NotContain("@creatio/theming")`.
  - **Remove** `Contain("palette engine")` (currently `:144`) → the deterministic-engine routing now reads `build-theme`; assert `Contain("build-theme")`.
  - Keep `Contain("Which flow")`, `Contain("No-code / server flow")`, `NotContain("not yet available")`, and the `create/update/delete-theme-by-environment` assertions (sibling server-flow contract is unchanged).
  - Replace the workspace step-2 / no-code step-1 npm-fetch text expectations with "call `build-theme`".
- Name e.g. `GetGuidance_ShouldReferenceBuildTheme_WhenTopicIsTheming`.

#### TC-U-21 — token catalog still NOT restated; `--crt` count guard updated (R-16/CM-03)
- **Level**: Unit · **File**: `GuidanceGetToolTests.cs` (modify in place) · **Module**: McpServer
- **Traces**: D8, R-16, CM-03
- **Steps / Expected**: the existing `--crt` namespace-mention count guard (currently `:163-165`, "at most once") is **re-evaluated** against the swapped text — the guide must still **not restate** the `--crt-*` token catalog (single source of truth); update the expected count if the `AI_GUIDES_INDEX.md` pointer changes, but the catalog stays out. Name e.g. `GetGuidance_ShouldNotRestateTokenCatalog_WhenTopicIsTheming`.

#### TC-U-22 — `GuidanceCatalog` blurb + `McpCapabilityMap` updated together (R-16 drift)
- **Level**: Unit · **File**: `GuidanceGetToolTests.cs` / a docs-drift test (if present) · **Module**: McpServer
- **Traces**: D8, R-16
- **Steps / Expected**: the `GuidanceCatalog` `theming` blurb drops `@creatio/theming`; `docs/McpCapabilityMap.md` gains the `build-theme` row + the updated theming-resource description. If a drift test exists, it ties the blurb to the map string; if not, this reduces to PR doc review (accept the gap explicitly).

### Phase 4 — `build-theme` CLI command (`clio.tests/Command/BuildThemeCommandTests.cs`)

> Fixture: `BaseCommandTests<BuildThemeOptions>`; register substitute `IThemeCssBuilder` + `IThemeTemplateProvider` (and `ISettingsRepository`/`IPlatformVersionResolverFactory` for the version-resolution case) in `AdditionalRegistrations`; resolve the SUT from `Container`. Verify how a local (no-environment) command is dispatched in `Program.cs` rather than assuming the `EnvironmentOptions` path (R-02 caveat).

#### TC-U-30 — stdout output mode (no `--output`)
- **Level**: Unit · **File**: `clio.tests/Command/BuildThemeCommandTests.cs` · **Module**: Command
- **Traces**: D1, OQ-03
- **Steps / Expected**: with `--primary` + `--css-class-name` and `--output` omitted, the command resolves the bundled template via `IThemeTemplateProvider`, calls `IThemeCssBuilder.Build`, and writes the `theme.css` string to stdout; exit 0; **no environment mutated** (D1) — an environment is touched only to *resolve the version* when `--environment-name` is given (R-03). Name e.g. `Execute_ShouldWriteCssToStdout_WhenOutputOmitted`.

#### TC-U-31 — workspace-dir output mode writes `theme.css` + `theme.json` (OQ-03)
- **Level**: Unit (logic) / Integration (real files — see TC-I-01) · **File**: `BuildThemeCommandTests.cs` · **Module**: Command
- **Traces**: D1, D5, OQ-03
- **Steps / Expected**: with `--output <dir>` the command writes **both** `theme.css` (built) and `theme.json` (filled from the bundled `theme.json.tpl` — `<%themeId%>`/`<%themeCaption%>`/`<%themeCssClass%>`); the MCP no-code flow never gets `theme.json` (asymmetry per D5/OQ-03). Name e.g. `Execute_ShouldWriteThemeCssAndThemeJson_WhenOutputIsWorkspaceDir`.

#### TC-U-32 — version resolution (`--version` XOR `--environment-name`) (R-03)
- **Level**: Unit · **File**: `BuildThemeCommandTests.cs` · **Module**: Command
- **Traces**: R-03
- **Steps / Expected**:
  - explicit `--version` is passed to `IThemeTemplateProvider` as the target (no environment touched).
  - `--environment-name` resolves the version via `ISettingsRepository.FindEnvironment` → `IPlatformVersionResolverFactory.Create(settings).ResolveAsync().ResolvedVersion`; an **unregistered** environment → a clear "environment '<x>' is not registered" error; an unresolvable/`latest` result → highest bundled.
  - **both** flags → a mutually-exclusive error; **neither** → highest bundled.
- Name e.g. `Execute_ShouldResolveVersionFromEnvironment_WhenEnvironmentNameProvided`.

#### TC-U-33 — feature-toggle gating on `BuildThemeOptions`
- **Level**: Unit · **File**: `BuildThemeCommandTests.cs` · **Module**: Command
- **Traces**: D6, R-12
- **Steps / Expected**: `BuildThemeOptions` carries `[FeatureToggle("theming")]`; with the toggle off the verb is filtered out at parse + dispatch (mirror the project-context four-surface contract). Assert the attribute presence + that the dispatch chokepoint blocks it when disabled. Name e.g. `BuildThemeOptions_ShouldCarryFeatureToggle_WhenInspected`.

---

## Integration Tests — `clio.tests/` (`[Category("Integration")]`)

> Real temp-file / scaffold I/O. PR-merge tier. Create temp dirs under the OS temp dir, write UTF-8, delete in teardown; OS-portable per the test-style policy. The `npm i` scaffold checks may need a Node toolchain on the runner — if unavailable, demote to the manual runbook (TC-M-05) and note the gap.

#### TC-I-01 — workspace-dir output writes real `theme.css` + `theme.json` (R-17 / OQ-03)
- **Level**: Integration · **File**: `clio.tests/Command/BuildThemeCommandTests.cs` (Integration cases) · **Module**: Command
- **Traces**: D1, OQ-03, R-17
- **Steps / Expected**: `Execute --output <temp dir>` (template from the bundled `tpl/themes/{version}/`) writes a real `theme.css` (matching the built CSS byte-for-byte) and a real `theme.json` with the substituted id/caption/cssClassName; teardown deletes the dir.

#### TC-I-02 — `build-theme` end-to-end through the CLI off the bundled template (no network)
- **Level**: Integration · **File**: `BuildThemeCommandTests.cs` (Integration) · **Module**: Command
- **Traces**: R-12, D5
- **Steps / Expected**: with neither version flag set, `build-theme` resolves the highest bundled `tpl/themes/{version}/theme.css.tpl` and produces a valid `theme.css` with **no** network call — proving the dev/test path works end-to-end while the toggle is off.

#### TC-I-04 — `new-ui-project` scaffolds + `npm i` succeeds WITHOUT `@creatio/theming` (R-01)
- **Level**: Integration · **File**: `clio.tests/Command/NewUiProjectScaffoldTests.cs` (or the existing scaffold test) · **Module**: Command
- **Traces**: D9, R-01
- **Steps / Expected**: scaffold a `ui-project`; assert `clio/tpl/ui-project/package.json` has **no** `@creatio/theming` devDependency; `npm i` (if Node available on the runner) succeeds; the Angular build does not break (the `--crt-*` vars are platform `:root` primitives, not from the package). Name e.g. `NewUiProject_ShouldInstallWithoutThemingPackage_WhenScaffolded`.

#### TC-I-05 — `--crt-*`-referencing component renders (tokens resolve from platform `:root`) (R-01)
- **Level**: Integration · **File**: `NewUiProjectScaffoldTests.cs` · **Module**: Command
- **Traces**: R-01
- **Steps / Expected**: a sample component referencing `--crt-*` tokens compiles/renders in the scaffolded project without the npm package (tokens are runtime platform primitives). If full Angular render is not runnable in CI, assert the scaffold's `styles.scss` is empty and no `.ts` imports `@creatio/theming` (only `@creatio-devkit/common`), and defer the live render to TC-M-05.

#### TC-I-06 — scaffold `AGENTS.md` styling path resolves to a token-catalog source (R-01)
- **Level**: Integration · **File**: `NewUiProjectScaffoldTests.cs` · **Module**: Command
- **Traces**: R-01
- **Steps / Expected**: the scaffolded `clio/tpl/ui-project/AGENTS.md` contains an explicit `--crt-*` token-catalog pointer to the catalog's rehomed CDN/Academy home (not `node_modules/@creatio/theming/THEMING_DESIGN_TOKENS_AI_GUIDE.md`). **Caveat (external):** the transitive `@creatio-devkit/common` REMOTE_COMPONENT_STYLING repoint is cross-repo and cannot be asserted from clio — flag it (TC-M-05).

---

## E2E (MCP) Tests — `clio.mcp.e2e/` (`[Category("E2E")]`)

> **CI status: NOT in CI — manual execution required.** MCP E2E is mandatory per the AGENTS.md MCP maintenance policy (unit mapping tests are insufficient alone), but the suite does not run in CI yet. The harness **enables the `build-theme` toggle** and sources the template from the bundled `tpl/themes/{version}/` asset (the feature is dark in production until go-live). TC-E2E-01 is the **only** check that catches a silent-no-op (`Type[]`/`*FromAssembly`) MCP registration.

#### TC-E2E-01 — `build-theme` advertised + produces a valid `theme.css`
- **Tool**: `build-theme`
- **File**: `clio.mcp.e2e/BuildThemeToolE2ETests.cs`
- **Traces**: D1, D6, R-02
- **Steps**: start the real `clio mcp-server` with the toggle enabled (template from the bundled `tpl/themes/{version}/`); list tools; assert `build-theme` is advertised with **`ReadOnly=true, Destructive=false, Idempotent=true, OpenWorld=false`** and a description referencing `get-guidance theming`; invoke it with a valid `primary` + `css-class-name`.
- **Expected**: tool present with the flags; returns `BuildThemeResult { success:true, css:<valid theme.css> }`. With the toggle **off**, the tool is **absent** from the manifest.
- **Manual gate**: add to the PR checklist.

#### TC-E2E-02 — `get-guidance theming` discovery against the real server (post-swap)
- **Tool**: `get-guidance` (`theming`)
- **File**: `clio.mcp.e2e/GuidanceGetToolE2ETests.cs` (extend the existing theming discovery test)
- **Traces**: D8, R-16
- **Expected**: the real server returns the updated guide — references `build-theme`, no longer references `@creatio/theming` / `palette engine`; token catalog still not restated. **Manual.** (R-16 flags this E2E-not-in-CI gap explicitly.)

---

## Manual / Live-Stand Tests + Runbook

> These depend on a real Creatio environment (`CanCustomizeBranding` + `CanManageThemes`). Listed so dev/QA do not mistake them for unit-coverable items. Record outcomes in the PR.

#### TC-M-01 — no-code / server end-to-end (build → create → list → set default)
- **Level**: Manual live stand · **Traces**: D1 (pure pipe composes with server flow)
- **Steps**: `build-theme --primary … --css-class-name …` (capture CSS) → pipe the CSS into `create-theme-by-environment --css-content …` → `list-themes-by-environment` (theme appears) → set it as `DefaultTheme`. Each step exits 0; the theme renders in the environment.

#### TC-M-02 — `build-theme` advertised by the real `clio mcp-server` (live manifest)
- **Level**: Manual live stand / E2E · **Traces**: D1, D6
- **Caveat**: unit tests assert the safety-flag **attribute values** on the tool class (TC-U-12), **not** the live manifest. The live-manifest assertion is satisfied **only** by TC-E2E-01 (manual, not in CI) — the catch for a silent-no-op registration.

#### TC-M-03 — workspace / dev end-to-end (build → push-workspace → list)
- **Level**: Manual live stand · **Traces**: D1, OQ-03
- **Steps**: `build-theme --output <workspace package dir>` (writes `theme.css` + `theme.json`) → `push-workspace` to the environment → `list-themes` shows the theme. Confirms the workspace-output mode + the `theme.json` contract land correctly in a real package.

#### TC-M-04 — go-live: verify parity + flip the toggle (R-12)
- **Level**: Manual live stand · **Traces**: R-12 go-live
- **Caveat**: gated on the surface (tests/docs/MCP tool) being complete and go-live approved — **not** producer-gated. Steps: confirm the parity gate (TC-U-P01..P03) and the surface tests are green; flip `clio experimental --name theming --enable`; run `build-theme` so it reads the bundled template; verify the produced `theme.css` against a known-good baseline before shipping the toggle on for release.

#### TC-M-05 — cross-repo styling-migration verification (R-01, external dependency)
- **Level**: Manual / cross-repo · **Traces**: R-01
- **Caveat**: **NOT unit-verifiable from clio.** Confirm the `@creatio-devkit/common` REMOTE_COMPONENT_STYLING guide has been repointed at the rehomed `--crt-*` catalog (CDN/Academy), and the token catalog has a surviving online home. Until both land, removing the scaffold devDependency leaves an author-time documentation gap — the dep-removal story is **blocked** on this (tracked in `sprint-status.yaml`).

---

## Not-in-CI / External-Dependency Callouts

> Consolidated so nothing here is mistaken for a CI-green guarantee.

| Item | Status | Why / gate |
|------|--------|-----------|
| **MCP E2E** (`clio.mcp.e2e/BuildThemeToolE2ETests.cs`, `GuidanceGetToolE2ETests.cs`) | **NOT in CI — manual** | Mandatory per AGENTS.md MCP policy, but the suite does not run in CI yet. Only check that catches a silent-no-op MCP registration (TC-E2E-01). PR checklist gate below. |
| **Go-live — toggle flip** (TC-M-04) | **Deferred until surface complete + approved (R-12)** | Not producer-gated; the template ships bundled in clio. Tracked as a pending story in `sprint-status.yaml`. Feature is **dark** until go-live. |
| **`get-theme-tokens` / `--crt-*` token catalog** | **Out of scope (D10)** | Future tool, a separate artifact from the bundled template. |
| **Cross-repo devkit-guide repoint** (`@creatio-devkit/common` REMOTE_COMPONENT_STYLING) + **token-catalog rehoming** (TC-M-05) | **External (R-01)** | clio cannot edit the devkit guide; the dep-removal story is blocked on this + the catalog rehoming. |
| **npm `@creatio/theming` deprecation/unpublish** (D9) | **External (creatio-ui)** | Out of clio's control; flagged, not tested here. |
| **Parity-fixture provenance** (TC-U-P04, R-15) | **Frozen — not regenerable** | Goldens were generated once from the TS source; after `@creatio/theming` is deleted they are frozen pins (C# canonical). The bundled template is freedom.scss-derived (R-15a); being an in-repo asset, no cross-repo live-snapshot guard is needed. |
| **`npm i` scaffold checks** (TC-I-04/05) | **Conditional (runner toolchain)** | Need Node on the runner; if unavailable, demote to TC-M-05 and note the gap. |

---

## Regression Guard

Tests that MUST stay green after this feature ships (re-run by the full `Category=Unit` suite — rule 4):

| Test file | Test(s) | Why at risk |
|-----------|---------|-------------|
| `clio.tests/Command/McpServer/GuidanceGetToolTests.cs` | the `theming` discovery test (`:124-165`) | shared `ThemingGuidanceResource` / `GuidanceCatalog` edited (D8/R-16) — the npm/palette-engine assertions are intentionally rewritten (TC-U-19..21); resolution must stay green. |
| `clio.tests/Command/McpServer/ComponentInfoToolTests.cs` | all | the flat tool + version-resolution prior art `BuildThemeTool` mirrors (R-02/R-03); must not regress. |
| Sibling theme tests (`ListThemesCommand.Tests.cs`, `CreateThemeCommand.Tests.cs`, `{Create,Update,Delete}ThemeToolTests.cs`) | all | `build-theme` composes with theme CRUD as a pure pipe (D1) — the CRUD path is untouched and must stay green. |
| `clio.tests/Command/NewUiProjectScaffoldTests.cs` (or equivalent) | scaffold/`package.json` tests | the `@creatio/theming` devDependency removal (D9/R-01) must not break scaffolding. |
| Full `Category=Unit` suite | all | `BindingsModule.cs` + `Program.cs` touched → DI composition + dispatch could regress any module (rule 4). |

---

## Coverage Estimate

| Layer | New/changed tests | Files | Notes |
|-------|-------------------|-------|-------|
| Unit (parity gate) | 5 cases (TC-U-P01..P05) | new `ColorMathParityTests`; `ThemeCssBuilderTests` (P05) (+ `Fixtures/color-math-parity.json`, `Fixtures/theme.css.tpl`) | **Phase 0 GATE — run FIRST**. `[Category("Unit")]`+`[Property("Module","Theming")]`. |
| Unit (color math) | 10 cases (TC-U-01..10, several multi-`TestCase`) | new `ColorNormalizerTests`, `PaletteGeneratorTests`, `ColorMetricsTests`, `ColorSpaceTests`, `TextTokenResolverTests`, `FontImportBuilderTests` | bit-exact anchors; five-palette uniform (R-18). |
| Unit (builder) | 3 cases (TC-U-15..17) | new `ThemeCssBuilderTests` | regex porting (R-10), post-fill guard. |
| Unit (template provider) | 4 cases (TC-U-25..28) | new `ThemeTemplateProviderTests` | bundled version pick: highest-when-empty, highest ≤ target, too-old → error, missing-file → error. Module=Command. |
| Unit (MCP tool) | 4 cases (TC-U-11..14) | new `BuildThemeToolTests` | flat shape (R-02), arg mapping, flags, toggle gating. Module=McpServer. |
| Unit (guidance swap) | 4 cases (TC-U-19..22) | `GuidanceGetToolTests.cs` (modify in place) | R-16 enumerated assertion rewrites. Module=McpServer. |
| Unit (CLI command) | 4 cases (TC-U-30..33) | new `BuildThemeCommandTests` | output modes, version resolution, toggle. Module=Command. |
| Integration | 5 cases (TC-I-01,02,04..06) | Integration cases in `BuildThemeCommandTests`, `NewUiProjectScaffoldTests` | real files/scaffold; `npm i` conditional on runner Node. |
| E2E (MCP) | 2 cases (TC-E2E-01..02) | new `BuildThemeToolE2ETests`; extend `GuidanceGetToolE2ETests` | **NOT in CI — manual only.** |
| Manual (live stand / cross-repo) | 5 items (TC-M-01..05) | runbook + PR notes | no-code + workspace + go-live + styling migration; TC-M-05 externally gated. |

---

## Definition of Done for QA

- [ ] **Phase 0 parity gate (TC-U-P01..P03) passes FIRST**, before the rest of the math story; the fallback branch taken (fdlibm port OR documented tolerance) is recorded (TC-U-P03).
- [ ] All TC-U-* implemented with `[Category("Unit")]` (NOT `[Category("UnitTests")]`) **AND** `[Property("Module","Theming")]` (math/builder) / `Module=McpServer` (tool) / `Module=Command` (CLI + template provider) — no uncategorized tests (R-06).
- [ ] Five-palette uniform verification (TC-U-P05) asserts `generateScale(-500)` for all five anchors identically — no secondary special-case, no separate 600–900 handling (R-18).
- [ ] All TC-I-* implemented with `[Category("Integration")]` (real temp files/scaffold; OS-portable); `npm i` checks demoted to TC-M-05 if Node is unavailable on the runner.
- [ ] Command fixtures use `BaseCommandTests<BuildThemeOptions>`, resolve the SUT from `Container`, register `IThemeCssBuilder`/`IThemeTemplateProvider` doubles in `AdditionalRegistrations`, `ClearReceivedCalls` in teardown; the MCP-tool fixture mirrors `ComponentInfoToolTests` (constructor-injected — NOT `BaseTool`/`IToolCommandResolver`).
- [ ] `[FeatureToggle("theming")]` asserted on **both** `BuildThemeOptions` (TC-U-33) and the `[McpServerToolType] BuildThemeTool` (TC-U-13); MCP registration via `RegisterEnabledPrimitives` (`IEnumerable<Type>`) — never a `Type[]`/`*FromAssembly`.
- [ ] `build-theme` flags asserted `ReadOnly=true/Destructive=false/Idempotent=true/OpenWorld=false`; description → `get-guidance theming` (TC-U-12); R-17 read-only-effects note documented (bundled-template read + optional version probe are read-only).
- [ ] R-16 guidance-swap assertions enumerated and rewritten in place (TC-U-19..22): `build-theme` present; `@creatio/theming` / `palette engine` removed; token catalog still not restated; `GuidanceCatalog` + `McpCapabilityMap` updated together.
- [ ] R-01 decommission verified (TC-I-04..06): scaffold + `npm i` without `@creatio/theming`; `--crt-*` renders from platform `:root`; scaffold `AGENTS.md` points at the rehomed catalog. Cross-repo devkit repoint + catalog rehoming flagged as external (TC-M-05).
- [ ] Regression guard tests (table above) green; **full `Category=Unit` suite** run (BindingsModule/Program touched — rule 4).
- [ ] MCP E2E (TC-E2E-01..02) documented and run **manually**; the manual gate is in the PR checklist (it is NOT in CI).
- [ ] Go-live (TC-M-04) recorded in the PR + `sprint-status.yaml` as **deferred until the surface is complete + approved** (not producer-gated) — feature stays dark until the toggle is flipped.
- [ ] Test naming follows `MethodName_ShouldExpectedBehavior_WhenCondition`; AAA + `because` per assertion + `[Description]` per test.
- [ ] PR includes the new/modified test files in the changed-files list and states the filter command used (e.g. `Validated: dotnet test --filter "Category=Unit"` + targeted `Module=Theming|McpServer|Command`).

---

## PR Checklist Gate (MCP E2E manual)

Because `clio.mcp.e2e` is NOT in CI, the PR must confirm manually:

- [ ] Ran `clio.mcp.e2e/BuildThemeToolE2ETests` against a real `clio mcp-server` (toggle enabled, template from the bundled `tpl/themes/{version}/`) — `build-theme` advertised with `ReadOnly=true/Destructive=false/Idempotent=true/OpenWorld=false`, description → `get-guidance theming`, and a valid `theme.css` produced (TC-E2E-01). Confirmed it is **absent** when the toggle is off.
- [ ] Ran the post-swap `get-guidance theming` discovery (TC-E2E-02): `build-theme` present, npm/palette-engine gone.
- [ ] Ran the live-stand no-code (TC-M-01) and workspace (TC-M-03) end-to-end round-trips.
- [ ] Recorded go-live (TC-M-04) as **deferred until the surface is complete + approved** and the cross-repo styling migration (TC-M-05) as **external** — neither blocks the toggle-off ship.
- [ ] "MCP reviewed" outcome stated per AGENTS.md MCP maintenance policy.
