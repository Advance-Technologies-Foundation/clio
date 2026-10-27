---
description: a Lookup sys-setting's SysSettings.ReferenceSchemaUId must hold the root (base, ExtendParent = false) SysSchema UId of the entity; an extension UId is accepted on create, then every value write is refused with a generic error, and settings created that way stay broken
applies-to:
  - clio/Common/ISysSettingsManager.cs
  - clio/CreatioModel/SysSchema.cs
ticket: ENG-104666
date: 2026-10-27
---

**What is true** - `SysSchema` keeps one row per package that carries an entity schema, each with its own `UId`: the
root (base) row has `ExtendParent = false`, every package extension `true`. On a 10.1.37 stand `MailboxSyncSettings`
has 4 rows (root in CrtCoreBase, extensions in Exchange, CrtCaseService, ExchangeListener) and `Contact` has 15.

Measured 2026-10-27 on that stand: a Lookup setting on `Contact` created by clio 8.1.0.144 got the Completeness
extension's `UId`; `create-sys-setting` reported success, and `update-sys-setting` with an existing Contact Id was
refused with the generic "the value did not match the expected type". The same create plus write with the root `UId`
(the fixed clio, e2e `CreateSysSetting_Lookup_On_Replaced_Schema_Should_Accept_Value_Write`) is accepted. A
name-only `SysSchema` select returns the rows in no stable order: for `MailboxSyncSettings` the issue's stand got
the CrtCaseService row and ours got ExchangeListener.

From the issue report (clio#1864), not measured here: the System settings UI stores the root `UId`, a setting with an
extension `UId` shows "Item with unique identifier ... not found", and re-selecting the object in the UI repairs it.
Correcting `ReferenceSchemaUId` with SQL alone did not make our write succeed; the platform likely caches the setting
definition, which we did not confirm.

**Why it is this way** - read in the source, not measured for sys-settings: the schema manager keeps one item per
schema under the root UId, so an extension UId names no item and the setting's lookup cannot be resolved. Nothing
refuses the extension UId at insert time.

**What breaks if you ignore it** - resolve a schema name with `Where(Name == x).FirstOrDefault()` and the result
depends on row order: whenever an extension row comes first (both stands we saw), the setting is created
"successfully" and then cannot hold a value. Settings created that way by older clio versions are not repaired by
the fix: recreate them, or re-select the object in System settings. The `ManagerName = 'EntitySchemaManager'` filter
is defensive, matching `ClassicEntitySchemaQuery` and `FindEntitySchemaCommand`; a same-name non-entity schema was
not observed. Related: [a-schema-registry-lookup-holds-a-schema-uid.md](a-schema-registry-lookup-holds-a-schema-uid.md)
(process data elements, same root-UId rule).
