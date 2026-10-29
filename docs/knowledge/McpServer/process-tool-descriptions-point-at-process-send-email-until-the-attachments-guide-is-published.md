---
description: TEMPORARY - create-business-process and modify-business-process name get-guidance name=process-send-email for email.attachments, not process-send-email-attachments, because UngatedMcpTools_ShouldNameOnlyUngatedGuidance refuses a guide name the pinned curated-knowledge-names.json does not publish; swap both when the fixture is re-pinned
applies-to:
  - clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/ModifyBusinessProcessTool.cs
  - clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json
  - clio.tests/Command/McpServer/CreateBusinessProcessToolTests.cs
  - clio.tests/Command/McpServer/ModifyBusinessProcessToolTests.cs
ticket: ENG-95985
date: 2026-10-29
---

**What is true** — the `email.attachments` clause of the create contract and the replace-rule clause of the modify
contract point at `process-send-email`, an article that is published today and routes onward. The article that
owns attachments, `process-send-email-attachments`, ships in a clio-knowledge pull request that has not merged.
The three process prompts already name the new article: no test scans prompts against the curated names.

**Why it is this way** — `WorkspaceTemplateGuidanceDriftTests.UngatedMcpTools_ShouldNameOnlyUngatedGuidance_WhenDirectingAgentsToRead`
fails on any `get-guidance name=` in an ungated tool description that the pinned generation does not publish, and
the pin may move only to a generation that is really published.

**What breaks if you ignore it** — leaving the interim name after the re-pin keeps agents one hop away from the
rules of the list (order, replace, empty-file failure). The swap costs 12 bytes in each of the two contracts; the
re-measure in `ToolContractPayloadBudgetTests` was taken with the final name (create 35015, modify 35009 of
35072), so the swap needs no new trim. Swap the two names, the two description pins, re-pin the fixture, then
delete this record.
