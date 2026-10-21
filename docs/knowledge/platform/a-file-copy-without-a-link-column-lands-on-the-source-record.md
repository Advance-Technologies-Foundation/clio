---
description: a Process file copy (IFile.Copy / File.CopyAttributes) inherits every source attribute the target has not set - a same-schema copy (ContactFile to ContactFile) whose ConnectedObjectColumnUId is missing or wrong lands as a duplicate on the SOURCE record, a cross-schema one is linked to no record, both silently
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/ModifyBusinessProcessTool.cs
ticket: ENG-96505
date: 2026-10-15
---

**What is true** — read in source, not measured (M23 on 2026-10-20 saved with the link column set, so it did not run this path):
`Terrasoft.File/File.cs:174-180` (`CopyAttributes`), `:182-187`, `:254-262`, `Terrasoft.File/Metadata/EntityFileMetadataStorage.cs:29-39`,
`:115-125`, `:159-164` (core 10.1.37); `FileProcessing.cs:152-179`, `:193-201` (CrtProcessDesigner 7.8.0). The
Object attachments copy sets the target's link column only when `ConnectedObjectColumnUId` resolves on the target
schema (a miss is tolerated), then calls `sourceFile.Copy(targetFile)`, and `File.CopyAttributes` copies EVERY
source attribute the target has not set - all loaded columns but `Id`, `Name`, `Size`, `TotalSize`, `CreatedOn`,
`ModifiedOn`, `TypeId`, `SysFileStorageId` and `Data`; one the target has no column for is dropped with a WARN. So a
same-schema copy (ContactFile -> ContactFile) with no or a wrong link column inherits the source's `ContactId` and
lands on the SOURCE record; a cross-schema copy with no link column is linked to no record; and even a correctly
linked copy inherits `Notes`, `Tag`, `FileGroup` and `LockedBy` / `LockedOn`.

**Why it is this way** — `Copy` is the generic File API copy, which clones a file's metadata; the element relies
on having set the link first, and nothing checks that it did.

**What breaks if you ignore it** — "no link column means unlinked" is false for a same-schema copy: the element
completes and the file appears twice on the record it was read from. That is why the ENG-96505 package cut has no
`linkColumn: "none"`, refuses a target with no column referencing the record (pass `saveTo.linkColumn`, R8), and
requires an explicit link column for an orphan attachment object such as `DNSGuideFile`.
