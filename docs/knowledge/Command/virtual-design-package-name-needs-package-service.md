---
description: GetDesignPackageUId returns name null and SysPackage has no virtual row; GetPackageProperties resolves the cached virtual name with a bare JSON Guid request
applies-to:
  - clio/Command/PageSchemaMetadataHelper.cs
ticket: "#1463"
date: 2026-09-11
---

**What is true** — `ApplicationPackagesService/GetDesignPackageUId` uses an
`AppPackageResponse` DTO with a `name` field but does not populate that field.
`PackageService/GetPackageProperties` resolves a virtual package's name before
its first save. Its request is a bare JSON string containing the UId, not an
object with a `packageUId` property. Verified on Creatio 10.1.585.0, .NET 8,
PostgreSQL, and traced through `PackageRepository.GetByUId` in platform source.

**Why it is this way** — the package repository checks its virtual-package
cache before consulting the database. A virtual package has no `SysPackage` row.

**What breaks if you ignore it** — querying only the database or copying the
unused `name` field leaves the actual destination unnamed. The remaining
current-leaf package name can then be mistaken for a writable destination.
