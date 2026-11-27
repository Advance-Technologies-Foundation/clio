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
is left with. Both branches are measured on `studio-16354310`: the winning one on 2026-11-24 (the version's package
at level 190 above the root's 167), the losing one on 2026-11-25 (a version in an editable package at 185 against a
member flagged active in a locked package at 192: the confirmed switch wrote the target and the platform kept
picking the locked member, answered `failed-not-restored`). The same run measured that a switch clears a member's
user-property pin: before the switch a version created next to the pinned vendor root warned, after it no longer did.

**What breaks if you ignore it** — a switch the platform loses leaves new records starting a member nobody
chose, and `failed-not-restored` is never undone. So the package warns at creation and in the activation dry run,
naming the package to add a dependency on, and refuses the real switch `package-position-confirmation-required`
until `confirmPackagePosition` (clio: `confirm-package-position`) carries the user's yes. A pin alone counts
neither at creation nor at activation, since the switch clears it. It stays a confirmation, not a refusal no flag lifts: a switch made
anyway keeps the target flagged, so a dependency added afterwards may make it win once the schema managers reload
(a compile or a restart; not measured), and a tie between equal ranks (then `Version`, then `Name`) is not measured. Dropping the refusal brings back the silent
wrong pick; counting a pin refuses a switch that would succeed, and at creation warns about a member that ranks
below the version and names a dependency that is already there.
