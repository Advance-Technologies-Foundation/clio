# Story 1: Parity spike (GATE) — `ColorSpace` + `generateScale` port + seeded parity-fixture harness

**Feature**: theming-native-build (ENG-90636, epic ENG-26797 — Theming with AI, clio dev flow; native-build + bundled-template continuation)
**FR coverage**: D3, D7 (layer 2), R-07, R-08, R-09 — the bit-exact-parity de-risking gate
**PRD**: design substance in the approved migration plan ([adr-theming-native-build.md](../adr/adr-theming-native-build.md) §Context)
**ADR**: [adr-theming-native-build.md](../adr/adr-theming-native-build.md) — **Accepted**; refinements R-01…R-18 are authoritative and supersede the pre-review body
**Test plan**: [tp-theming-native-build.md](../test-plans/tp-theming-native-build.md)
**Status**: ready-for-dev
**Size**: L (full day)

> **DE-RISKING GATE — runs FIRST, blocks every other math/build story (Stories 2, 4) and the
> drift guard.** This is the **Phase 0 parity spike** from the test plan. It ports only the bottom of
> the OKLab pipeline (`ColorSpace` + `generateScale` + `deriveSecondary`/`accentCandidates`) and stands
> up the **seeded, adversarially-seeded parity-fixture harness**, then proves C# == TS **bit-exact at the
> hex level**. The single highest parity risk (R-08) is that `Math.Pow`/`Math.Cbrt` are **NOT
> bit-guaranteed across V8↔.NET**; this story either proves they match on the adversarial fixture **or**
> records the **pre-decided fallback** (port fdlibm `pow`/`cbrt` OR a documented tolerance). Do **not**
> write the rest of the math (Story 2) until this gate is green or its fallback is applied.

---

## As a

developer porting the deterministic OKLCH color math from the retired `@creatio/theming` TS package to C#

## I want

`ColorSpace` (matrices verbatim, JS-`Math.round` rounding, invariant-culture hex parse) + `generateScale`
ported, plus a committed, broad, adversarially-seeded parity fixture asserted hex-for-hex against the TS golden

## So that

the cross-runtime float-parity hazard (R-08) is proven or its fallback chosen **before** any further math is
written — the whole port rests on a bit-exact OKLab pipeline, and discovering ULP drift at the end is the
single failure this gate exists to prevent

---

## Acceptance Criteria

- [ ] **AC-01** — Given the OKLCH pipeline (`HEX → sRGB → linear RGB → OKLab → OKLCH`), when `ColorSpace`
  is ported, then the conversion matrices and constants are copied **verbatim** from `color-space.ts`,
  `double` is used throughout (JS `Number` is IEEE-754 double), and `lv**3`/`mv**3`/`sv**3` use
  `Math.Pow(x, 3)` (not `x*x*x`), `Math.Cbrt`, `Math.Pow`, `Math.Atan2`, with the
  `((h % 360) + 360) % 360` hue guard kept verbatim. (D3.)
- [ ] **AC-02** — Given `rgbToHex`, when a channel sits on a `.5`/near-`.5` boundary, then rounding uses
  **JS `Math.round` semantics** (round half toward +∞ with the ULP guard) — **NOT** `Math.Floor(x + 0.5)`:
  for `x = 0.49999999999999994` the result is **0** (a naive `Math.Floor(x+0.5)` gives 1 because
  `x+0.5 == 1.0`), and the order is **round THEN clamp** (`color-space.ts:60-66`), not clamp-then-round.
  (R-07; TC-U-04.)
- [ ] **AC-03** — Given hex/number parsing in `Clio.Theming`, when a channel is parsed, then it uses
  `byte.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture)` (not `Convert.ToInt32(s, 16)`),
  every numeric parse/format uses `CultureInfo.InvariantCulture` (no implicit `ToString()`/`Parse`), and
  `srgbChannels` documents that it assumes a normalized 6-hex input (TS `parseInt` yields `NaN` on garbage;
  `byte.Parse` throws — divergent on malformed input). (R-09; TC-U-03.)
- [ ] **AC-04** — Given the gamut test `channel >= -0.001 && channel <= 1.001` (`color-space.ts:74` — the
  most precision-sensitive comparison in the library) and the `maxChromaInGamut` binary search (24 iters in
  `[0, 0.4]`) and the `cuspL` loop, when ported, then the epsilon literals, operators, iteration counts,
  start/step/bound, and the exact `double` accumulation are reproduced verbatim — **not** refactored to index
  arithmetic. (R-11b/R-11c; pinned by the fixture.)
- [ ] **AC-05** — Given a committed `clio.tests/Theming/Fixtures/color-math-parity.json` of `hex →
  {generateScale, deriveSecondary, accentCandidates}` pairs captured **once** from the TS source
  (creatio-ui / freedom.scss) — frozen, not regenerable after the package is deleted — when each entry is run through
  the C# math, then the C# output **equals** the golden **exactly, hex-for-hex**. The fixture is **broad
  and adversarially seeded** — it includes inputs whose channels land near `x.5·255` rounding boundaries,
  near-gamut-boundary chromas, and `detectMode`/contrast-threshold boundaries (a coverage/count guard fails
  if the adversarial seeds are dropped — it is **not** "≈200 random hex"). (R-08, R-15; TC-U-P01, TC-U-P02,
  TC-U-P04.)
- [ ] **AC-06** — Given the parity fixture is the load-bearing gate, when it is run and **any** entry diverges
  by even one ULP, then the **pre-decided fallback is applied and recorded here** — **either** fdlibm
  `pow`/`cbrt` is ported (and the fixture then passes exactly) **OR** an explicit, documented tolerance
  constant + its justification is asserted. The decision branch taken is recorded in the test (and the
  workspace diary); "discover at the end" is not allowed. (R-08; TC-U-P03.)
- [ ] **AC-ERR** — Given a malformed (non-6-hex) input to `srgbChannels`, when parsed, then it surfaces a
  user-friendly failure (no bare `catch (Exception)`, no stack trace) — and the divergence from TS
  (`parseInt`→`NaN` vs `byte.Parse`→throw) is documented; `ColorNormalizer.Normalize` (Story 2) is the
  validating front door, so `ColorSpace` may assume normalized input.

## Implementation Notes

Port **only** the bottom of the pipeline + the fixture harness. The remaining math (`ColorNormalizer`, full
`PaletteGenerator`/`ColorMetrics`/`TextTokenResolver`/`FontImportBuilder`, `ThemeCssBuilder`) is Story 2 and
must not start until this gate is green or its fallback is recorded.

**Key file (create): `clio/Theming/ColorSpace.cs`** (ADR Implementation Plan, D3, R-07, R-09) — `internal static`
helper (no DI, no interface — math is deterministic, stateless, no collaborators; CLIO001/CLIO005 do not apply;
`clio.tests` reaches it via the existing `InternalsVisibleTo`). Port: `srgb`/`linearize`/`delinearize`,
`hexToOklch`, `hexToOklab`, `oklchToRgb`, `rgbToHex` (**JS `Math.round`, round-THEN-clamp** — R-07),
`maxChromaInGamut` (24 iters, `[0, 0.4]`), `toHex`, `detectMode`, and the gamut test
`channel >= -0.001 && channel <= 1.001` verbatim (R-11c). Matrices and constants **verbatim** from
`color-space.ts`; `Math.Pow` for `**`; `byte.Parse(..., NumberStyles.HexNumber, InvariantCulture)` (R-09).

**Key files (create): `clio/Theming/PaletteGenerator.cs`, `clio/Theming/ColorNormalizer.cs` (minimal)** — enough
to drive the fixture: `GenerateScale`, `DeriveSecondary`, `GenerateAccentCandidates` (+ internal `cuspL` with the
exact `double` `+=` loop: start `0.35`, step `0.01`, bound `0.85`, R-11b), and a minimal `Normalize` used only to
feed the fixture (the full alpha-reject/rgb/hsl/named matrix is Story 2). Establish the `clio/Theming/` namespace
(`Clio.Theming`) here; the `Theming` module trait + CLAUDE.md/AGENTS.md mapping land in Story 2 with the full
suite of tests.

**Key file (create): `clio.tests/Theming/ColorMathParityTests.cs` + `Fixtures/color-math-parity.json`**
(D7 layer 2, R-08, R-15) — the **harness**:
- Deserialize each fixture entry; run the C# math; compare hex-for-hex.
- Assert the fixture **contains** the adversarial boundary seeds (count/coverage guard — R-08; TC-U-P02).
- The fallback case (TC-U-P03) records which branch was taken (fdlibm port OR documented tolerance).
- The goldens are generated once from the TS source and frozen — not regenerable after the package is deleted (TC-U-P04).

Pattern to follow: the `PageSchemaMetadataHelper` / `ThemeRequestBuilder` `internal static`-utility precedent
(no DI, no `new` of behavior, reached via `InternalsVisibleTo`); `ComponentRegistrySnapshotTests` as the
fixture-driven golden-comparison shape.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` `[Property("Module","Theming")]` | **TC-U-P01** generated parity fixture matches C# exactly (the load-bearing gate); **TC-U-P02** fixture is broad + adversarially seeded (boundary-seed coverage guard) | `clio.tests/Theming/ColorMathParityTests.cs` (+ `Fixtures/color-math-parity.json`) |
| Unit `[Category("Unit")]` `[Property("Module","Theming")]` | **TC-U-P03** pre-decided fallback applied + recorded when ULP drift detected (fdlibm port OR documented tolerance); **TC-U-P04** the goldens are frozen regression pins — generated once from the TS source, not regenerable after it is deleted | `clio.tests/Theming/ColorMathParityTests.cs` |
| Unit `[Category("Unit")]` `[Property("Module","Theming")]` | **TC-U-04** JS-`Math.round` semantics + round-then-clamp order (`0.49999999999999994`→0; slightly-negative / slightly->1 pre-clamp channels) | `clio.tests/Theming/ColorSpaceTests.cs` |
| Unit `[Category("Unit")]` `[Property("Module","Theming")]` | **TC-U-09** (partial) gamut epsilon literals/operators pinned exactly (`>= -0.001 && <= 1.001`) | `clio.tests/Theming/ColorSpaceTests.cs` |
| Unit `[Category("Unit")]` `[Property("Module","Theming")]` | **TC-U-03** (partial) hex parse under non-invariant culture (`de-DE`) — `byte.Parse(..., HexNumber, InvariantCulture)`; malformed-input divergence documented | `clio.tests/Theming/ColorMathParityTests.cs` |

- All `clio.tests/Theming/` tests carry `[Category("Unit")]` (never `[Category("UnitTests")]`) **AND**
  `[Property("Module","Theming")]` (R-06 — no uncategorized tests).
- Test naming `MethodName_ShouldExpectedBehavior_WhenCondition` (e.g.
  `GenerateScale_ShouldMatchTsGolden_ForEveryParityFixtureEntry`,
  `RgbToHex_ShouldRoundThenClamp_WhenChannelIsHalfBoundary`,
  `ParityFixture_ShouldContainAdversarialBoundarySeeds_WhenLoaded`).
- AAA + a `because` on every assertion + `[Description]` on every test.
- Parity-gate-only run (Phase 0): `dotnet test --filter "Category=Unit&Module=Theming"`.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO005; `ColorSpace`/`PaletteGenerator` are `internal static` utilities, not DI registrations — CLIO005 clean)
- [ ] `ColorSpace` ports the matrices **verbatim**, uses `double` throughout, `Math.Pow` for `**`, `Math.Cbrt`/`Math.Pow`/`Math.Atan2`, the `((h%360)+360)%360` guard, and the `>= -0.001 && <= 1.001` gamut test exactly (D3, R-11c)
- [ ] `rgbToHex` uses **JS `Math.round` semantics** (NOT `Math.Floor(x+0.5)`) and **round-THEN-clamp** order; the `0.49999999999999994`-class anchor → 0 (R-07)
- [ ] Hex parse uses `byte.Parse(..., NumberStyles.HexNumber, CultureInfo.InvariantCulture)`; all numeric parse/format invariant-culture; malformed-input divergence documented (R-09)
- [ ] `Fixtures/color-math-parity.json` is **broad + adversarially seeded** (rounding-boundary, near-gamut-chroma, threshold-boundary inputs) — not "≈200 random hex" (R-08)
- [ ] **The gate is green** (TC-U-P01 passes hex-for-hex) **OR** the pre-decided fallback is applied and recorded (TC-U-P03: fdlibm `pow`/`cbrt` ported OR a documented tolerance constant + justification) — the chosen branch is written into the test and the workspace diary
- [ ] All Theming tests carry `[Category("Unit")]` **AND** `[Property("Module","Theming")]` — never `[Category("UnitTests")]`
- [ ] Targeted run: `dotnet test --filter "Category=Unit&Module=Theming"` (Phase 0 gate green before Story 2 starts)
- [ ] `.codex/workspace-diary.md` entry appended recording the parity outcome (bit-exact, or which fallback was taken)
- [ ] PR description references this story file and states the parity-gate outcome

## Dev Agent Record

- Implementation started: 2026-06-26
- Implementation completed: 2026-06-26
- Tests passing: **18/18** on **both** target frameworks (`net8.0` and `net10.0`), via
  `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=Theming"`. No new
  Roslyn/CLIO warnings from the added code (the two CLIO005 warnings in the build output are
  pre-existing on `CreateEntityBusinessRuleCommand`/`CreatePageBusinessRuleCommand`, unrelated).
- Parity-gate outcome: **BIT-EXACT — no fallback needed.** `Math.Pow`/`Math.Cbrt` matched V8 on the
  full adversarial fixture (56 entries × 12-shade `generateScale` + `deriveSecondary` +
  `accentCandidates`) hex-for-hex, on .NET 8 and .NET 10. `BitExactToleranceUlps = 0` is pinned in
  `ColorMathParityTests`; the R-08 fdlibm-port / documented-tolerance fallback was **not** required.
- Notes:
  - Golden generated **once** by running the creatio-ui TS source (`libs/devkit/theming`) via
    `npx tsx parity-driver.mts`. After `@creatio/theming` is deleted the goldens are frozen and not
    regenerable — the C# port is canonical. (`Fixtures/README.md` was later removed.)
  - `JsRound` reproduces JS `Math.round` by comparing the exact fractional part (not `Math.Floor(x+0.5)`);
    `0.49999999999999994 → 0` is pinned (R-07). Hex parse uses `byte.Parse(HexNumber, InvariantCulture)`;
    a `[SetCulture("de-DE")]` test confirms culture-invariance (R-09).
  - **Deviation from the prose:** `hexToOklab` is NOT ported here — it does not exist in `color-space.ts`
    and is unused by the Story 1 surface (`generateScale`/`deriveSecondary`/`accentCandidates`); it
    belongs with `distanceOklab`/`Metrics` in Story 2. Ported exactly what `color-space.ts` contains.
  - Files: `clio/Theming/{ColorSpace,PaletteGenerator,ColorNormalizer}.cs` (constants inline, no `Models/`);
    `clio.tests/Theming/{ColorMathParityTests,ColorSpaceTests}.cs`
    + `Fixtures/color-math-parity.json`; `clio.tests/clio.tests.csproj` (Theming/Fixtures glob).
  - **Gate is green → Story 2 (full math port) is unblocked.** Nothing committed (no PR opened).
