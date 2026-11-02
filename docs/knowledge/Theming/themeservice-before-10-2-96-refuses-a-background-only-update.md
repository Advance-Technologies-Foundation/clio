---
description: ThemeService before Creatio 10.2.96 does not know the theme background, so clio sends UpdateTheme with only id and background and does not read the write back
applies-to:
  - clio/Command/Theming/ThemeBackground.cs
  - clio/Command/Theming/ThemeServiceRequirement.cs
ticket: ENG-99181
date: 2026-10-30
---

**What is true** — `background` on the ThemeService requests arrived with core merge `07c5e28f924`, first
in build `10.2.96`. Older builds ignore the member: `UpdateTheme` with only `id` and `background` fails with
"No updatable fields were provided", `CreateTheme` with a background stores none and reports success.

**Why it is this way** — sending only `{ id, background }` turns an older ThemeService into an explicit
error, even on an unversioned build (`0.0.0.0`) that passes `[RequiresCreatioVersion]`, so no read-back.

**What breaks if you ignore it** — adding another field to the update, or using `CreateTheme`, makes an older
build report a success that stored nothing.
