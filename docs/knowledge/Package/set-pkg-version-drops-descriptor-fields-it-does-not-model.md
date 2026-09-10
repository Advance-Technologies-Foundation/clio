---
description: clio set-pkg-version rewrites descriptor.json through PackageDescriptor and drops every field the model lacks, including InstallScripts
applies-to:
  - clio/Command/PackageCommand/SetPackageVersionCommand.cs
  - clio/Package/PackageDescriptor.cs
  - rebundle-bundled-package.ps1
ticket: ENG-95794
date: 2026-09-10
---

**What is true** — `set-pkg-version` deserialises `descriptor.json` into `PackageDescriptor`, sets two
fields and serialises it back. `PackageDescriptor` models UId, PackageVersion, Name, Type, ProjectPath,
ModifiedOnUtc, Maintainer, InstallBehavior and DependsOn — nothing else. Any other block a package
carries is silently removed. `CrtDashboardsMigratorApp` carries `InstallScripts.AfterInstall` (the
schema that grants column rights on its log section), and the command deleted it.

**Why it is this way** — the model was written for the fields clio itself needs; nobody had run the
command on a descriptor with install scripts before the migrator was bundled.

**What breaks if you ignore it** — the package installs, `SysPackage.Version` moves, Ping answers,
every pin is green, and the AfterInstall script never runs on the target, so the log section is
installed without its column rights. `rebundle-bundled-package.ps1` therefore stamps the migrator's
descriptor by text edit (`StampByHand`), and `BundledDashboardsMigratorPackageTests` asserts the block
is still in the archive. The fix that removes the workaround is a round-trip-safe descriptor writer
(keep unknown JSON members); until then do not switch the migrator back to `set-pkg-version`.
