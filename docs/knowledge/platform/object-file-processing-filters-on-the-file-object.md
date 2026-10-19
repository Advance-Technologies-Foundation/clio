---
description: the Object attachments element (ObjectFileProcessingUserTask) filters on the FILE object SourceEntitySchemaUId (AccountFile, SysFile), not on the record object - the stored rootSchemaName is ignored, Type = File is always added, a filter written against the record reads zero files with success, and an empty filter reads RecordsToRead arbitrary files
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/McpServer/Tools/ProcessDesigner/ModifyBusinessProcessTool.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs
ticket: ENG-96505
date: 2026-10-15
---

**What is true** — read in source, not measured: `ObjectFileProcessingUserTask.cs:36-47`, `:96-103`
(CrtProcessDesigner 7.8.0); `SpecifyESQFilters` returning on an empty string (`ProcessUserTaskUtilities.CrtProcessDesigner.cs:431-433`,
generated, core 10.1.37); `Terrasoft.Nui.ServiceModel/Extensions/QueryExtension.cs:640-642`, `:957-962`; a shipped test
process holds an empty-filter "save" element (`CRM60006PP`, corpus). The element's read is an ESQ rooted on
`SourceEntitySchemaUId`, the FILE object such as `AccountFile`, with `RowCount = RecordsToRead`; `DataSourceFilters`
resolves its columns on that root, so the `rootSchemaName` stored in the filter JSON plays no part. The runtime then
always adds `Type = File` and, when `SourceDataEntitySchemaUId` is set, `RecordSchemaName = <that object>`. So a filter
phrased against the record, `{object: "Account", Id = X}`, runs as `AccountFile.Id = X` and reads zero files with
success (a shared column name such as `Id`, `Name` or `CreatedOn` silently applies to the file's column); an empty
filter reads up to `RecordsToRead` (template 50) arbitrary files of the whole table; and `RecordsToRead` 0 or unset is
`TOP 0`: zero files, success.

**Why it is this way** — the designer's filter editor is itself rooted on the file object, so a designer-built
filter never names the record; the runtime trusts that and does not re-root.

**What breaks if you ignore it** — a writer that roots the filter on whatever object the caller names saves green
and reads nothing, or, with no filter, copies 50 unrelated files onto a record; the package's generic
`DataNodeFilterTarget` did exactly that before ENG-96505. The ENG-96505 cut refuses a `filter.object` other than the
runtime root, moves the record scope into `attachments.recordId`, and refuses an Object + save element with no
selecting filter at the end of the request.
