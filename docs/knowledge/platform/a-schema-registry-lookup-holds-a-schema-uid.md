---
description: a Lookup whose reference object is the schema registry (SysSchema, VwSysEntitySchemaInWorkspace, any VwSys*Schema* view) holds the root schema UId the runtime loads, never the row Id that is the view's primary column - and the schema manager can resolve the root UId only, so a row Id or an extension UId needs SQL
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/McpServer/Tools/ProcessDesigner/ModifyBusinessProcessTool.cs
ticket: ENG-102113
date: 2026-10-05
---

**What is true** - Add data `EntitySchemaId` / `FilterEntitySchemaId`, Modify data `EntitySchemaUId`, Delete
data `EntitySchemaId` and process-parameter defaults on the same objects are Lookups on the schema registry whose
value is a ROOT schema UId. Measured 2026-10-05: all 918 such values in `PackageStore` are root UIds; the
designer's picker (`EntitySchemaSelectMixin`) stores UIds; the elements' runtime reads them with
`EntitySchemaManager.GetInstanceByUId`, which throws `ItemNotFoundException` for anything else; on a 10.2 stand
`VwSysEntitySchemaInWorkspace` and `SysSchema` list 6 rows for Contact (the root, `ExtendParent = false`, and 5
package extensions), each with its own `Id` and `UId`, and the views' primary column is `Id`.

Read in the TSBpm source, not measured: the manager keeps one item per schema under the root UId, and that item
carries the LAST extension's `Id` and `RealUId` (`SchemaManager.InitializeItemCollection`). No public manager
call resolves a row Id, and the platform's own fallback (`StartOpportunityManagementProcessUserTask`) is
`Select UId From SysSchema Where Id`. From CrtProcessBuilder 1.6.6.66 the package checks such a value through
`EntitySchemaManager.FindItemByUId` first, falls back to the ordinary record check, and stores the value as
given. On a data element's object, addMapping accepts only the object the element already holds; setting or
changing it is `setElement.<block>`.

**Why it is this way** - the platform declares the lookup against the registry so its lookup editor has a list
to show, but the consumers are schema managers, not entity reads. The declared type and the stored value
disagree, and nothing in the schema says so.

**What breaks if you ignore it** - treat the value as "the reference object's record id" and a row `Id` is
accepted by an Id check, stored, fails at run time with `ItemNotFoundException`, and opens BLANK in the designer;
"any UId in the view" is wrong too, because an extension's UId fails the same way. That is clio#1368 and #1300.
