---
description: A process schema's shared resource cache is keyed by its UId, SchemaManager.SaveSchema releases caches by SysSchema.Name only, and a server-side design session refills that cache from its PRE-EDIT snapshot - so without an explicit release after the save, every sub-process caller copies the callee's captions from one save behind, and persists them
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
ticket: ENG-100077
date: 2026-09-23
---

**What is true** — every schema has one app-pool-wide `SchemaResourceManager` in
`Workspace.ResourceStorage`, named `Schema.GetResourceManagerName()` = `UId.ToString("N")`
(`Terrasoft.Core/Schema.cs:586-592`; only `EntitySchema` overrides the name). Three platform facts
combine into a silent lag:

- A design session (`DesignSchema` → `FindDesignItem`) calls `UpdateResourceManager`, which releases
  that cache and REFILLS it from the SessionData snapshot taken when the session opened, i.e. before
  the edit (`SchemaManager.cs:4627-4635, 2712-2720, 4795-4806`).
- `SaveSchema` → `SaveSchemaResourcesInDB` → `ReleaseResourcesManagers` releases
  `GetManager(SysSchema.Name)` (`SchemaManager.cs:2610-2636, 1121-1128`). A process's cache is keyed
  by UId, so it is never released by the save.
- The first metadata build afterwards reads the stale cache and is pinned in `MetaItems`; every
  sub-process element that resolves the callee through `GetInstanceFromMetaData` copies its captions
  from there until the callee is saved again.

The classic designer never lags because it releases the UId-keyed cache itself after its save
(`Terrasoft.Nui.ServiceModel/WebService/BaseProcessSchemaDesigner.cs:181-186`,
`ProcessSchemaDesignerUtilities.ReleaseLocalizableValues`; the block carries
`// TODO Cache management will be added in #CRM-28975`). CrtProcessBuilder does the same in
`ProcessSchemaRepository.Save` / `SaveEdited` from ENG-100077.

**Why it is this way** — `ReleaseResourcesManagers` was written for schemas whose manager is named after
the schema; the process manager name changed to the UId and the release did not follow. The designer
compensates; any other server-side writer has to as well.

**What breaks if you ignore it** — measured on a .NET Framework stand (18 of 20 predictions held; write-up
in `spec/eng-100077-subprocess-caption-sync/`):

- a caption-only change on a callee reaches its callers ONE SAVE LATE, on an explicit resync AND on any
  unrelated save of a caller - one such save reverted a correct caller row to the stale caption;
- a parameter added in that save shows an EMPTY caption on callers built from the stale entry;
- `describe` of the callee can be fresh while the resync right after it is stale, because the two read
  different instances (runtime vs `MetaItems`), so a fresh describe proves nothing about the sync.

The cure for an already-stale entry is any designer save of the callee, or a save by a writer that
releases; a second save through a writer that does NOT release only moves the lag by one save.
