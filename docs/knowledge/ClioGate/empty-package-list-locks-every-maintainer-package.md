---
description: PackageUnlocker.CallGate turns an empty package array into a null payload, and cliogate LockPackages/UnlockPackages treat null or empty as "every package with this Maintainer code" - an empty list is a bulk operation, never a no-op
applies-to:
  - clio/Package/PackageUnlocker.cs
  - cliogate/Files/cs/CreatioApiGateway.cs
date: 2026-08-19
---

**What is true** — `PackageUnlocker.CallGate` sends `{"unlockPackages": null}` whenever the
collection it was given is empty (`packages.Length > 0 ? packages : null`). On the gate side both
`UnlockPackages` and `LockPackages` test `list != null && list.Any()`; the else branch runs a single
`Update` on `SysPackage` filtered only on `Maintainer` (lock additionally excludes `Custom`), so it
rewrites `InstallType` for every package of the maintainer. The parameterless `Unlock()` / `Lock()`
overloads exist for precisely that intent.

**Why it is this way** — "unlock everything this maintainer owns" is the post-install step clio
needs, and the WCF signature has no separate verb for it, so the absent argument is the selector.

**What breaks if you ignore it** — filtering a package list down to nothing (all names skipped, a
lookup returned no rows) and then calling `Unlock(list)` silently unlocks or locks the whole
maintainer's package set instead of doing nothing. The bulk branch also ignores its own
`update.Execute()` row count and returns `true`, so the call reports success either way. Guard the
empty case at the call site if you mean a no-op.
