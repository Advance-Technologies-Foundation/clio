---
description: after an Object attachments Process file element SAVES (Save to object attachments, ResultActionType 0), its output ObjectFiles holds the SOURCE file locators it read, not the copies - the copies are only in CreatedObjectFileIds (item Id, a Guid); a consumer that maps ObjectFiles to work on the saved files works on the originals
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/ProcessModel/IProcessDescriber.cs
ticket: ENG-96505
date: 2026-10-20
---

**What is true** — measured on 2026-10-15 on the .NET Framework dev stand with CrtProcessBuilder 1.6.6.100 (M26),
in the ST-01 run `8c951966…`: a builder-built Object attachments element copied two AccountFile attachments onto a
Contact, and its element trace (`SysPrcElementTraceLog`) showed `ObjectFiles` holding locators with
`EntitySchemaName` `AccountFile` and the Ids of the two SOURCE rows, while `CreatedObjectFileIds` held the Ids of the
two new ContactFile rows read back from the table. The mechanism is source, not measured:
`ObjectFileProcessingUserTask.CopyFiles` sets `ObjectFiles` from `FileCopyResults.SourceFileLocators` and
`CreatedObjectFileIds` from `TargetFileLocators` (`ObjectFileProcessingUserTask.cs:77-87`), and `SourceFileLocators`
are the locators the element's query read before copying (`FileProcessing.cs:152-179`, CrtProcessDesigner 7.8.0).
With "use the files in the process" (`ReadFiles`, `:71-75`) `ObjectFiles` holds the same read locators and
`CreatedObjectFileIds` is empty. Not measured: the planned designer-built probe whose multi-instance callee would
record each iterated locator, so what a downstream consumer receives is read from the trace and the source, not
observed. The Process parameter variant is the opposite case: its `ObjectFiles` (item `ObjectFile`) holds the copies
it saved (`ProcessFileProcessingUserTask.cs:67-81`, source).

**Why it is this way** — in source, `ObjectFiles` is the element's "files it read" output in both modes (both
`ReadFiles` and `CopyFiles` fill it through `SetReadObjectFiles`); the copy step's targets reach the process only as
the Ids in `CreatedObjectFileIds`. No design note explains the choice.

**What breaks if you ignore it** — an element meant to act on the saved copies (send, tag, rename or delete them)
that maps `ObjectFiles` acts on the originals on the source record, with no error. Reach the copies through
`CreatedObjectFileIds` (a multi-instance sub-process whose callee takes a Guid) or re-select them with Read data.
The designer refuses changing the element's configuration while `CreatedObjectFileIds` is mapped
(`getCanChangeElementConfig`, `ObjectFileProcessingUserTaskPropertiesPage.js:294-303`, source), and CrtProcessBuilder
refuses switching a saving element to `useInProcess`, or moving its source or target object, while another element
reads it (`FileProcessingConfigApplier`, tag `crtprocessbuilder-1.6.6.109`). The
shipped `process-files` guide states this measurement with its date and version (clio-knowledge #295); if the
platform changes it, both move together.
