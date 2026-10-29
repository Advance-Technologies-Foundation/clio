---
description: TEMPORARY - the create/modify-business-process descriptions and prompts name get-guidance name=process-send-email for email.attachments, not process-send-email-attachments, because that guide is not published yet and UngatedMcpTools_ShouldNameOnlyUngatedGuidance refuses a guide name the pinned curated-knowledge-names.json does not publish; until it ships, no guide gives agents the attachments rules
applies-to:
  - clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/ModifyBusinessProcessTool.cs
  - clio/Command/McpServer/Prompts/ProcessDesigner/CreateBusinessProcessPrompt.cs
  - clio/Command/McpServer/Prompts/ProcessDesigner/ModifyBusinessProcessPrompt.cs
  - clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json
  - clio.tests/Command/McpServer/CreateBusinessProcessToolTests.cs
  - clio.tests/Command/McpServer/ModifyBusinessProcessToolTests.cs
ticket: ENG-95985
date: 2026-10-29
---

**What is true** — the `email.attachments` clause of the create contract, the replace-rule clause of the modify
contract and the matching sentences of the create and modify prompts all point at `process-send-email`, an article
that is published today. It does not route onward to an attachments article: it only keeps the pointer from
answering "unknown guide". The article that owns attachments, `process-send-email-attachments`, does not exist
yet; it will ship in a clio-knowledge pull request.

**Why it is this way** — `WorkspaceTemplateGuidanceDriftTests.UngatedMcpTools_ShouldNameOnlyUngatedGuidance_WhenDirectingAgentsToRead`
fails on any `get-guidance name=` in an ungated tool description that the pinned generation does not publish, and
the pin may move only to a generation that is really published. No test scans the prompts, which is why they once
named the unpublished guide; they now agree with the descriptions so the swap is one change.

**What breaks if you ignore it** — if this clio release ships before the guide is published, agents get the member's
shape from the contract and no attachments rules from guidance: the source order, the replace rule (a sent list
replaces every row, `[]` removes them all) and the empty-file failure of a single File reach them only through
refusals and warnings after the call. Once the guide is published: switch the two descriptions, the two prompts and
their test pins to `process-send-email-attachments`, re-pin the fixture, then delete this record. The switch costs
12 bytes in each of the two contracts; `ToolContractPayloadBudgetTests` records the headroom measured with the
interim name (create 34997, modify 34997 of 35072), so it needs no new trim.
