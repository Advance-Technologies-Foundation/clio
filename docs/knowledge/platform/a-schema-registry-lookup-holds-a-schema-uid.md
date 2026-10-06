---
description: a data element's object (Add data EntitySchemaId / FilterEntitySchemaId, Modify data EntitySchemaUId, Delete data EntitySchemaId) is a Lookup on the schema registry that holds the root entity schema UId, never the row Id that is the view's primary column - and the schema manager resolves the root UId only, so a row Id or an extension UId needs SQL
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/McpServer/Tools/ProcessDesigner/ModifyBusinessProcessTool.cs
ticket: ENG-102113
date: 2026-10-05
---

**What is true** - Add data `EntitySchemaId` / `FilterEntitySchemaId`, Modify data `EntitySchemaUId` and Delete data
`EntitySchemaId` are Lookups on the schema registry (`SysSchema`, `VwSysEntitySchemaInWorkspace`) whose value is a
ROOT entity schema UId. Measured 2026-10-05: all 918 such values in `PackageStore` (these four plus process-parameter
defaults on the same views) are root UIds; on a 10.2 stand a stored row Id failed at run time with
`ItemNotFoundException` and opened blank in the designer; `VwSysEntitySchemaInWorkspace` and `SysSchema` list 6 rows
for Contact (the root, `ExtendParent = false`, and 5 package extensions), each with its own `Id` and `UId`; the
views' primary column is `Id`; `VwSysProcess` sits under the same `VwSysSchemaInWorkspace` root but lists processes.

Read in the source, not measured: the designer's picker (`EntitySchemaSelectMixin`) stores UIds; the elements read
the value with `EntitySchemaManager.GetInstanceByUId`; the manager keeps one item per schema under the root UId,
carrying the LAST extension's `Id` and `RealUId` (`SchemaManager.InitializeItemCollection`), so no public manager call
resolves a row Id - the platform's own fallback (`StartOpportunityManagementProcessUserTask`) is
`Select UId From SysSchema Where Id`. From CrtProcessBuilder 1.6.6.67 the package checks a value on `SysSchema` and
the entity views through `EntitySchemaManager.FindItemByUId` first and falls back to the record check, storing the
value as given; a process-parameter default may therefore still hold a row Id. On a data element's object, addMapping
accepts only the object the element already holds; setting or changing it is the element's block.

**Why it is this way** - the platform declares the lookup against the registry so its lookup editor has a list
to show, but the consumers are schema managers, not entity reads. The declared type and the stored value
disagree, and nothing in the schema says so.

**What breaks if you ignore it** - treat a data element's object as "the reference object's record id" and a row
`Id` is accepted by an Id check, stored, and fails at run time; "any UId in the view" is wrong too, because an
extension's UId fails the same way; and a rule keyed on the registry roots would accept an entity UId on
`VwSysProcess`. That is clio#1368 and #1300.
