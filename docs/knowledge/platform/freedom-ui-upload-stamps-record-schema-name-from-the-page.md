---
description: a Freedom UI attachment upload into SysFile stores RecordSchemaName only when the client sends AdditionalParams.RecordSchemaName; the Process file Object element reads SysFile by RecordSchemaName + Type File, so a file uploaded without it is never found - a create-app form page sends it even though the page body names no recordEntitySchemaName
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
ticket: ENG-103937
date: 2026-10-20
---

**What is true** - measured on a stand on 2026-10-20 (M8): on a `create-app` form page whose `crt.FileList` is
`SysFile` / `RecordId` with NO `recordEntitySchemaName` in the page body, the upload POST still carried
`AdditionalParams={"RecordSchemaName":"UsrFpDoc"}` and the row got `RecordSchemaName = 'UsrFpDoc'`, `TypeId` = File,
`Version` 1; an Object element on that object then copied it (M23b, ST-08 c). The server takes `RecordSchemaName`
from `AdditionalParams` only (`FileUploader`, source), and the element's read adds `Type = File` and
`RecordSchemaName = <record object>` to its filter (`ObjectFileProcessingUserTask.FillFileDataFilters`, source). That
the list derives the name from the page's main data source is an inference.

**Why it is this way** - SysFile is shared by every object without its own attachment object, so the record schema
name is the only thing that says whose file a row is.

**What breaks if you ignore it** - a component whose storage leaves the record schema empty (a RichTextEditor with
`recordEntitySchemaName: ""`, say) uploads files no Process file element reads: the element completes and copies
nothing. Check the stored `RecordSchemaName` before blaming the element.
