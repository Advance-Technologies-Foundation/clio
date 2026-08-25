---
description: ThemeService.svc/GetTheme exists on trunk and answers HTTP 200 with an EMPTY body for every parameter shape, so get-theme reads the CSS from the cssFilePath the GetAvailableThemes catalog publishes instead
applies-to:
  - clio/Command/Theming/GetThemeCommand.cs
  - clio/Command/Theming/IThemeCatalog.cs
ticket: ENG-93991
date: 2026-08-25
---

**What is true** — `get-theme` reads a theme's CSS in two hops: resolve the theme through the
`GetAvailableThemes` catalog, then GET the `cssFilePath` that catalog entry publishes — the same
static file the Shell loads. It does **not** call the native `ThemeService.svc/GetTheme`. That
endpoint does exist: probes to unknown `ThemeService` methods come back as a WCF
"Endpoint not found" HTML page, while `GetTheme` answers HTTP 200. But it returned an **empty body**
for every parameter shape probed on trunk — `{"id"}`, `{"themeId"}`, wrapped, css-class-name, `{}`,
including a known-good theme id. Its request contract is opaque, so it is unused. There is no
`KnownRoute` entry for it.

**Why it is this way** — the catalog + static file pair is the only path proven to return content.
`spec/adr/adr-theming.md` E-D1 carries the probe evidence.

**What breaks if you ignore it** — "the platform has a GetTheme endpoint, why are we doing two
requests?" is the predictable review question, and switching to it produces a tool that reports
success with empty `cssContent` on every call. An empty body is not distinguishable from a theme
that genuinely has no CSS, so the failure is silent and the `update-theme` round-trip then writes
that emptiness over a real theme.
