---
description: TEMPORARY - until ENG-96506 (Generated report + process parameter modes) only Object attachments is a fileProcessing variant; ReportFileProcessingUserTask and ProcessFileProcessingUserTask still build through the generic userTask route, describe as a generic usertask and take setFilter through DataNodeFilterTarget; the report and files groups are declared but refused. Delete this record with the RP change.
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio/Command/McpServer/Tools/ListUserTasksTool.cs
  - clio/Command/McpServer/Prompts/ListUserTasksPrompt.cs
  - clio/Command/ProcessModel/Schema.cs
ticket: ENG-96505
date: 2026-10-15
---

**What is true** — read in the package source at tag `crtprocessbuilder-1.6.6.103`
(`FileProcessingVariantRegistry.RegisteredVariants`, `UserTaskElementHandler.RefuseFileProcessingOnGenericRoute`).
The cut registers ONE Process file variant, `ObjectFileProcessingVariant`, so only `ObjectFileProcessingUserTask`
is claimed: built with `type: "fileProcessing"` and an `attachments` group, refused on the generic `userTask` route,
described with a `fileProcessing` block, filtered through `FileProcessingFilterTarget`.
`ReportFileProcessingUserTask` and `ProcessFileProcessingUserTask` keep today's behaviour: the generic route builds
them drawn but unconfigured, describe returns them as a generic `usertask` with raw parameters, and `setFilter` goes
to the generic `DataNodeFilterTarget`. The `report` and `files` groups exist in the contract (so WCF does not drop
them) but are refused as not supported yet.

**Why it is this way** — decision D13 scopes each refusal to the cut that makes the variant configurable: refusing
the generic route for Report and Process before ENG-96506 ships their groups would leave them with no route at all.

**What breaks if you ignore it** — clio guidance, prompts or tests that say "the Process file element is refused as
a generic userTask" overstate this cut: two of the three schemas still build that way, and such an element saves
green and fails at run time (an empty `ReportId` or `TargetEntitySchemaUId` throws). Guidance written for the
`report` / `files` groups is wrong until ENG-96506 lands. **Delete this record in the ENG-96506 clio PR (CL-RP)**,
which registers the other two variants and adds their generic-route refusal.
