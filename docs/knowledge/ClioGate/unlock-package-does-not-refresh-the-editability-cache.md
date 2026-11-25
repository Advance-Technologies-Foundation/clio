---
description: clio lock-package / unlock-package rewrite SysPackage.InstallType directly - a session that already read the package keeps its old editability, the system connection's until the site restarts
applies-to:
  - cliogate/Files/cs/CreatioApiGateway.cs
ticket: ENG-106400
date: 2026-11-25
---

**What is true** — `lock-package` and `unlock-package` (ClioGate `LockPackages` / `UnlockPackages`) change
`SysPackage.InstallType` with a direct `Update`. The platform decides whether a package is editable from
`PackageEditabilityCache`, which lives in a session's `SessionData`: the first editability read in a session
(`PackageDBProvider.GetIsPackageForeignByUId`) is kept for that session, and a direct update does not touch it. The
system connection's session, which the platform's own version switch uses, lasts until the site restarts. Measured
on `studio-16354310` (2026-11-25): after `unlock-package UsrPosV` the row read `InstallType = 0`, yet the package
still read as not editable until `restart-web-app`. The lock looked immediate only because the site had just
restarted, so no session had read the package yet.

**Why it is this way** — the platform caches editability per session so that schema saves do not query
`SysPackage` each time; its own package operations update only the calling session's cache, and a ClioGate raw
update updates none.

**What breaks if you ignore it** — after a lock or an unlock, a session that read the package before (the system
connection above all) keeps the old answer: a save into the package, or a version switch that decides whether a
member is foreign, behaves as if the package were unchanged. A test that locks a package to stage a foreign member
it created there sees the switch treat the member as editable, and nothing reports it. Restart the site after
`lock-package` or `unlock-package` before relying on the new state.
