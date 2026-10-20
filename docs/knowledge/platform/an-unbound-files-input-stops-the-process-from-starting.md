---
description: a Process parameter Process file whose Files input is bound to nothing (Files and Files.File both at source None) makes the platform refuse to START the process - "Process is not valid. Fix invalid elements" ValidateException - while Files bound to a FileCollection that is merely empty completes and copies nothing
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/McpServer/Prompts/ProcessDesigner/DescribeProcessPrompt.cs
  - clio/Command/McpServer/Prompts/ProcessDesigner/ModifyBusinessProcessPrompt.cs
ticket: ENG-96506
date: 2026-10-19
---

**What is true** — measured on the stand on 2026-10-19 (the clio environment `Creatio`, .NET Framework,
CrtProcessBuilder 1.6.6.105) with one probe process, Start → Process parameter element saving to a contact → End:

- **Files bound to a FileCollection process parameter that was never set:** the element and the process completed,
  and nothing was copied.
- **Files cleared in the designer, so Files and Files.File are both stored with no source:**
  - the designer marks Files as required ("Enter a value") and warns that the process cannot start, but it still saves;
  - `run-process` is then refused before anything runs: "Process is not valid. Fix invalid elements "Save docs"
    before starting it." (`ValidateException`).

**Why it is this way** — Files is a required parameter of `ProcessFileProcessingUserTask`, and the platform
validates required element parameters when a process starts, not when it is saved. An empty collection passes that
check, because the parameter IS bound; only its value is empty.

**What breaks if you ignore it** — a notice or guide that says an unbound element "copies nothing and reports
success" is wrong. The whole process fails to start, so steps before the element do not run either. Describe's
`bindingLevels: none` has to be treated as a broken element, not as a harmless no-op.
