---
description: CurrentPackageId must be read with ISysSettingsManager.GetSysSettingValueByCode (per-user), never GetAllUsersDefaultByCode - it is a per-developer setting and the All-Users default usually names no package
applies-to:
  - clio/Command/PackageTargetResolver.cs
  - clio/Common/ISysSettingsManager.cs
date: 2026-08-19
---

**What is true** — `PackageTargetResolver.ResolveCurrentPackage` reads the `CurrentPackageId`
system setting through `sysSettingsManager.GetSysSettingValueByCode`, which resolves the value for
the authenticated user first. It deliberately does not use `GetAllUsersDefaultByCode`, even though
that is the method clio's own `get-sys-setting` MCP surface is contractually bound to, and even
though the two look interchangeable at the call site.

**Why it is this way** — in Creatio `CurrentPackageId` is a personal setting: each developer picks
the package their design-time writes land in, and that choice is stored as a per-user value. The
All-Users default is a different row and on a normal development environment names either nothing
or whichever package happened to be current when the environment was set up.

**What breaks if you ignore it** — "unifying" the sys-setting reads onto the All-Users accessor
turns every blank `--package` into either a hard failure ("the environment's CurrentPackageId system
setting does not point at one") on an environment where the developer's package is set perfectly
well, or, worse, a silent delivery of theme, branding and data-binding rows into somebody else's
package. Nothing in the response distinguishes that from success: the run reports the package it
resolved, and only a later `list-packages` or a lost customization reveals the wrong target.
