---
description: after activating another member, a version-family member in a read-only package still stores IsActiveVersion=True; a "no sibling still flagged" read-back must skip such members
applies-to:
  - clio/Command/DynamicCaseService.cs
  - clio/Command/SetActiveDynamicCaseVersionCommand.cs
  - clio/Command/DynamicCaseVersionModels.cs
ticket: ENG-106400
date: 2026-11-24
---

**What is true** — when `SetActiveVersionItem` switches a case (or process) family to another member, a member
that lives in a package that does not accept changes gets nothing written: its stored
`SysSchemaProperty.IsActiveVersion` stays `True` and its `SysSchema.ModifiedOn` does not move. `VwSysDcmLib` then
says `false` for it while the stored row says `True`; only the platform's pick (`GetActualVersionUId`) is the
truth. Measured on `studio-16354310` (2026-11-24) with the vendor root `OmniChatCase` in `OmnichannelMessaging`.

**Why it is this way** — the platform cannot re-save a schema in a read-only package, so deactivating it is not a
write it can make; package position decides between it and the target instead (see
[a version family tie is decided by package position](a-version-family-tie-is-decided-by-package-position.md)).

**What breaks if you ignore it** — `SetActiveCaseVersion` excludes such members from `siblingsStillFlagged` and
lists them in `decidedByPackagePosition`. A read-back on the clio side (or in guidance) that counted every member
still reading `True` as a failed switch would report every activation over a vendor root as failed, and an agent
would retry or "repair" a switch that took.
