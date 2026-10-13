---
description: the CurrentPackageId system setting holds a SysPackage UId despite its name, and is one non-personal All-employees value; filtering SysPackage by Id resolves nothing on a normal stand
applies-to:
  - clio/Command/PackageTargetResolver.cs
  - clio.tests/Command/PackageTargetResolverTests.cs
  - clio.tests/Command/EnvironmentPackageDataBinderTests.cs
ticket: ENG-99970
date: 2026-09-27
---

**What is true**
- `CurrentPackageId` stores a `SysPackage.UId`, not a `SysPackage.Id`. It is a Lookup on `VwSysPackages`, whose `Id` column is `SysPackage.UId`.
- The platform writes it and reads it as a UId: `Terrasoft.Core/Packages/WorkspaceUtilities.cs` `ForceGetCurrentPackageUId`, `PackageDBDeterminer`, `AppManager`, `ProcessSchemaManagerService`.
- It is created with `IsPersonal = false` (`WorkspaceUtilities.CreateCurrentPackageIdSettingsIfNeed`). Its value is therefore the single All-employees default, and there are no per-user rows.
- Measured on d_krestov_n (Creatio 10.1.37, .NET Framework) on 2026-09-27:
  - the setting has `IsPersonal: false` and exactly one `SysSettingsValue` row: All employees, `IsDef: true`, `a00051f4-cde3-4f3f-b08e-c5ad1a5c735a`;
  - that value is the UId of `Custom`, whose Id is `84076ac3-ce2d-4716-887a-7fcf5858e2b6`.

**Why it is this way** — it is a platform naming accident, and a shipped system setting cannot be renamed. So
`PackageTargetResolver` filters `SysPackage` by `UId`. It reads the value through `GetSysSettingValueByCode`, which
returns that same default. The unit fakes keep the row's Id and UId different, because a fake where they coincide
passes whichever column the filter uses.

**What breaks if you ignore it** — filtering by `Id` matches no row. Every caller that omits the package then fails
with "points at package '…', which could not be resolved to a usable package". Those callers are
`get-target-package`, and `set-logo` and `set-background-image` through `IPackageDataBinder`. `create-theme` does
not use the resolver: with no package it lets the server choose.
