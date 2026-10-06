---
description: activating a culture (SysCulture.Active) is not enough for the UI to load in it - the per-culture client resources do not exist until a full configuration compile (compile-configuration --all) runs; no restart is needed
applies-to:
  - clio/Command/Localization/CultureMessages.cs
  - clio/Command/LocalizePageCommand.cs
ticket: ENG-90576
date: 2026-09-26
---

**What is true** — after a culture is activated in the Languages section (`SysCulture.Active = true`), the
client resources of that culture are still missing: on stand `eng90576` (2026-09-26)
`0/conf/content/resources/es-ES/ConfigurationConstantsResources.js` answered 404 and the shell did not load
when the user switched to es-ES. A full configuration compile (`clio compile-configuration --all`, about
8 minutes there) generated them and the shell then loaded in es-ES. No application restart was needed.

**Why it is this way** — the per-culture client resource files are build output of the configuration
compile; activating a culture only changes the `SysCulture` row and does not trigger a build.

**What breaks if you ignore it** — translations written with `localize-page` or `update-app-section` into a
freshly activated culture are stored and read back correctly, yet a user who switches to that culture gets a
shell that does not load, and nothing in the write result says why. That is why
`CultureMessages.CultureInactiveWarningFormat` names the compile step.
