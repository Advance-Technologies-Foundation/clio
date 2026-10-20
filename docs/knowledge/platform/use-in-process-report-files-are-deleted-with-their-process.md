---
description: the files a Generated report Process file makes with "use in process" (ReportFiles) are temporary SysProcessFile rows - present while the process instance is parked, gone when it completes; measured for a top-level process, the sub-process case is NOT measured
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/McpServer/Prompts/ProcessDesigner/CreateBusinessProcessPrompt.cs
ticket: ENG-96506
date: 2026-10-19
---

**What is true** — measured on the stand on 2026-10-19 (the clio environment `Creatio`, CrtProcessBuilder 1.6.6.105)
with a builder-made probe, for the top-level case: a Generated report element using its files in the process, then a Process parameter element
saving `ReportFiles` to a Contact, then a Perform task that parks the instance. While it was parked there was 1
`SysProcessFile` row; after the activity was completed and the instance finished there were 0. The copy the Process
parameter element saved (a ContactFile row) stayed. Whether a SUB-process's report files go when the sub-process
completes, as the builder's notice says, is read from source and not measured.

**Why it is this way** — a generated report is attached to no record until an element saves it; the platform keeps
it as a process file owned by the instance.

**What breaks if you ignore it** — mapping `ReportFiles` into an Out parameter, or back to a caller, and reading it
after the process ends reads files that no longer exist. Consume or save them inside the same instance; the
builder's "temporary files" notice on an Out mapping exists for this.
