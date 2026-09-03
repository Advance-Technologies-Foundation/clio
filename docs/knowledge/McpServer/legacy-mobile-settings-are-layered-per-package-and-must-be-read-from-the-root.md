---
description: The effective legacy Mobile-wizard settings are the ordered application of EVERY package layer (CrtBase → product → Custom); the designer hierarchy must be re-queried from the ROOT schema or upper replacing layers are silently missing
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/Legacy/LegacyMobileSettingsReader.cs
  - clio/Command/PageSchemaMetadataHelper.cs
ticket: ENG-95730
date: 2026-09-02
---

**What is true** — `MobileCaseGridPageSettingsDefaultWorkplace` (and every other wizard settings schema) can exist
in several packages as replacing schemas. Each layer is its own diff array (insert / merge / remove by `name`), and
only the ROOT → HEAD application of all of them is what the classic designer and the mobile runtime show.
`ClientUnitSchemaDesignerService.GetParentSchemas` returns the full chain only when asked from the root schema UId;
asked from a lower layer it omits the upper ones (the same trap `PageGetCommand.ResolveHierarchy` handles).

**Why it is this way** — Package-hierarchy replacement is resolved by the designer service per request; the SysSchema
row a name resolves to is whichever package the DataService returns first, not the head.

**What breaks if you ignore it** — A converted mobile list page misses columns a product or Custom package added, or
shows columns a later layer removed — with no error. `LegacyMobileSettingsReader` re-queries from the root, applies
ROOT → HEAD with `IJsonDiffApplier`, and cross-checks the layer set against `QuerySysSchemaRowsByName`; a package that
stores the schema but is absent from the hierarchy is reported as a guide constraint, never silently dropped.
