---
description: ResultActionType of the Process file user tasks has NO template default - an unset value reads as 0 = SaveToFiles, while the designer card defaults Object/Report to 1 (use in process), so a server-built or generic element with no action COPIES files
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/McpServer/Tools/ProcessDesigner/DescribeProcessTool.cs
  - clio/Command/McpServer/Tools/ListUserTasksTool.cs
ticket: ENG-96505
date: 2026-10-15
---

**What is true** — the run-time reading of an unset value is read in source, not measured (`ResultActionTypeEnum`,
`FileProcessing.cs:19-30`, `ObjectFileProcessingUserTask.cs:97`, `:104-110`, the generated
`ObjectFileProcessingUserTaskSchema.CrtProcessDesigner.cs:94-97`, CrtProcessDesigner 7.8.0; `GetIntValue` returning 0,
`Terrasoft.Core/Process/ProcessInstanceParametersDataReader.cs:763-765`, core 10.1.37); the designer default was seen
on 2026-10-01 (UO-3) and the unset stored action measured on 2026-10-02 (M14, CrtProcessBuilder 1.6.6.54).
`ResultActionType` carries `L8: {}` in all three Process file templates and the property is a plain `int`, so an
element with no action runs as `SaveToFiles` (0): the Object variant copies the matching attachments onto the
target, the Report variant saves its documents to the target record. `UseInProcess` is 1; any other number throws
`NotSupportedException`, and the Process parameter variant throws for 1 as well. Only the designer card holds a
default (`BaseFileProcessingUserTaskPropertiesPage.js:11-12`, `:894-903`): `"1"` for Object and Report, and a forced
hidden `"0"` for the Process variant.

**Why it is this way** — the default is a UI convention of the properties page, not schema metadata, so anything
that builds the element without the page inherits the runtime's zero.

**What breaks if you ignore it** — reading "no action" as the designer's "use in process" is backwards: such an
element saves, copying up to `RecordsToRead` files or writing documents onto a record, with no error. The ENG-96505
package cut therefore always writes the action on create, describes an unset one as a save with
`actionStored: false`, and counts an unset action as saving in its empty-filter and switch guards.
