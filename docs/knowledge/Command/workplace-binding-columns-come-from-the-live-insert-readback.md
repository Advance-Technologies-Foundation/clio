---
description: place-section-in-workplace reads SysWorkplace LoaderId (and Type, unless workplace-type is passed) back after the DataService insert instead of sending them
applies-to:
  - clio/Command/WorkplacePlacementService.cs
ticket: ENG-103017, ENG-103652
date: 2026-10-19
---

**What is true** — A DataService `InsertQuery` of `SysWorkplace` that sends only `Id`, `Name` and
`SysApplicationClientType` gets `Type` (General) and `LoaderId` filled by the platform. Measured on
Creatio 10.2.395 (.NET Framework). When `workplace-type` is passed, the insert also sends `Type`, and
`LoaderId` is still filled (measured on Creatio 10.2.421, .NET 10: a Portal insert got the same loader as the
General workplaces). `place-section-in-workplace` therefore inserts the row first, then
reads it back and upserts the binding row with `Type`, `LoaderId`, `Position` and the client type. On the
same stand, a DataService `UpdateQuery` moved create-app's `SysModuleInWorkplace` row to another workplace,
and `SaveSchema` accepted the junction bindings once the target `SysWorkplace` was bound in the same package.

**Why it is this way** — clio has no source for those two values other than the platform's own defaults,
and a binding ships only the columns it carries: an install supplies no defaults.

**What breaks if you ignore it** — If the read-back is dropped, or the binding is written before the insert,
the package installs a workplace with an empty `Type` and `LoaderId` on every other environment. A
corrected package cannot repair that, because bindings are first-install-only. The tool warns when the
read-back comes back empty; treat that warning as a platform change, not as noise.
