---
description: fonts.googleapis.com/css2?family=Verdana answers 200 with a substitute, so only fonts.google.com/metadata/fonts/<Family> can decide whether a family is published; a curated OS-font allowlist was tried and deleted
applies-to:
  - clio/Theming/GoogleFontsCatalog.cs
  - clio/Command/Theming/BuildThemeCommand.cs
ticket: ENG-93985
date: 2026-08-19
---

**What is true** — `GoogleFontsCatalog` probes `https://fonts.google.com/metadata/fonts/<Family>`
and nothing else. The obvious alternative, requesting the CSS the theme would import
(`fonts.googleapis.com/css2?family=<Family>`), is not an existence check: css2 answers **200** for
Verdana, Tahoma, Georgia, Times New Roman, Courier New, Trebuchet MS, Consolas and others, serving a
metric-compatible substitute **under the requested family name**. The metadata endpoint answers 404
for those and 200 for Roboto, Lato, Inter, Noto Sans and Cascadia Code.

**Why it is this way** — the first implementation of this feature was a curated allowlist of
OS-bundled family names (`SystemFontFamilies` plus a `ThemeFontClassifier`, ~45 names, a drift oracle
and a grammar invariant) filtered out before the import was assembled. It was deleted: no static list
and no weight-count heuristic can separate the two populations. Lato is a genuine Google family that
css2 serves at a single weight, and Noto Sans and Cascadia Code ship with some operating systems yet
are genuinely published on Google Fonts, so each heuristic misclassifies in a different direction.
The per-family metadata response is 12-27 KB against 2.7 MB for the full catalogue, so no caching of
a catalogue and no JSON array parsing is needed.

**What breaks if you ignore it** — "simplify" the probe onto css2 and every OS font is reported as a
Google font again: build-theme emits an `@import` for Verdana, the downloaded substitute shadows the
installed face, and the original bug ("theme styles are not applied") returns with the check
apparently passing. Reintroducing the allowlist instead brings back a table that has to be maintained
against a catalogue that renames families, and it will be wrong for Lato-shaped and Noto-Sans-shaped
names on the day it is written.
