---
description: a user-task element built on the server (SchemaUId assignment, parameter sync) takes a template parameter's DefValueForExistingProcess (GS8) instead of its value - the Process file ConsiderTimeInFilter becomes "false" where the designer writes "true"
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs
ticket: ENG-96505
date: 2026-10-15
---

**What is true** — measured with M14 on 2026-10-02 (CrtProcessBuilder 1.6.6.54, a disposable process built through
the generic `userTask` route and deleted the same day); mechanism read in `Terrasoft.Core/Process/ProcessSchemaUserTask.cs:106-115`
and `ProcessSchemaActivity.cs:308-322` (core 10.1.37). Assigning `SchemaUId` on a `ProcessSchemaUserTask` runs the
parameter sync, which creates each element parameter with `CreateElementParameterFromUserTaskSchemaParameter`, and
when the template value carries `DefValueForExistingProcess` (`L8.GS8`) that value replaces `GS2`. The Process file
`ConsiderTimeInFilter` (template Script `"true"`, `GS8 "false"`) is therefore stored as Script `"false"` on a
server-built element, while a designer-built one keeps `"true"` (18 of 18 shipped elements). Other user-task
templates with a `GS8` value were not surveyed.

**Why it is this way** — by its name, `GS8` is the value for a parameter added to an element of an EXISTING process;
the sync uses the same factory for a brand-new element and cannot tell the two apart.

**What breaks if you ignore it** — `ConsiderTimeInFilter` is not read at run time, but the designer's filter editor
applies it when the filter is next edited and saved (`trimDateTimeParameterToDate`, `FilterModuleMixin.js:839-842`,
CrtProcessDesigner 7.8.0), so date-time comparisons on a server-built element are silently trimmed to the date. The
ENG-96505 package cut copies the template value verbatim on create instead.
