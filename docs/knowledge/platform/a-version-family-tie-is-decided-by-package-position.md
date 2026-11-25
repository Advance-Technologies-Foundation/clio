---
description: two members of a case or process version family both flagged active - the platform picks by package HierarchyLevel, so a version in a lower-ranked package can lose although its activation succeeded
applies-to:
  - clio/Command/DynamicCaseService.cs
  - clio/Command/SetActiveDynamicCaseVersionCommand.cs
  - clio/Command/DynamicCaseVersionModels.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/SetActiveDynamicCaseVersionTool.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/ModifyDynamicCaseAsNewVersionTool.cs
ticket: ENG-106400
date: 2026-11-25
---

**What is true** — which member of a version family is "the active one" is not a single stored flag. The
platform computes it per family: a user property (`SysSchemaUserProperty`) first, then the stored
`SysSchemaProperty.IsActiveVersion` (a member with no row reads `True`, the instance default), and between two
members still flagged `True` the one in the package with the higher `SysPackage.HierarchyLevel` wins, then
`Version`, then `Name`. A member in a read-only package keeps reading `True` after another member is activated
(see [a deactivated foreign member keeps reading True](a-deactivated-foreign-version-keeps-reading-active.md)), so a
version saved into a package ranked AT OR BELOW such a member can be "activated" successfully and still not be the
one the bar and new records use.

**Why it is this way** — the tie-break by package position is how the platform resolves the two `True` members it
is left with. Measured on `studio-16354310` (2026-11-24) for the winning branch only (the version's package at level 190 above
the root's 167); the losing branch is traced in core source, not measured, because the designer always puts a
version above its root.

**What breaks if you ignore it** — a switch the platform may lose leaves new records starting a member nobody
chose, and `failed-not-restored` is never undone. So the package warns at creation and in the activation dry run,
naming the package to add a dependency on, and refuses the real switch
`package-position-confirmation-required` until `confirmPackagePosition` (clio: `confirm-package-position`) carries
the user's yes; `set-active-dynamic-case-version` still reads the pick back and reports the member that really
won. It is a confirmation, not a hard refusal, on purpose: the losing branch is not measured, so a user whose
package does rank higher must still be able to switch. Dropping the confirmation flag, or turning the check into a
refusal no flag lifts, would block that user; dropping the refusal brings back the silent wrong pick.
