# Story 8: Go-live (Phase 5) — verify parity, flip the toggle on, surface public docs

**Feature**: theming-native-build (ENG-90636, epic ENG-26797 — Theming with AI, clio dev flow; native-build + bundled-template continuation)
**FR coverage**: D6, R-12 (rollout) — the surface-complete go-live
**PRD**: design substance in the approved migration plan ([adr-theming-native-build.md](../adr/adr-theming-native-build.md) §Context)
**ADR**: [adr-theming-native-build.md](../adr/adr-theming-native-build.md) — **Accepted**; refinements R-01…R-18 are authoritative and supersede the pre-review body
**Test plan**: [tp-theming-native-build.md](../test-plans/tp-theming-native-build.md)
**Status**: deferred
**Size**: M (half day)

> **DEFERRED — surface-gated (R-12).** The feature is **dark until the clio surface is complete**
> (Stories 1–6: math, bundled template provider, command, MCP tool, guidance, docs) and parity is verified.
> The template is **bundled in clio** (`tpl/themes/{version}/`), so nothing external gates it. This story
> verifies parity against the committed template/builder contract guard and **flips
> `[FeatureToggle("theming")]` on**. **Public docs (`Commands.md`/help/wiki) land here** (the gated verb
> enters the generated export baseline only when the toggle is on). **GATE:** the clio surface complete +
> parity verified — there is no external producer dependency.
> **Depends on Stories 4 + 6** (the surface + the authored docs).

---

## As a

clio release owner taking `build-theme` from feature-toggled-off to shipping enabled

## I want

the bundled template verified against the builder's contract guard, and the feature toggle flipped on once parity
is verified and the surface is complete

## So that

`build-theme` ships enabled and the bundled `theme.css.tpl` is verified to match the builder's contract — with
the public docs surfacing the moment the verb un-darkens

---

## Acceptance Criteria

- [ ] **AC-01 (bundled template)** — Given the version-pinned template ships bundled in clio under
  `tpl/themes/{version}/` (D5), when `build-theme` runs, then it reads the template via `IThemeTemplateProvider`
  (Story 3) and produces a valid `theme.css`; go-live is just the toggle flip, with no external template switch. (D5;
  TC-M-04.)
- [ ] **AC-02 (parity against the contract guard)** — Given the bundled `theme.css.tpl`, when the in-repo
  template/builder contract guard runs, then it asserts the committed `Fixtures/theme.css.tpl` contains every
  token the builder rewrites (all `--crt-palette-*`, the finalized `--crt-color-*` roles, `<%themeCssClass%>`,
  the strippable header comment); a drift fails loudly. The template is bundled, so a change lands in the same
  PR — no cross-repo live-snapshot guard exists or is needed. (D7, R-15; TC-M-04.)
- [ ] **AC-03 (parity verified before flip)** — Given the bundled template, when compared against the
  builder's template/builder contract (every token the builder rewrites is present), then it passes **before**
  the toggle is flipped on; a drift fails loudly. (D7.)
- [ ] **AC-04 (flip the toggle on — R-12 Phase 5)** — Given parity is verified, when go-live lands, then
  `[FeatureToggle("theming")]` is **enabled** (the feature un-darkens) and `build-theme` is reachable on all
  four surfaces (CLI parse, help/docs, dispatch, MCP). The flip is the deliberate divergence point from the
  toggle-off window. Note: `theming` is a **shared** key gating the whole theming surface (OQ-02), so the flip
  un-darkens `build-theme` **and** the theme CRUD (`create-theme`/`update-theme`/`delete-theme`/`list-themes`/
  `clear-themes-cache`) + guidance together. (R-12; TC-E2E-01 with the toggle on.)
- [ ] **AC-05 (public docs land — D6)** — Given the toggle is on, when the generated public docs export runs,
  then `build-theme` enters the deterministic export baseline — `Commands.md`/help/wiki (authored in Story 6)
  surface the verb publicly **here**, not before. A `RELEASE.md` note is added now that the toggle ships on.
  (D6, Consequences "breaking change: No".)
- [ ] **AC-06 (go-live gated on the clio surface only — R-12)** — Given the template is bundled in clio, when
  this story is planned, then go-live is gated **only** on the clio surface being complete (Stories 1–6) +
  parity verified — there is **no external producer dependency** and no clio-side fallback question. (R-12.)
- [ ] **AC-ERR (gate documented)** — Given the clio surface (tests, docs, MCP tool) is not yet complete, when
  the story is picked up, then it stays `deferred` — do **not** flip the toggle before the surface is complete
  and parity is verified. (R-12.)

## Implementation Notes

Surface-gated. Stories 1–6 are fully shippable behind the toggle; this story is the go-live once the surface is
complete + parity verified. **Use the `document-command` skill for the `RELEASE.md`/public-docs step; review MCP
artifacts per the AGENTS.md MCP maintenance policy** (the toggle-on manifest is the live surface).

**Key check: parity against the in-repo contract guard** (ADR Implementation Plan, D7, R-15) — the bundled
`tpl/themes/{version}/theme.css.tpl` and its committed test copy `Fixtures/theme.css.tpl` are asserted by the
template/builder contract guard (Story 2): every token the builder rewrites is present. The template is bundled,
so a change lands in the same PR — there is no cross-repo live-snapshot guard.

**Key change: flip `[FeatureToggle("theming")]` on** — enable via the appsettings `features` object / the
release configuration (`clio experimental --name theming --enable` is the local trigger). Verify
`build-theme` is then reachable on all four surfaces and advertised by the real `clio mcp-server` (TC-E2E-01
with the toggle on — manual, not in CI).

**Key file (modify): `RELEASE.md`** — add the `build-theme` go-live note now that the toggle ships on
(additive verb + tool; edits to guidance/docs; scaffold devDependency removal in Story 7).

**Manual runbook (TC-M-04):** verify the bundled-template parity against the contract guard; flip the toggle on;
run `build-theme` so it produces valid CSS from the bundled template; confirm parity **before** flipping the
toggle on for release.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| E2E `[Category("E2E")]` (manual, NOT in CI) | **TC-E2E-01 (toggle ON)** real `clio mcp-server` advertises `build-theme` with the four flags + the `get-guidance theming` description; produces valid CSS from the bundled template | `clio.mcp.e2e/BuildThemeToolE2ETests.cs` |
| Manual / live stand | **TC-M-01** no-code end-to-end (build → create → list → set default); **TC-M-03** workspace end-to-end (build `--output` → push-workspace → list); **TC-M-04** verify bundled-template parity against the contract guard + flip the toggle | runbook + PR notes |

- `[Category("E2E")]` (never `[Category("UnitTests")]`); naming `MethodName_ShouldBehavior_WhenCondition`.
- AAA + a `because` on every assertion + `[Description]` on every test. Flag the E2E-not-in-CI gap.

## Definition of Done

- [ ] **GATE confirmed cleared** before leaving `deferred`: the clio surface (Stories 1–6: math, bundled template
  provider, command, MCP tool, guidance, docs) is complete **and** parity is verified — otherwise this story stays `deferred` (feature dark)
- [ ] Bundled-template parity verified against the builder's template/builder contract **before** the flip (D7)
- [ ] `[FeatureToggle("theming")]` flipped **on**; `build-theme` reachable on all four surfaces + advertised by the real `clio mcp-server` (TC-E2E-01 toggle-on, manual)
- [ ] Public docs land: `Commands.md`/help/wiki (Story 6) surface the verb in the generated export baseline; `RELEASE.md` note added (D6)
- [ ] Manual live-stand runbook (TC-M-01/03/04) recorded in the PR; E2E not in CI (manual)
- [ ] PR description references this story file and states the go-live outcome

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started:
- Implementation completed:
- Surface-complete gate cleared (Stories 1–6 done + parity verified):
- Toggle flipped on:
- Manual live-stand run:
- Notes:
