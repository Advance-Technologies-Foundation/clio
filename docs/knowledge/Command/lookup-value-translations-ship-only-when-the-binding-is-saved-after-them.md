---
description: create-data-binding-db writes row translations (UpdateLocalizationQuery) BEFORE SaveSchema of the binding, because the package's per-culture snapshot (SysPackageDataLcz) is taken at save time; the runtime schema has no localizable flag, so the check uses the designer's isLocalizableText
applies-to:
  - clio/Command/DataBindingDbLocalizer.cs
  - clio/Command/DataBindingDbCommand.cs
ticket: ENG-105291
date: 2026-11-07
---

**What is true** — measured on Creatio 10.1.834 (stand `sae_m_seeenu_16284236_1119`, lookup
`UsrRepairStatus` in package `UsrEng105291`, cultures en-US + ru-RU). A plain InsertQuery writes a localizable
column in the connected user's culture only. `UpdateLocalizationQuery` on the lookup wrote the ru-RU `Name` and
`Description`, and the following `SchemaDataDesignerService.svc/SaveSchema` of the binding filled the ru-RU
`SysPackageDataLcz` row of that binding with them; every other culture's row held empty strings. The
`RuntimeEntitySchemaRequest` column contract (`Terrasoft.Nui.ServiceModel.DataContract.EntitySchemaColumn`) has no
localizable flag at all, so whether a column takes translations is read from the entity designer
(`GetSchemaDesignItem`, `useFullHierarchy: true`, `isLocalizableText`), which also covers the `Name` and
`Description` inherited from `BaseLookup`. Server side, `FillLocalizableColumnValues` throws
`NotSupportedException(<data type name>)` for a non-localizable column. The server merges the culture map per culture: a later write of
he-IL alone kept the ru-RU value of the same record, so a re-run only has to carry the cultures it changes.

**Why it is this way** — package data is a snapshot that `SaveSchema` takes from the database; a localization
written after the save stays in the record's localization table and never reaches the package
(see [`platform/direct-sysmodule-localization-write-leaves-package-data-stale.md`](../platform/direct-sysmodule-localization-write-leaves-package-data-stale.md)
for the same mechanism on `SysModule`).

**What breaks if you ignore it** — move the localization write after `SaveBinding`, or into a separate call
that does not re-save the binding, and the translations show on the stand but an export or an install on
another site ships empty values, with every call reporting success. Checking the flag on the runtime schema
instead would accept every column, and the server would reject the write only after the rows were inserted.
