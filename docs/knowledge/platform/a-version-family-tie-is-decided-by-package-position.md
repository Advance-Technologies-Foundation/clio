---
description: two members of a case or process version family both flagged active - the platform picks by package HierarchyLevel, so a version in a lower-ranked package can lose although its activation succeeded
applies-to:
  - clio/Command/DynamicCaseService.cs
  - clio/Command/SetActiveDynamicCaseVersionCommand.cs
  - clio/Command/DynamicCaseVersionModels.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/SetActiveDynamicCaseVersionTool.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/ModifyDynamicCaseAsNewVersionTool.cs
ticket: ENG-106400
date: 2026-11-24
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

**What breaks if you ignore it** — the package therefore treats this as a WARNING, not a refusal:
`modify-dynamic-case-as-new-version` and the activation dry run warn and name the package to add a dependency on,
and `set-active-dynamic-case-version` reads the pick back and reports `failed-not-restored` naming the member that
really won. A clio or guidance change that turned the warning into a refusal would block a user whose package does
rank higher, because the losing branch is not measured.
