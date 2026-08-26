---
description: set-user-theme resolves a selector only against the GetAvailableThemes catalog, so there is deliberately no 'default'/'dark' alias map for the built-in themes - the alias map was written, proved not to work and was deleted; --reset is the only way back to the stock theme
applies-to:
  - clio/Command/Theming/UserThemeApplier.cs
  - clio/Command/Theming/SetUserThemeCommand.cs
ticket: ENG-93302
date: 2026-08-19
---

**What is true** — `UserThemeApplier.TryResolveTargetTheme` resolves the selector tier by tier (id,
then css class name, then caption) against the themes `IThemeCatalog.TryGetAvailableThemes` returned,
and against nothing else. There is no hard-coded table of built-in theme names, and adding one is not
a missing convenience: the platform's built-in themes are not returned by
`ThemeService.svc/GetAvailableThemes`, so no selector for them can ever resolve, and writing a
built-in's css class name into the profile does not apply it either (the Shell resolves the profile
value by theme **id**). `--reset`, which writes an empty `Theme`, is the supported route back to the
environment default.

**Why it is this way** — the first version of the command shipped a built-in alias map (`default`,
`dark`) that translated to css class names. It was deleted after live testing: the write was stored
faithfully and the Shell silently fell back to the stock theme, so the command reported success and
nothing changed.

**What breaks if you ignore it** — re-adding aliases produces the worst possible outcome, a command
that succeeds and verifies (the read-back matches what was written, because the server stores any
string without validating it against a real theme) while the user's UI is unchanged. If a built-in
theme ever has to be selectable, the id has to come from a platform source that lists built-ins, not
from a constant in clio.
