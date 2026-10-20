---
description: the Process parameter Process file (ProcessFileProcessingUserTask) throws NotSupportedException for ResultActionType 1 (use in process) - it can only save files to a record's attachments, so the builder refuses useInProcess, requires saveTo and always stores ResultActionType 0
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/McpServer/Prompts/ProcessDesigner/CreateBusinessProcessPrompt.cs
  - clio/Command/McpServer/Prompts/ProcessDesigner/ModifyBusinessProcessPrompt.cs
ticket: ENG-96506
date: 2026-10-19
---

**What is true** — basis: source, NOT measured. `ProcessFileProcessingUserTask` handles only
`ResultActionTypeEnum.SaveToFiles` and throws `NotSupportedException` for every other value, `UseInProcess` (1)
included (`CrtProcessDesigner/branches/7.8.0/Schemas/ProcessFileProcessingUserTask/ProcessFileProcessingUserTask.cs:94`).
Its designer page hides the choice and forces `0`. Unlike the other two variants it has no "use the files in the
process" mode at all; its `ObjectFiles` output holds the COPIES it saved.

**Why it is this way** — the element exists to attach files the process already holds; handing them on is what the
source parameter itself does.

**What breaks if you ignore it** — an element stored with action 1, or with a bound action that yields 1, saves green
and throws on its first run. CrtProcessBuilder 1.6.6.105 refuses `useInProcess` on the `files` group, requires
`saveTo`, and repairs any other stored action to `0` with a notice; a guide or prompt that offers "use in process"
for this variant sends the agent into that refusal.
