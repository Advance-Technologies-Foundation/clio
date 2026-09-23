---
description: A process schema's shared resource cache is keyed by its UId, SchemaManager.SaveSchema releases caches by SysSchema.Name only, and a server-side design session refills the UId-keyed cache from its PRE-EDIT snapshot - so a writer that does not release it after the save leaves every sub-process caller copying the callee's captions one save behind, and persisting them
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
ticket: ENG-100077
date: 2026-09-23
---

**What is true** — each schema has one app-pool-wide resource manager in `Workspace.ResourceStorage`, named
`GetResourceManagerName()` = `UId.ToString("N")` (`Terrasoft.Core/Schema.cs:586-592`). A design session
(`DesignSchema` → `FindDesignItem` → `UpdateResourceManager`) refills it from the snapshot taken when the
session opened, i.e. before the edit (`SchemaManager.cs:4627-4635, 2712-2720, 4795-4806`). `SaveSchema` →
`ReleaseResourcesManagers` releases `GetManager(SysSchema.Name)` only (`SchemaManager.cs:2610-2636, 1121-1128`),
so the UId-keyed cache survives the save. The next metadata build reads it and is kept in `MetaItems`, and every
caller that resolves the callee through `GetInstanceFromMetaData` copies those captions until the callee is
saved again. CrtProcessBuilder releases it in `ProcessSchemaRepository.Save` / `SaveEdited` from 1.6.6.17, as
the classic designer does after its own save (`ProcessSchemaDesignerUtilities.ReleaseLocalizableValues`,
`Terrasoft.Nui.ServiceModel/WebService/BaseProcessSchemaDesigner.cs:181-186`).

**Why it is this way** — the release goes by `SysSchema.Name`, and a manager is named after the schema only for
a RUNTIME entity schema (`EntitySchema.GetResourceManagerName`, `Entities/EntitySchema.cs:2621-2623`); every
other schema, a process included, is keyed by UId. The designer compensates by releasing the UId-keyed manager
itself (its block carries `TODO Cache management will be added in #CRM-28975`), so any other server-side writer
has to as well.

**What breaks if you ignore it** — measured on a .NET Framework stand (`spec/eng-100077-subprocess-caption-sync/`):
a caption changed on a callee reaches its callers one save late, on an explicit resync and on any unrelated save
of a caller (one such save reverted a correct caller row); a parameter added in that save shows an EMPTY caption
on callers; and `describe` of the callee can read fresh while the resync right after it is stale, because the two
read different instances. The measured cure for an entry that is already stale is a designer save of the callee.
