---
description: ColorMetrics.White and ColorMetrics.Dark are hardcoded copies of the creatio-ui --crt-color-base-light (#ffffff) and --crt-color-base-dark (#181818) primitives, and because the dark one is a near-black rather than #000000 the text-on-* highest-contrast fallback is a live path and some backgrounds get no AA-passing text at all
applies-to:
  - clio/Theming/ColorMetrics.cs
  - clio/Theming/TextTokenResolver.cs
  - clio/Theming/ThemeCssBuilder.cs
  - clio.tests/Theming/Fixtures/theme-css-golden.json
ticket: ENG-94155
date: 2026-08-24
---

**What is true** — `--crt-color-base-light: #ffffff` and `--crt-color-base-dark: #181818` are declared
in **creatio-ui**, outside this repository. `ColorMetrics.White` and `ColorMetrics.Dark` are hardcoded
copies of those two values: `TextTokenResolver.ResolveTextOnColorToken` computes the `text-on-*`
contrast decision from the copies, while `ThemeCssBuilder` emits `var(--crt-color-base-light)` /
`var(--crt-color-base-dark)`. Because the dark reference is a near-black and not `#000000`, all three
candidates can fail AA on a mid-tone background — so the highest-contrast fallback at the end of the
method is a live path, and for some backgrounds **no** AA-passing text colour exists. Two of the five
cases in `theme-css-golden.json` resolve through that fallback and both land below 4.5:1 (`#e91e63` →
light at 4.347, `#fc172d` → dark at 4.481).

**Why it is this way** — `ThemeService` accepts only `cssContent` text, so there is no channel to read
the environment's primitives at build time; duplicating the values is the only way to compute a contrast
decision about a colour the theme does not own. Emitting the primitive rather than a literal is a
separate, deliberate choice: `SetColorDeclaration` takes an arbitrary value string, so
`--crt-color-text-on-accent: #000000` is perfectly emittable and would not redefine any primitive — the
`text-on-*` tokens belong to the theme. It is emitted as `var(--crt-color-base-dark)` so on-colour text
matches `--crt-color-text-body`, which the template already binds to the same primitive, and so it
follows creatio-ui if that value ever moves.

**What breaks if you ignore it** — Substituting the intuitive `#000000` inflates every dark-reference
contrast by 18% and flips real outcomes: on the `vivid-derived` golden background `#e91e63` the
candidate reads 4.83 instead of 4.084, so it looks AA-passing and gets chosen, where the correct result
is the fallback picking the light reference at 4.347. The same applies if creatio-ui ever changes a
primitive and the copy here is not updated — every `text-on-*` decision drifts, and in a running
environment nothing catches it: `build-theme` exits 0, the theme installs, and the WCAG claim is wrong
only in the browser. The test suite does catch the edit, but obliquely: changing either copy fails
`ThemeCssBuilderTests.Build_ShouldMatchCommittedGolden_ForEveryBuilderFixtureCase` plus a
`TextTokenResolverTests` or `ColorMetricsTests` anchor, reporting a golden length or a resolution kind
and never naming a primitive. This record is the only place that names the cause.
