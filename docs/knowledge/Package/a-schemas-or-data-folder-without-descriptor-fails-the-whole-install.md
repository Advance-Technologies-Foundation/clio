---
description: Invalid descriptor - one Schemas/<Name>/ or Data/<Name>/ folder without descriptor.json fails the whole archive install, and the platform message names only the folder's last path segment
applies-to:
  - clio/Package/PackageItemDescriptorCheck.cs
  - clio/Package/PackageArchiver.cs
ticket: ENG-102505
date: 2026-10-07
---

**What is true** — When Creatio installs a package archive (`PackageZipOperations.Load`), `PackageFileStorage.CreateDescriptor` reads `descriptor.json` from every directory under `Schemas/`, `Data/`, `Assemblies/` and `SqlScripts/`, and `ThrowIfPackageStorageItemErrorsExist` fails the whole installation if one is missing. The error text is `Invalid descriptor:\n<inner message>\nPath: <path>`, but `ResponseUtils.GetMessageWithSecuredPath` runs it through `SecurePathUtilities.RemoveLocalPath`, which keeps only the last segment of a Windows path. Measured on Creatio 10.2.414.0 (.NET Framework) for GH-1749: clio printed `Path: UsrI1749Binding`, with no package and no `Data/` prefix.

**Why it is this way** — Platform code, read in creatio-core (`Terrasoft.Core/Packages/PackageFileStorage.cs`, `Terrasoft.Core.ServiceModelContract/ResponseUtils.cs`). An archive holds files only, so an empty directory, or one whose files clioignore drops, never reaches the platform. That is why clio's check reads the final file list, not the package folder on disk.

**What breaks if you ignore it** — A check that walks the folder on disk refuses pushes the platform would accept. The ps repository has empty `Schemas/<Name>/` leftovers, and a workspace clioignore can exclude `Localization/` files. Widening the check to `Assemblies/` or `SqlScripts/` was left out on purpose: nobody has yet shown that every real layout keeps a descriptor there. Prove that first, or the check refuses packages that install today.
