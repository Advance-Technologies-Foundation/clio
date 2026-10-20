---
description: the Generated report Process file (ReportFileProcessingUserTask) generates through ReportEngine.Generate, which dispatches on SysModuleReport Type.Name - MS Word and FastReport have a path, DevExpress and any other type throw NotSupportedException at run time; FastReport's process path is unverified and the builder refuses it
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/McpServer/Prompts/ProcessDesigner/CreateBusinessProcessPrompt.cs
  - clio/Command/ListPrintablesCommand.cs
ticket: ENG-96506
date: 2026-10-19
---

**What is true** — basis: source, NOT measured. `ReportFileProcessingUserTask` calls `ReportEngine.Generate`
(`CrtProcessDesigner/branches/7.8.0/Schemas/ReportFileProcessingUserTask/ReportFileProcessingUserTask.cs:189`), which
reads the printable's `Type.Name` and maps it to `ReportType` by the enum's `[Description]` text
(`Reports/branches/7.8.0/Schemas/ReportEngine/ReportEngine.cs:110-125`). Only `MsWord` and `FastReport` have a
branch (`:305-316`); every other type, DevExpress included, throws `NotSupportedException` ("UnsupportedReportType").
The FastReport branch always produces `.pdf` through `ClassFactory.Get<IReportGenerator>("FastReport")`, and nobody
has run it from a process: the stand used for this work has no FastReport package or printable
(`../infra/the-creatio-sandbox-has-five-word-printables-and-no-fastreport.md`). CrtProcessBuilder accepts MS Word
printables only and refuses FastReport and DevExpress by name - an owner decision, not a platform limit.

**Why it is this way** — the designer's printable list is not filtered by type, so it offers printables whose run
time either throws (DevExpress) or was never exercised in a process (FastReport).

**What breaks if you ignore it** — "the designer offers it, so the builder should take it" builds a DevExpress
element that saves green and fails on its first run, and a FastReport one whose behaviour nobody has seen. Lifting
the FastReport refusal needs a stand with a FastReport printable and a run first, not a code change alone.
`list-printables` lists MS Word printables only (its `Type.Id` filter), which is the set the builder accepts.
