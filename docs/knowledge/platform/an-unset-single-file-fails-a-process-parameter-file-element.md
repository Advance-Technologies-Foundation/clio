---
description: a Process parameter Process file bound nested-only to a single File (Files.File) that is unset at run time goes to Error with a NullReferenceException in FileFactory.CreateFileInstance under ProcessFileProcessingUserTask.SaveEntityFiles and copies nothing - it does not succeed silently
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/McpServer/Prompts/ProcessDesigner/CreateBusinessProcessPrompt.cs
ticket: ENG-96506
date: 2026-10-19
---

**What is true** — measured on the stand on 2026-10-19 (the clio environment `Creatio`, core 10.1.37 .NET
Framework, CrtProcessBuilder 1.6.6.104) with the designer-built probe of
`a-nested-only-files-binding-reads-one-row.md` and its File parameter left unset: the element went to Error with
`System.NullReferenceException` at `Terrasoft.File.FileFactory.CreateFileInstance` (`FileFactory.cs:45`) under
`ProcessFileProcessingUserTask.SaveEntityFiles`, and nothing was copied.

**Why it is this way** — the run time does not check the file locator before it builds the file.

**What breaks if you ignore it** — a guide that says an unset File "copies nothing" is wrong: the process stops with
an error. The guide must say the File has to be set before the element runs. The builder cannot refuse it when it
saves, because whether a parameter is set is a run-time fact.
