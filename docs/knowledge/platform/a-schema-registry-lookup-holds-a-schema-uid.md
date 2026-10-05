---
description: a Lookup whose reference object is the schema registry (SysSchema, VwSysEntitySchemaInWorkspace, any VwSys*Schema* view) stores the schema UId the manager resolves, never the row Id that is the view's primary column - the views list one row per package extending a schema, each with its OWN Id and UId, and the runtime loads root UIds only
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/McpServer/Tools/ProcessDesigner/ModifyBusinessProcessTool.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/DescribeProcessTool.cs
ticket: ENG-102113
date: 2026-10-05
---

**What is true** - Add data `EntitySchemaId` / `FilterEntitySchemaId`, Modify data `EntitySchemaUId`, Delete
data `EntitySchemaId` and the process-parameter defaults that reference the same objects are typed as Lookups
on the schema registry, but their value is a SCHEMA UId. Measured 2026-10-05:

- every designer-written value of this family in `PackageStore` is a root schema UId (Add data 277/277,
  selection 38/38, Modify data 347/347, Delete data 51/51, process-level defaults 205/205), none a row id;
- the designer's picker (`EntitySchemaSelectMixin`) lists the client schema list by UId, and the data
  elements' runtime reads the value with `EntitySchemaManager.GetInstanceByUId`, which searches ROOT items
  only and throws `ItemNotFoundException` on a miss;
- on a 10.2 stand `VwSysEntitySchemaInWorkspace` and `SysSchema` return 6 rows for Contact - the root
  (`ExtendParent = false`) and 5 package extensions - each with its own `Id` and its own `UId`. The views'
  primary column is `Id`. `VwProcessLib` / `VwSysProcess` are the exception: their `Id` equals the UId.

From CrtProcessBuilder 1.6.6.65 the package resolves such a value as a schema (registry row by `UId` or `Id`,
then the row's manager) and stores the UId the runtime loads, so a row id or an extension UId is normalized.

**Why it is this way** - the platform declares the lookup against the registry so its own lookup editor has a
list to show, but the consumers of these parameters are schema managers, not entity reads. The declared type
and the stored value disagree, and nothing in the schema says so.

**What breaks if you ignore it** - a validator, test or agent that treats the value as "the reference
object's record id" passes the row `Id`: it is accepted by an Id check, stored, fails at run time with
`ItemNotFoundException`, and the designer opens the element BLANK - opening it already creates a designer
draft, and saving that draft erases the element's configuration. Checking "any UId in the view" is wrong too:
an extension's UId is in the view and still fails at run time. This is what clio#1368 and clio#1300 were.
