---
description: SysPackage.IsLocked is not a delete gate on .NET — the platform deletes IsLocked=true packages — and create-package WITH dependencies leaves IsLocked=true on the new package, so IsLocked must never be used as the "cannot delete this" signal
applies-to:
  - clio/Command/PackageDeletionPreflight.cs
  - clio/Package/PackageDependencyManager.cs
ticket: issue-1784
date: 2026-11-27
---

**What is true** — measured on a local stand (Creatio 10.2.965, .NET 10, PostgreSQL) on 2026-11-27: a package
created with `create-package` reads `IsLocked=false`; the same create with `--dependencies` reads
`IsLocked=true` afterwards — the dependency save (`SavePackageProperties`) is what flips it. Deleting an
`IsLocked=true` scratch package (same maintainer, `InstallType=0`, no dependents) through
`AppInstallerService.svc/DeletePackage` succeeds: the platform's delete gate (`PackageManager.CanDeletePackages`)
consults `CheckPackagesLock`, which on .NET checks only source-control file locks under `#if NETFRAMEWORK`, never
`SysPackage.IsLocked`. The rule that actually refuses a delete is the foreign rule
(`PackageDBProvider.GetIsPackageForeign`): `maintainer != environment maintainer || installType != SourceControl(0)`.

**Why it is this way** — `IsLocked` is a design-time checkout marker written by `PackageLocker.TryLockPackage`
(which on .NET always locks in the DB, source control disabled), and the package-properties save path locks without
a matching unlock. It marks "someone is editing this in the designer", not "this package must not be deleted".

**What breaks if you ignore it** — a delete pre-flight that treats `IsLocked=true` as "cannot delete" refuses
exactly the scratch-with-a-dependency package the delete tool exists for (any `create-package --dependencies`
output is permanently undeletable through the tool, while the CLI deletes it fine — a split brain the caller
cannot see). `PackageDeletionPreflight.IsForeign` therefore reads only `Maintainer` and `InstallType`, and compares
the maintainer against the environment's `Maintainer` setting (a `MediumText` setting: the value lives in
`SysSettingsValue.TextValue`, not on `SysSettings`, whose `Value` column SelectQuery cannot even project).
