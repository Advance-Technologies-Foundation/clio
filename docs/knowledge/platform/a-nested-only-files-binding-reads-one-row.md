---
description: a Process parameter Process file whose Files.File alone is bound to a single File (outer Files unbound, the designer's nested-only binding) copies exactly one file - measured on the stand with a designer-built probe
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/McpServer/Prompts/ProcessDesigner/CreateBusinessProcessPrompt.cs
  - clio.mcp.e2e/ProcessFileReportAndParameterToolE2ETests.cs
ticket: ENG-96506
date: 2026-10-19
---

**What is true** — measured on the stand on 2026-10-19 (the clio environment `Creatio`, core 10.1.37 .NET
Framework, CrtProcessBuilder 1.6.6.104) with a designer-built probe whose `Files` had no source and whose
`Files.File` was `[#FileParam#]`, a File process parameter set by a script task: the run completed and exactly ONE
new attachment row was created on the target, a copy of the source file with its Notes inherited; the source rows
were unchanged. What happens when that File is unset is a separate fact, recorded in
`an-unset-single-file-fails-a-process-parameter-file-element.md`.

**Why it is this way** — the run time reads an unbound outer collection as one row and takes the nested item for it.

**What breaks if you ignore it** — refusing the single-File source ("wrap one file in a FileCollection") would forbid
what the designer itself writes; CrtProcessBuilder 1.6.6.105 therefore binds a single File as `Files.File` only, and
describe reads it as `bindingLevels: itemOnly`.
