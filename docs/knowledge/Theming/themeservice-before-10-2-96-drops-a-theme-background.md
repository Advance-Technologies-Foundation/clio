---
description: ThemeService before Creatio 10.2.96 does not know the theme background - CreateTheme drops it and reports success, UpdateTheme with only id and background is refused - so clio checks the 10.2.96 floor itself before any background write
applies-to:
  - clio/Command/Theming/CreateThemeCommand.cs
  - clio/Command/Theming/UpdateThemeCommand.cs
  - clio/Command/Theming/ThemeServiceRequirement.cs
ticket: ENG-99181
date: 2026-11-04
---

**What is true** — `background` on the ThemeService requests arrived with core merge `07c5e28f924`, first
in build `10.2.96`. Older builds ignore the member. `CreateTheme` with a background stores none and reports
`success: true`. `UpdateTheme` with only `id` and `background` fails with "No updatable fields were
provided", and an update that also carries content fields rewrites them and drops the background.

**Why it is this way** — WCF `DataContract` deserialization drops unknown members, so an older service never
sees the field.

**What breaks if you ignore it** — without the floor, an older environment reports a created or updated theme
without its background, and `UsePanelIconBackground` is still turned off for everyone. An unversioned
development build (`0.0.0.0`) passes the floor, so an old development core loses the background the same way;
check it with `get-theme`.
