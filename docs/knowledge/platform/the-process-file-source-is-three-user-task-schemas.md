---
description: the Process file element is three user-task schemas (ObjectFileProcessingUserTask, ReportFileProcessingUserTask, ProcessFileProcessingUserTask) - "What is the source of the file?" is not a parameter but a schema choice, and a source change REPLACES the element (same UId, new name, no value survives)
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/ProcessModel/Schema.cs
  - spec/eng-92719-file-processing-element/
ticket: ENG-96505
date: 2026-10-15
---

**What is true** — the replacement is read in source (`ProcessUserTaskConstants.js:176-180`,
`BaseFileProcessingUserTaskPropertiesPage.js:242-253`, `:495-497`, CrtProcessDesigner 7.8.0;
`process-schema-designer-view-model-new.js:270-275`, `:565-589`, `:1468-1487`, core 10.1.37, `Terrasoft.Nui`); the
stand's served `onItemTypeUpdated` was seen on 2026-10-01 to hold the same steps, but a source switch on a configured
element was not performed. "Process file" is three user-task schemas, all captioned "Process file":
`ObjectFileProcessingUserTask` `9387c794…` (Object attachments), `ReportFileProcessingUserTask` `c2bf0416…`
(Generated report), `ProcessFileProcessingUserTask` `6c620dd2…` (Process parameter). No schema declares a source
parameter: the "What is the source of the file?" options are client constants (`ReadFiles "0"`, `GenerateReports "1"`,
`UseProcessParameter "2"`) that `_getUserTaskName` maps to a schema name. Changing the source makes the designer
create a NEW instance of the other schema: same UId, position, size, container and caption; a new name
`<SchemaName><N>`; no value of the old element survives.

**Why it is this way** — the designer models the choice as an element type swap, not a value.

**What breaks if you ignore it** — a writer that changes `SchemaUId` in place keeps parameters that belong to
another schema and leaves every reference to the old element's outputs dangling, silently. The ENG-96505
CrtProcessBuilder cut refuses a source change (another group on update) for that reason, and a "source" key in a
build contract is an identity check, never a value written to the element.
