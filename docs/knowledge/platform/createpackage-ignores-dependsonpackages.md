---
description: PackageService CreatePackage accepts a dependsOnPackages list and answers success, but stores no dependencies; they need a second SavePackageProperties request
applies-to:
  - clio/Package/RemotePackageCreator.cs
ticket: ENG-101352
date: 2026-09-30
---

**What is true** — `ServiceModel/PackageService.svc/CreatePackage` takes a full `WorkspacePackageDto`,
including `dependsOnPackages`, but `PackageDBRepository.Create` (creatio-core) stores only
`Id`, `UId`, `Name`, `Position`, `IsChanged`, `IsLocked`, `Description`, `Type` and `InstallBehavior`.
It never reads the dependency list, and it takes `Maintainer` from the server's `Maintainer` system
setting, not from the request. Measured on a .NET Framework stand on 2026-09-30: a create request with
`dependsOnPackages: [Custom]` answered `success: true`, and `GetPackageProperties` then returned
`dependsOnPackages: []`. The same holds for `ApplicationPackagesService.svc/CreatePackageInApp`, which
calls the same repository method.

**Why it is this way** — the Configuration section creates a package without dependencies and adds
them afterwards on the package page through `SavePackageProperties`; the create path was never meant
to carry them.

**What breaks if you ignore it** — sending the dependencies with the create request and trusting the
`success: true` answer produces a package with no dependencies and no error anywhere. `create-package`
therefore saves them with `IPackageDependencyManager.AddDependencies` after creation, and reports a
failure of that second request as "package created, dependencies not applied" rather than as a refusal,
because at that point the package already exists.
