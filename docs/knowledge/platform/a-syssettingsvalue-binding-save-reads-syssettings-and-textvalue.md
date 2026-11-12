---
description: saving a SysSettingsValue package data binding throws 'Value "TextValue" was not found.' (or "SysSettingsId") unless both columns are in the binding, because the platform's SecureText re-encryption probes them with a throwing lookup
applies-to:
  - clio/Command/DataBindingDbCommand.cs
ticket: ENG-106250
date: 2026-11-12
---

**What is true** — when `SchemaDataDesignerService.SaveSchema` saves a binding of `SysSettingsValue`, the platform
loads the bound rows with only the binding's columns and, while `ReEncryptSecureSettingsValuesInPackageData` is on
(its default), runs `PackageSchemaDataUtilities.GetIsSecureSettings` on each row
(`core/core/TSBpm/Src/Lib/Terrasoft.Core/Packages/PackageSchemaDataUtilities.cs`, called from
`PackageElementUtilities.LoadDataFromEntities`). That method guards with `entity.GetIsColumnValueLoaded("SysSettingsId")`
and `GetIsColumnValueLoaded("TextValue")`, and the string overload resolves the value with `ColumnValues.GetByName`,
which throws `ItemNotFoundException` ("Value \"{0}\" was not found.") for a column the query never selected,
instead of answering false. clio therefore always projects `SysSettings` and `TextValue` into a `SysSettingsValue`
binding (`PlatformRequiredBindingColumns`). Install is not affected: deserialization builds full entities.

**Why it is this way** — the guard was written as "skip when not loaded", but the overload it calls cannot see an
absent column. Fixing it is a platform change; clio has to work on the stands that exist today.

**What breaks if you ignore it** — DB-first projection keeps only the columns the rows reference, so a value row
for a Float, Integer or Lookup setting has no `TextValue`, and `create-data-binding-db` / `upsert-data-binding-row-db`
fail with `SaveSchema failed: Value "TextValue" was not found.` (clio#1785, clio#1901). Every shipped system setting
needs this binding.
